using System.Diagnostics;
using HelpDesk.Application.Categorias;
using HelpDesk.Application.Chamados;
using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Triagem;

namespace HelpDesk.Application.Triagem;

/// <summary>Como terminou o processamento: para o log do Worker (códigos, nunca conteúdo).</summary>
public sealed record ResultadoPipeline(StatusTriagem Status, string? Codigo);

/// <summary>
/// Triagem como pipeline determinístico (ADR-0004): Mascarar → Recuperar → MontarPrompt → Completar → Validar.
/// Cada etapa é um span (ADR-0019) com IDs, versões, contagens e o resultado da validação, nunca texto.
/// <para>
/// Nunca lança (NFR-04, NFR-05): provedor fora, resposta cortada, saída inválida ou erro inesperado viram
/// <c>Falhou</c> com uma mensagem amigável. A única exceção é o cancelamento do próprio Worker: a triagem fica
/// pendente e é retomada quando o lease expirar.
/// </para>
/// </summary>
public sealed class PipelineTriagem(
    MascaradorDadosPessoais mascarador,
    IRecuperadorContexto recuperador,
    MontadorPromptTriagem montador,
    IClienteLlmTriagem cliente,
    IConsultaCategorias categorias,
    TimeProvider relogio)
{
    public const string NomeFonteAtividades = "HelpDesk.Triagem";

    private static readonly ActivitySource _fonte = new(NomeFonteAtividades);

    public async Task<ResultadoPipeline> ProcessarAsync(
        TriagemIA triagem, Chamado chamado, CancellationToken cancellationToken)
    {
        var execucao = new ExecucaoTriagem(cliente.Provedor, cliente.Modelo, montador.Versao);
        try
        {
            var (titulo, descricao) = Mascarar(chamado);
            var contexto = await RecuperarAsync(titulo, descricao, cancellationToken);
            var categoriasValidas = await categorias.ListarAsync(cancellationToken);
            var prompt = await MontarAsync(titulo, descricao, categoriasValidas, contexto, cancellationToken);

            ResultadoLlm resposta;
            using (_fonte.StartActivity("completar"))
            {
                resposta = await cliente.CompletarAsync(prompt, new ContextoTriagem(triagem.Id, chamado.Id),
                    cancellationToken);
            }

            execucao = execucao with { Modelo = resposta.Modelo };
            if (resposta.FalhaTipo is { } tipo)
            {
                return Falhar(triagem, execucao, tipo, MensagemDoProvedor(tipo));
            }

            // ADR-0021: a resposta cortada pelo limite de tokens não é JSON confiável.
            if (resposta.Truncada)
            {
                return Falhar(triagem, execucao, "resposta_truncada", "A resposta da IA excedeu o limite de tamanho.");
            }

            var validacao = Validar(resposta.Texto, categoriasValidas);
            if (validacao.Sugestao is not { } sugestao)
            {
                return Falhar(triagem, execucao, validacao.Codigo!, validacao.Mensagem!);
            }

            triagem.Concluir(sugestao, execucao, relogio.GetUtcNow(), contexto.Fontes);
            return new ResultadoPipeline(StatusTriagem.Concluida, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Defesa final: nenhuma exceção sai do pipeline. O detalhe técnico não vai para a triagem.
            return Falhar(triagem, execucao, "erro_interno",
                "Não foi possível processar a triagem. Tente refazer em alguns instantes.");
        }
    }

    private (TextoMascarado Titulo, TextoMascarado Descricao) Mascarar(Chamado chamado)
    {
        using var etapa = _fonte.StartActivity("mascarar");
        // RN-10: o nome e o e-mail do solicitante nunca vão para o LLM, nem quando aparecem dentro do texto.
        string[] nomes = [chamado.SolicitanteNome];
        var titulo = mascarador.Mascarar(chamado.Titulo, nomes);
        var descricao = mascarador.Mascarar(chamado.Descricao, nomes);

        var total = titulo.Mascaramentos + descricao.Mascaramentos;
        etapa?.SetTag("mascaramento.emails", total.Emails);
        etapa?.SetTag("mascaramento.telefones", total.Telefones);
        etapa?.SetTag("mascaramento.cpfs", total.Cpfs);
        etapa?.SetTag("mascaramento.nomes", total.Nomes);
        return (titulo, descricao);
    }

    /// <summary>
    /// Só recupera se a versão do prompt usa contexto: com a linha de base (sem RAG), não há custo de embedding nem
    /// fontes gravadas que o modelo não viu.
    /// </summary>
    private async Task<ContextoRecuperado> RecuperarAsync(
        TextoMascarado titulo, TextoMascarado descricao, CancellationToken ct)
    {
        using var etapa = _fonte.StartActivity("recuperar");
        if (!await montador.UsaContextoAsync(ct))
        {
            etapa?.SetTag("rag.habilitado", false);
            return ContextoRecuperado.Vazio;
        }

        var contexto = await recuperador.RecuperarAsync(titulo, descricao, ct);
        etapa?.SetTag("rag.habilitado", true);
        etapa?.SetTag("rag.documentos", contexto.Trechos.Count);
        etapa?.SetTag("rag.fontes", contexto.Fontes.Count);
        return contexto;
    }

    private async Task<PromptTriagem> MontarAsync(
        TextoMascarado titulo, TextoMascarado descricao, IReadOnlyList<CategoriaResumo> categoriasValidas,
        ContextoRecuperado contexto, CancellationToken ct)
    {
        using var etapa = _fonte.StartActivity("montar_prompt");
        var prompt = await montador.MontarAsync(titulo, descricao, categoriasValidas, contexto.Trechos, ct);
        etapa?.SetTag("prompt.versao", prompt.Versao);
        etapa?.SetTag("prompt.trechos_contexto", prompt.Contexto.Count);
        return prompt;
    }

    private static ResultadoValidacao Validar(string? texto, IReadOnlyList<CategoriaResumo> categoriasValidas)
    {
        using var etapa = _fonte.StartActivity("validar");
        var resultado = ValidadorSaidaTriagem.Validar(texto, categoriasValidas);
        etapa?.SetTag("validacao.resultado", resultado.Valida ? "valida" : "invalida");
        if (!resultado.Valida)
        {
            etapa?.SetTag("validacao.motivo", resultado.Codigo);
        }

        return resultado;
    }

    private ResultadoPipeline Falhar(TriagemIA triagem, ExecucaoTriagem execucao, string codigo, string mensagem)
    {
        triagem.Falhar(mensagem, execucao, relogio.GetUtcNow());
        Activity.Current?.SetTag("triagem.falha", codigo);
        return new ResultadoPipeline(StatusTriagem.Falhou, codigo);
    }

    private static string MensagemDoProvedor(string tipo) => tipo switch
    {
        "timeout" => "O provedor de IA não respondeu a tempo. Tente refazer a triagem.",
        "rate_limit" => "O provedor de IA atingiu o limite de uso. Tente refazer em alguns minutos.",
        "indisponivel" => "O provedor de IA está indisponível no momento. Tente refazer a triagem.",
        _ => "Não foi possível obter a sugestão da IA. Tente refazer a triagem.",
    };
}
