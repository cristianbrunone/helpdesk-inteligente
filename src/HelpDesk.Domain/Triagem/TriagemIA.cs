using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Erros;

namespace HelpDesk.Domain.Triagem;

/// <summary>Sugestão já validada (RF-10, RN-09): só chega aqui o que passou pelo validador da saída do LLM.</summary>
public sealed record SugestaoTriagem(
    short CategoriaId,
    Prioridade Prioridade,
    string Resumo,
    string RespostaSugerida,
    decimal Confianca);

/// <summary>
/// Documento que o RAG recuperou e entregou ao modelo (RF-16): chamado resolvido ou artigo, com a similaridade.
/// Fica gravado na triagem para o atendente ver em que a sugestão se apoiou.
/// </summary>
public sealed record FonteTriagem(string Tipo, Guid Id, long? Numero, string Titulo, double Similaridade);

/// <summary>Com qual provedor, modelo e versão de prompt a triagem foi processada (rastreabilidade e evals).</summary>
public sealed record ExecucaoTriagem(string Provedor, string Modelo, string PromptVersao);

/// <summary>
/// Triagem de um chamado por IA. É também o item da fila do Worker (ADR-0010): "em processamento" não é um status,
/// é <see cref="StatusTriagem.Pendente"/> com lease ativo. Ciclo de vida:
/// Pendente → Concluida | Falhou; Concluida → Aceita | Rejeitada. A vigente é a mais recente (P-04).
/// </summary>
public sealed class TriagemIA
{
    public const int ResumoTamanhoMaximo = 200;
    public const int DecididaPorTamanhoMaximo = 120;
    public const int MotivoRejeicaoTamanhoMaximo = 500;

    public Guid Id { get; private set; }

    public Guid ChamadoId { get; private set; }

    public StatusTriagem Status { get; private set; }

    public short? CategoriaSugeridaId { get; private set; }

    public Prioridade? PrioridadeSugerida { get; private set; }

    public string? Resumo { get; private set; }

    public string? RespostaSugerida { get; private set; }

    public decimal? Confianca { get; private set; }

    /// <summary>Nulos enquanto pendente; preenchidos quando a triagem é processada (com sucesso ou não).</summary>
    public string? Provedor { get; private set; }

    public string? Modelo { get; private set; }

    public string? PromptVersao { get; private set; }

    /// <summary>Motivo amigável da falha (nunca o detalhe técnico, contrato do detalhe).</summary>
    public string? ErroMotivo { get; private set; }

    /// <summary>Quantas vezes o Worker reservou esta triagem (controle da fila e do lease).</summary>
    public short Tentativas { get; private set; }

    public DateTimeOffset ProximaTentativaEm { get; private set; }

    public DateTimeOffset? LockExpiraEm { get; private set; }

    /// <summary>Contexto W3C do trace da criação: o processamento no Worker é vinculado a ele (ADR-0019).</summary>
    public string? TraceParent { get; private set; }

    public DateTimeOffset CriadoEm { get; private set; }

    public DateTimeOffset? ConcluidaEm { get; private set; }

    public DateTimeOffset? DecididaEm { get; private set; }

    public string? DecididaPor { get; private set; }

    public string? MotivoRejeicao { get; private set; }

    /// <summary>Documentos do RAG usados na sugestão (vazia sem RAG ou quando nada passou do limiar).</summary>
    public IReadOnlyList<FonteTriagem> Fontes { get; private set; } = [];

    private TriagemIA(Guid id, Guid chamadoId, DateTimeOffset criadoEm, string? traceParent)
    {
        Id = id;
        ChamadoId = chamadoId;
        Status = StatusTriagem.Pendente;
        CriadoEm = criadoEm;
        ProximaTentativaEm = criadoEm;
        TraceParent = traceParent;
    }

    /// <summary>Triagem pendente de um chamado recém-criado (RF-02): entra na fila na mesma transação do chamado.</summary>
    public static TriagemIA Criar(Chamado chamado, DateTimeOffset agora, string? traceParent = null) =>
        new(Guid.CreateVersion7(agora), chamado.Id, agora, traceParent);

    /// <summary>
    /// "Refazer" (RF-12): cria uma nova triagem pendente, preservando as anteriores (P-04). Chamado finalizado não
    /// aceita (P-11), e só pode existir uma pendente por chamado (índice único parcial no banco também garante).
    /// </summary>
    public static TriagemIA Refazer(Chamado chamado, TriagemIA? vigente, DateTimeOffset agora, string? traceParent = null)
    {
        if (chamado.Finalizado)
        {
            throw new ChamadoFinalizadoException(chamado.Status);
        }

        if (vigente?.Status == StatusTriagem.Pendente)
        {
            throw new TriagemEmAndamentoException();
        }

        return Criar(chamado, agora, traceParent);
    }

    public void Concluir(
        SugestaoTriagem sugestao, ExecucaoTriagem execucao, DateTimeOffset agora,
        IReadOnlyList<FonteTriagem>? fontes = null)
    {
        ExigirPendente();
        // O validador da Application já garantiu isto; aqui é a última defesa do invariante (e do CHECK do banco).
        ArgumentException.ThrowIfNullOrWhiteSpace(sugestao.Resumo);
        ArgumentException.ThrowIfNullOrWhiteSpace(sugestao.RespostaSugerida);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sugestao.Resumo.Length, ResumoTamanhoMaximo);
        if (sugestao.Confianca is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sugestao), "A confiança deve estar entre 0 e 1.");
        }

        CategoriaSugeridaId = sugestao.CategoriaId;
        PrioridadeSugerida = sugestao.Prioridade;
        Resumo = sugestao.Resumo;
        RespostaSugerida = sugestao.RespostaSugerida;
        Confianca = sugestao.Confianca;
        Fontes = fontes ?? [];
        Encerrar(StatusTriagem.Concluida, execucao, agora);
    }

    /// <summary>RF-11: a saída inválida (ou o provedor indisponível) vira <c>Falhou</c> com motivo, nunca exceção.</summary>
    public void Falhar(string motivo, ExecucaoTriagem execucao, DateTimeOffset agora)
    {
        ExigirPendente();
        ArgumentException.ThrowIfNullOrWhiteSpace(motivo);

        ErroMotivo = motivo;
        Encerrar(StatusTriagem.Falhou, execucao, agora);
    }

    /// <summary>RF-13: aplica categoria e prioridade sugeridas ao chamado, na mesma transação.</summary>
    public void Aceitar(Chamado chamado, string? decididaPor, DateTimeOffset agora)
    {
        var autor = ValidarDecisao(chamado, decididaPor);

        chamado.AplicarSugestao(CategoriaSugeridaId!.Value, PrioridadeSugerida!.Value, agora);
        Decidir(StatusTriagem.Aceita, autor, agora);
    }

    /// <summary>RF-14: registra a rejeição sem alterar o chamado. O motivo é insumo para melhorar o prompt.</summary>
    public void Rejeitar(Chamado chamado, string? decididaPor, string? motivo, DateTimeOffset agora)
    {
        var autor = ValidarDecisao(chamado, decididaPor, motivo);

        MotivoRejeicao = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
        Decidir(StatusTriagem.Rejeitada, autor, agora);
    }

    private string ValidarDecisao(Chamado chamado, string? decididaPor, string? motivo = null)
    {
        var erros = new ErrosValidacao();
        var autor = erros.Texto(nameof(DecididaPor), decididaPor, 1, DecididaPorTamanhoMaximo, "quem decidiu");
        if (!string.IsNullOrWhiteSpace(motivo))
        {
            erros.Texto("Motivo", motivo, 1, MotivoRejeicaoTamanhoMaximo, "o motivo");
        }

        erros.LancarSeHouver();

        // RN-08 / P-11: estado final do chamado vem antes do estado da triagem.
        if (chamado.Finalizado)
        {
            throw new ChamadoFinalizadoException(chamado.Status);
        }

        // RN-07: só uma triagem concluída (e ainda não decidida) pode ser aceita ou rejeitada.
        if (Status != StatusTriagem.Concluida)
        {
            throw new TriagemNaoConcluidaException(Status);
        }

        return autor;
    }

    private void Decidir(StatusTriagem status, string autor, DateTimeOffset agora)
    {
        Status = status;
        DecididaPor = autor;
        DecididaEm = agora;
    }

    private void Encerrar(StatusTriagem status, ExecucaoTriagem execucao, DateTimeOffset agora)
    {
        Status = status;
        Provedor = execucao.Provedor;
        Modelo = execucao.Modelo;
        PromptVersao = execucao.PromptVersao;
        ConcluidaEm = agora;
        LockExpiraEm = null;
    }

    private void ExigirPendente()
    {
        if (Status != StatusTriagem.Pendente)
        {
            throw new InvalidOperationException($"A triagem {Id} já foi processada ({Status}).");
        }
    }
}
