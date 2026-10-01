namespace HelpDesk.Infrastructure.Persistencia;

/// <summary>
/// Livro-razão de toda chamada ao LLM (tabela <c>uso_llm</c>, RF-17, NFR-11): custo, latência e falhas, sem nenhum
/// conteúdo. É registro técnico, não conceito de domínio; a triagem guarda só o resultado.
/// </summary>
public sealed class RegistroUsoLlm
{
    public const string OperacaoTriagem = "triagem";
    public const string OperacaoCopiloto = "copiloto";
    public const string OperacaoEmbedding = "embedding";

    /// <summary>Gerado pelo banco (<c>bigint identity</c>).</summary>
    public long Id { get; private set; }

    public string Operacao { get; private set; }

    public Guid? TriagemId { get; private set; }

    public Guid? ChamadoId { get; private set; }

    public string Provedor { get; private set; }

    public string Modelo { get; private set; }

    public int? TokensEntrada { get; private set; }

    public int? TokensSaida { get; private set; }

    public int LatenciaMs { get; private set; }

    public bool Sucesso { get; private set; }

    /// <summary><c>timeout</c>, <c>rate_limit</c>, <c>indisponivel</c>, <c>formato</c>...; nulo quando houve sucesso.</summary>
    public string? ErroTipo { get; private set; }

    public DateTimeOffset CriadoEm { get; private set; }

    public RegistroUsoLlm(
        string operacao,
        Guid? triagemId,
        Guid? chamadoId,
        string provedor,
        string modelo,
        int? tokensEntrada,
        int? tokensSaida,
        int latenciaMs,
        bool sucesso,
        string? erroTipo,
        DateTimeOffset criadoEm)
    {
        Operacao = operacao;
        TriagemId = triagemId;
        ChamadoId = chamadoId;
        Provedor = provedor;
        Modelo = modelo;
        TokensEntrada = tokensEntrada;
        TokensSaida = tokensSaida;
        LatenciaMs = latenciaMs;
        Sucesso = sucesso;
        ErroTipo = erroTipo;
        CriadoEm = criadoEm;
    }
}
