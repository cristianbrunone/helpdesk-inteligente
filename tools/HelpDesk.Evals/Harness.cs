using System.Globalization;
using HelpDesk.Application.Conhecimento;
using HelpDesk.Infrastructure.Configuracao;
using Microsoft.Extensions.Logging;

namespace HelpDesk.Evals;

/// <summary>Argumentos da linha de comando (exemplos em <see cref="Uso"/>).</summary>
internal sealed record OpcoesEval(
    int Repeticoes,
    bool ComRag,
    string VersaoPrompt,
    string Casos,
    string Saida,
    TimeSpan Intervalo,
    PrecoTokens Preco)
{
    public const string Uso = """
        Uso: dotnet run --project tools/HelpDesk.Evals -- [opções]

          --rag on|off            RAG ligado (padrão on). Com RAG, exige ConnectionStrings__Default (banco indexado).
          --prompt triagem.vN     Versão do prompt (padrão: triagem.v2 com RAG, triagem.v1 sem RAG).
          --repeticoes N          Execuções por caso (padrão 3).
          --casos caminho         Conjunto rotulado (padrão evals/triagem/casos.jsonl).
          --saida pasta           Onde gravar o relatório (padrão docs/evals).
          --intervalo-ms N        Espera entre execuções, para o limite por minuto do provedor (padrão 0).
          --preco-entrada US$     Preço por milhão de tokens de entrada (padrão 0, free tier).
          --preco-saida US$       Preço por milhão de tokens de saída (padrão 0).

        O provedor vem das mesmas variáveis do Worker (LLM_PROVIDER, LLM_BASE_URL, LLM_API_KEY, modelos, RAG_*).
        """;

    public static OpcoesEval Ler(string[] args)
    {
        var valores = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
            {
                throw new ArgumentException($"Argumento inválido: '{args[i]}'.");
            }

            valores[args[i][2..]] = args[++i];
        }

        string? Valor(string nome) => valores.Remove(nome, out var valor) ? valor : null;

        var comRag = Valor("rag") switch
        {
            null or "on" => true,
            "off" => false,
            var outro => throw new ArgumentException($"--rag deve ser on ou off, não '{outro}'."),
        };
        var opcoes = new OpcoesEval(
            Repeticoes: Inteiro(Valor("repeticoes"), "repeticoes", 3, 1, 20),
            ComRag: comRag,
            VersaoPrompt: Valor("prompt") ?? (comRag ? "triagem.v2" : "triagem.v1"),
            Casos: Valor("casos") ?? Path.Combine("evals", "triagem", "casos.jsonl"),
            Saida: Valor("saida") ?? Path.Combine("docs", "evals"),
            Intervalo: TimeSpan.FromMilliseconds(Inteiro(Valor("intervalo-ms"), "intervalo-ms", 0, 0, 120_000)),
            Preco: new PrecoTokens(LerPreco(Valor("preco-entrada"), "preco-entrada"), LerPreco(Valor("preco-saida"), "preco-saida")));

        return valores.Count == 0
            ? opcoes
            : throw new ArgumentException($"Opção desconhecida: --{valores.Keys.First()}.");
    }

    private static int Inteiro(string? valor, string nome, int padrao, int minimo, int maximo) =>
        valor is null ? padrao
        : int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= minimo && n <= maximo ? n
        : throw new ArgumentException($"--{nome} deve ser um inteiro entre {minimo} e {maximo}.");

    private static decimal LerPreco(string? valor, string nome) =>
        valor is null ? 0
        : decimal.TryParse(valor, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var preco) && preco >= 0 ? preco
        : throw new ArgumentException($"--{nome} deve ser um número não negativo, com ponto decimal.");
}

/// <summary>O resultado de uma rodada do harness: as execuções, as métricas de todos os casos e o relatório gravado.</summary>
internal sealed record ResultadoHarness(
    IReadOnlyList<ExecucaoEval> Execucoes, MetricasEval Metricas, string CaminhoRelatorio);

/// <summary>Uma rodada completa: carrega o conjunto, executa o pipeline real N vezes por caso e grava o relatório.</summary>
internal static class Harness
{
    public static async Task<ResultadoHarness> ExecutarAsync(
        OpcoesEval opcoes, LeitorAmbiente ambiente, string? connectionString, ILoggerFactory logs, TextWriter progresso,
        CancellationToken cancellationToken)
    {
        var llm = ambiente.OpcoesLlm();
        var rag = ambiente.OpcoesRag();
        if (opcoes.ComRag && string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Com --rag on, defina ConnectionStrings__Default apontando para o banco já indexado (ex.: o do docker compose).");
        }

        var casos = ConjuntoEval.Carregar(opcoes.Casos);
        await progresso.WriteLineAsync(
            $"Eval: {llm.NomeProvedor} ({llm.ModeloEfetivo}), {opcoes.VersaoPrompt}, RAG {(opcoes.ComRag ? "on" : "off")}, " +
            $"{casos.Count} casos × {opcoes.Repeticoes}");

        var configuracao = new ConfiguracaoEval(llm, rag, opcoes.VersaoPrompt, opcoes.ComRag, connectionString, opcoes.Intervalo);
        IReadOnlyList<ExecucaoEval> execucoes;
        await using (var executor = new ExecutorEval(configuracao, logs))
        {
            // Progresso só com o ID do caso e o rótulo obtido: nada do texto vai para o console.
            execucoes = await executor.ExecutarAsync(casos, opcoes.Repeticoes,
                (caso, e) => progresso.WriteLine(
                    $"  {caso.Id} #{e.Repeticao}: {(e.Valida ? $"{e.Categoria} / {e.Prioridade}" : $"falha ({e.Codigo})")}"),
                cancellationToken);
        }

        var cabecalho = new CabecalhoRelatorio(DateTimeOffset.UtcNow, llm.NomeProvedor, llm.ModeloEfetivo,
            opcoes.ComRag ? llm.ModeloEmbeddingEfetivo : null, opcoes.VersaoPrompt, opcoes.ComRag, rag.TopK,
            rag.SimilaridadeMinima, opcoes.Repeticoes, opcoes.Preco);
        Directory.CreateDirectory(opcoes.Saida);
        var caminho = Path.Combine(opcoes.Saida, RelatorioMarkdown.NomeDoArquivo(cabecalho));
        await File.WriteAllTextAsync(caminho, RelatorioMarkdown.Gerar(cabecalho, casos, execucoes), cancellationToken);

        return new ResultadoHarness(execucoes, MetricasEval.Calcular(casos, execucoes, opcoes.Preco), caminho);
    }
}
