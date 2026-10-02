using System.Diagnostics;
using System.Runtime.CompilerServices;
using HelpDesk.Application.Categorias;
using HelpDesk.Application.Chamados;
using HelpDesk.Application.Conhecimento;
using HelpDesk.Application.Triagem;
using HelpDesk.Domain.Erros;

namespace HelpDesk.Application.Copiloto;

/// <summary>Uma mensagem do histórico, que fica no cliente (P-08): <c>usuario</c> ou <c>assistente</c>.</summary>
public sealed record MensagemCopiloto(string Papel, string Conteudo)
{
    public const string PapelUsuario = "usuario";
    public const string PapelAssistente = "assistente";
}

/// <summary>Os eventos do stream do copiloto (contrato §copiloto, ADR-0012). O <c>erro</c> é da API.</summary>
public abstract record EventoCopiloto;

public sealed record EventoFerramenta(string Nome, string Fase, string? Descricao, int? Resultados) : EventoCopiloto
{
    public const string FaseIniciada = "iniciada";
    public const string FaseConcluida = "concluida";
}

/// <summary>Texto da resposta, já filtrado pelo guardrail de saída (ADR-0020).</summary>
public sealed record EventoDelta(string Texto) : EventoCopiloto;

/// <summary>Só fontes verificadas: citadas na resposta e devolvidas pelas ferramentas.</summary>
public sealed record EventoFontes(IReadOnlyList<FonteCopiloto> Itens) : EventoCopiloto;

public sealed record EventoAviso(string Tipo, IReadOnlyList<string>? Referencias = null) : EventoCopiloto
{
    public const string TipoReferenciaNaoVerificada = "referencia_nao_verificada";
    public const string TipoRespostaTruncada = "resposta_truncada";
}

public sealed record EventoFim(long? TokensEntrada, long? TokensSaida) : EventoCopiloto;

/// <summary>Um passo do provedor, já traduzido do SDK: texto, uma ferramenta começando ou terminando, ou o fim.</summary>
public abstract record PassoCopiloto;

public sealed record PassoTexto(string Texto) : PassoCopiloto;

public sealed record PassoFerramentaIniciada(string Nome) : PassoCopiloto;

/// <summary><paramref name="Resultados"/>: quantos itens a ferramenta devolveu; nulo se falhou.</summary>
public sealed record PassoFerramentaConcluida(string Nome, int? Resultados) : PassoCopiloto;

public sealed record PassoFim(bool Truncada, long? TokensEntrada, long? TokensSaida) : PassoCopiloto;

/// <summary>
/// O agente do copiloto (ADR-0004): o modelo escolhe as ferramentas, até <c>3</c> rodadas por pergunta, com
/// limite de tokens de saída (ADR-0021). Recebe só o prompt mascarado. Uma falha do provedor sobe como
/// <see cref="IaIndisponivelException"/>.
/// </summary>
public interface ICopilotoLlm
{
    IAsyncEnumerable<PassoCopiloto> ConversarAsync(
        PromptCopiloto prompt, FerramentasCopiloto ferramentas, Guid chamadoId, CancellationToken cancellationToken);
}

/// <summary>Uma pergunta validada e pronta para o provedor: o resultado de <see cref="ConversarComCopiloto.PrepararAsync"/>.</summary>
public sealed class ConversaCopiloto
{
    internal ConversaCopiloto(Guid chamadoId, long numero, string solicitanteNome, PromptCopiloto prompt)
    {
        ChamadoId = chamadoId;
        Numero = numero;
        SolicitanteNome = solicitanteNome;
        Prompt = prompt;
    }

    public Guid ChamadoId { get; }

    public long Numero { get; }

    internal string SolicitanteNome { get; }

    internal PromptCopiloto Prompt { get; }
}

/// <summary>
/// Conversar com o copiloto sobre um chamado (RF-20..24). Em dois passos, porque o stream SSE não pode mudar o
/// status HTTP depois de começar (ADR-0012):
/// <list type="number">
/// <item><see cref="PrepararAsync"/>: kill switch (503), validação das mensagens (422), chamado (404) e prompt.</item>
/// <item><see cref="ResponderAsync"/>: os eventos, com a saída do modelo passando pelo <see cref="FiltroSaidaCopiloto"/>
/// (PII mascarada no stream, citações verificadas) antes de sair.</item>
/// </list>
/// O span <c>copiloto.responder</c> leva só contagens e resultados, nunca texto (ADR-0019, ADR-0020).
/// </summary>
public sealed class ConversarComCopiloto(
    OpcoesIA opcoesIA,
    IConsultaChamados chamados,
    MontadorPromptCopiloto montador,
    ICopilotoLlm copiloto,
    IConsultasCopiloto consultas,
    IGeradorEmbeddings gerador,
    IConsultaCategorias categorias,
    MascaradorDadosPessoais mascarador,
    OpcoesRag opcoesRag)
{
    public const string NomeFonteAtividades = "HelpDesk.Copiloto";
    public const int MensagensMaximo = 20;
    public const int MensagemTamanhoMaximo = 2000;

    private static readonly ActivitySource _fonte = new(NomeFonteAtividades);

    public async Task<ConversaCopiloto> PrepararAsync(
        Guid chamadoId, IReadOnlyList<MensagemCopiloto>? mensagens, CancellationToken cancellationToken)
    {
        if (!opcoesIA.CopilotoHabilitado)
        {
            throw new IaIndisponivelException("O copiloto está desativado no momento.");
        }

        var validas = Validar(mensagens);
        var chamado = (await chamados.ObterDetalheAsync(chamadoId, cancellationToken))?.Chamado
            ?? throw RecursoNaoEncontradoException.Chamado(chamadoId);
        var prompt = await montador.MontarAsync(chamado, validas, cancellationToken);
        return new ConversaCopiloto(chamado.Id, chamado.Numero, chamado.SolicitanteNome, prompt);
    }

    public async IAsyncEnumerable<EventoCopiloto> ResponderAsync(
        ConversaCopiloto conversa, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var atividade = _fonte.StartActivity("copiloto.responder");
        atividade?.SetTag("chamado.id", conversa.ChamadoId);
        atividade?.SetTag("prompt.versao", conversa.Prompt.Versao);
        atividade?.SetTag("copiloto.mensagens", conversa.Prompt.Mensagens.Count);

        var ferramentas = new FerramentasCopiloto(conversa.ChamadoId, consultas, gerador, categorias, mascarador,
            opcoesRag);
        var filtro = new FiltroSaidaCopiloto(mascarador, [conversa.SolicitanteNome]);
        var chamadasDeFerramenta = 0;
        PassoFim? fim = null;

        await using var passos = copiloto.ConversarAsync(conversa.Prompt, ferramentas, conversa.ChamadoId,
            cancellationToken).GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            // Num iterador assíncrono, o Activity.Current volta ao de quem consome a cada passo: sem reafirmar, as
            // rodadas de ferramenta e as chamadas ao provedor não ficariam penduradas neste span.
            if (atividade is not null)
            {
                Activity.Current = atividade;
            }

            if (!await passos.MoveNextAsync())
            {
                break;
            }

            switch (passos.Current)
            {
                case PassoFerramentaIniciada iniciada:
                    chamadasDeFerramenta++;
                    yield return new EventoFerramenta(iniciada.Nome, EventoFerramenta.FaseIniciada,
                        Descricao(iniciada.Nome), null);
                    break;
                case PassoFerramentaConcluida concluida:
                    yield return new EventoFerramenta(concluida.Nome, EventoFerramenta.FaseConcluida, null,
                        concluida.Resultados);
                    break;
                case PassoTexto texto when filtro.Processar(texto.Texto) is { Length: > 0 } liberado:
                    yield return new EventoDelta(liberado);
                    break;
                case PassoFim passoFim:
                    fim = passoFim;
                    break;
            }
        }

        if (filtro.Finalizar() is { Length: > 0 } resto)
        {
            yield return new EventoDelta(resto);
        }

        var verificacao = filtro.VerificarCitacoes(ferramentas.Fontes, [conversa.Numero]);
        yield return new EventoFontes(verificacao.Fontes);
        if (verificacao.NaoVerificadas.Count > 0)
        {
            yield return new EventoAviso(EventoAviso.TipoReferenciaNaoVerificada, verificacao.NaoVerificadas);
        }

        if (fim?.Truncada == true)
        {
            yield return new EventoAviso(EventoAviso.TipoRespostaTruncada);
        }

        atividade?.SetTag("copiloto.ferramentas", chamadasDeFerramenta);
        atividade?.SetTag("guardrail.mascaramentos_saida", filtro.Mascaramentos.Total);
        atividade?.SetTag("guardrail.citacoes_nao_verificadas", verificacao.NaoVerificadas.Count);
        atividade?.SetTag("copiloto.truncada", fim?.Truncada ?? false);
        yield return new EventoFim(fim?.TokensEntrada, fim?.TokensSaida);
    }

    /// <summary>Limites do contrato: 1 a 20 mensagens, cada uma com 1 a 2.000 caracteres, terminando no atendente.</summary>
    private static List<MensagemCopiloto> Validar(IReadOnlyList<MensagemCopiloto>? mensagens)
    {
        var erros = new List<string>();
        if (mensagens is not { Count: > 0 })
        {
            erros.Add("Envie pelo menos uma mensagem.");
        }
        else
        {
            if (mensagens.Count > MensagensMaximo)
            {
                erros.Add($"Envie no máximo {MensagensMaximo} mensagens.");
            }

            if (mensagens.Any(m => m.Papel is not (MensagemCopiloto.PapelUsuario or MensagemCopiloto.PapelAssistente)))
            {
                erros.Add("O papel de cada mensagem deve ser 'usuario' ou 'assistente'.");
            }

            if (mensagens.Any(m => string.IsNullOrWhiteSpace(m.Conteudo)
                || m.Conteudo.Trim().EnumerateRunes().Count() > MensagemTamanhoMaximo))
            {
                erros.Add($"Cada mensagem deve ter entre 1 e {MensagemTamanhoMaximo} caracteres.");
            }

            if (mensagens[^1].Papel != MensagemCopiloto.PapelUsuario)
            {
                erros.Add("A última mensagem deve ser do atendente.");
            }
        }

        return erros.Count == 0
            ? [.. mensagens!.Select(m => m with { Conteudo = m.Conteudo.Trim() })]
            : throw new ValidacaoException(new Dictionary<string, string[]> { ["Mensagens"] = [.. erros] });
    }

    private static string? Descricao(string nome) =>
        FerramentasCopiloto.Todas.FirstOrDefault(f => f.Nome == nome)?.Descricao;
}
