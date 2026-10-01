using System.Globalization;
using System.Text;

namespace HelpDesk.Evals;

/// <summary>Cabeçalho do relatório: o que foi medido, com qual provedor e configuração.</summary>
internal sealed record CabecalhoRelatorio(
    DateTimeOffset Data,
    string Provedor,
    string ModeloChat,
    string? ModeloEmbedding,
    string VersaoPrompt,
    bool ComRag,
    int TopK,
    double SimilaridadeMinima,
    int Repeticoes,
    PrecoTokens Preco);

/// <summary>
/// O relatório em Markdown para <c>docs/evals/</c> (ADR-0018): configuração, métricas (todos × held-out, com a
/// contagem absoluta) e os erros por caso. Só rótulos e números: nenhum texto de chamado nem de resposta.
/// </summary>
internal static class RelatorioMarkdown
{
    private static readonly CultureInfo _ptBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string NomeDoArquivo(CabecalhoRelatorio cabecalho) =>
        $"{cabecalho.Data:yyyy-MM-dd}-triagem-{cabecalho.VersaoPrompt.Replace("triagem.", "", StringComparison.Ordinal)}" +
        $"-{(cabecalho.ComRag ? "com-rag" : "sem-rag")}.md";

    public static string Gerar(
        CabecalhoRelatorio cabecalho, IReadOnlyList<CasoEval> casos, IReadOnlyList<ExecucaoEval> execucoes)
    {
        var todos = MetricasEval.Calcular(casos, execucoes, cabecalho.Preco);
        var reservados = MetricasEval.Calcular([.. casos.Where(c => c.HeldOut)], execucoes, cabecalho.Preco);
        var k = cabecalho.Repeticoes;
        var emTodas = k == 1 ? "na única execução (pass^1)" : $"nas {k} execuções (pass^{k})";
        var semPreco = cabecalho.Preco == PrecoTokens.Gratuito;

        var md = new StringBuilder();
        md.AppendLine(CultureInfo.InvariantCulture,
            $"# Eval da triagem: {cabecalho.VersaoPrompt} {(cabecalho.ComRag ? "com RAG" : "sem RAG")} ({cabecalho.Data:yyyy-MM-dd})");
        md.AppendLine();
        md.AppendLine("Gerado por `tools/HelpDesk.Evals` ([ADR-0018](../adr/0018-evals-offline-da-ia.md)). Não editar à mão: rode o harness de novo.");
        md.AppendLine();

        md.AppendLine("## Configuração");
        md.AppendLine();
        md.AppendLine("| Item | Valor |");
        md.AppendLine("|---|---|");
        Linha(md, "Provedor", cabecalho.Provedor);
        Linha(md, "Modelo de chat", cabecalho.ModeloChat);
        Linha(md, "Prompt", cabecalho.VersaoPrompt);
        Linha(md, "RAG", cabecalho.ComRag
            ? $"ligado: top-k {cabecalho.TopK} por tipo, similaridade mínima {Numero(cabecalho.SimilaridadeMinima, 2)}, embeddings {cabecalho.ModeloEmbedding}"
            : "desligado");
        Linha(md, "Repetições por caso", k.ToString(_ptBr));
        Linha(md, "Casos", $"{todos.Casos} ({reservados.Casos} held-out)");
        Linha(md, "Execuções", todos.Execucoes.ToString(_ptBr));
        Linha(md, "Preço por milhão de tokens", cabecalho.Preco == PrecoTokens.Gratuito
            ? "não informado (free tier): o custo fica zerado e vale o consumo de tokens"
            : $"entrada US$ {cabecalho.Preco.EntradaPorMilhao.ToString(_ptBr)}, saída US$ {cabecalho.Preco.SaidaPorMilhao.ToString(_ptBr)}");
        md.AppendLine();

        md.AppendLine("## Métricas");
        md.AppendLine();
        md.AppendLine("| Métrica | Todos | Held-out |");
        md.AppendLine("|---|---|---|");
        Linha(md, "Acurácia de categoria", Percentual(todos.AcuraciaCategoria), Percentual(reservados.AcuraciaCategoria));
        Linha(md, $"Categoria certa {emTodas}", Percentual(todos.PassKCategoria),
            Percentual(reservados.PassKCategoria));
        Linha(md, "Acurácia de prioridade", Percentual(todos.AcuraciaPrioridade), Percentual(reservados.AcuraciaPrioridade));
        Linha(md, $"Prioridade certa {emTodas}", Percentual(todos.PassKPrioridade),
            Percentual(reservados.PassKPrioridade));
        Linha(md, "Saída válida (JSON + validação de domínio)", Percentual(todos.SaidaValida), Percentual(reservados.SaidaValida));
        Linha(md, "Falhas do provedor (timeout, 429, 5xx)", todos.FalhasDoProvedor.ToString(_ptBr),
            reservados.FalhasDoProvedor.ToString(_ptBr));
        Linha(md, "Casos de segurança aprovados (injeção e PII)", Contagem(todos.Seguranca), Contagem(reservados.Seguranca));
        Linha(md, "Latência p50 / p95 por triagem", Latencia(todos), Latencia(reservados));
        Linha(md, "Tokens por triagem bem-sucedida", Numero(todos.TokensPorSucesso, 0), Numero(reservados.TokensPorSucesso, 0));
        Linha(md, "Custo por triagem bem-sucedida", semPreco ? "— (sem preço)" : Dolar(todos.CustoPorSucessoUsd),
            semPreco ? "— (sem preço)" : Dolar(reservados.CustoPorSucessoUsd));
        md.AppendLine();
        md.AppendLine("Acurácia: execuções certas sobre o total (falha conta como erro). pass^k: casos certos em todas as k " +
            "execuções. Saída válida: sobre as execuções em que o provedor respondeu. A latência inclui a recuperação do RAG " +
            "e as novas tentativas. Tokens e custo incluem os embeddings e as tentativas que falharam.");
        md.AppendLine();

        md.AppendLine("## Erros por caso");
        md.AppendLine();
        var comErro = casos
            .Select(c => (Caso: c, Execucoes: execucoes.Where(e => e.CasoId == c.Id).OrderBy(e => e.Repeticao).ToList()))
            .Where(x => x.Execucoes.Any(e => Errada(x.Caso, e)))
            .ToList();
        if (comErro.Count == 0)
        {
            md.AppendLine("Nenhum: todas as execuções acertaram categoria e prioridade, e a segurança foi aprovada.");
        }
        else
        {
            md.AppendLine("| Caso | Grupo | Held-out | Esperado | Obtido em cada execução |");
            md.AppendLine("|---|---|---|---|---|");
            foreach (var (caso, doCaso) in comErro)
            {
                var esperado = $"{string.Join(" ou ", caso.Categorias)} / {caso.Prioridade}";
                var obtido = string.Join("; ", doCaso.Select(e => Obtido(caso, e)));
                md.AppendLine(CultureInfo.InvariantCulture,
                    $"| `{caso.Id}` | {caso.Grupo} | {(caso.HeldOut ? "sim" : "não")} | {esperado} | {obtido} |");
            }
        }

        return md.ToString();
    }

    private static bool Errada(CasoEval caso, ExecucaoEval execucao) =>
        !execucao.Valida || !caso.CategoriaCerta(execucao.Categoria) || execucao.Prioridade != caso.Prioridade
        || !MetricasEval.AprovadaNaSeguranca(caso, execucao);

    private static string Obtido(CasoEval caso, ExecucaoEval execucao)
    {
        var texto = execucao.Valida ? $"{execucao.Categoria} / {execucao.Prioridade}" : $"falha ({execucao.Codigo})";
        if (!MetricasEval.AprovadaNaSeguranca(caso, execucao))
        {
            texto += caso.Grupo == CasoEval.GrupoPii ? " **vazou dado pessoal**" : " **seguiu a injeção**";
        }

        return texto;
    }

    private static void Linha(StringBuilder md, params string[] celulas) =>
        md.AppendLine(CultureInfo.InvariantCulture, $"| {string.Join(" | ", celulas)} |");

    private static string Percentual(Fracao fracao) => fracao.Valor is { } valor
        ? $"{valor.ToString("P1", _ptBr)} ({fracao.Acertos}/{fracao.Total})"
        : "—";

    private static string Contagem(Fracao fracao) => fracao.Total == 0 ? "—" : $"{fracao.Acertos}/{fracao.Total}";

    private static string Latencia(MetricasEval metricas) =>
        metricas.Execucoes == 0 ? "—" : $"{metricas.LatenciaP50Ms.ToString("N0", _ptBr)} ms / {metricas.LatenciaP95Ms.ToString("N0", _ptBr)} ms";

    private static string Numero(double? valor, int casas) => valor is { } v ? v.ToString($"N{casas}", _ptBr) : "—";

    private static string Dolar(decimal? valor) => valor is { } v ? $"US$ {v.ToString("0.000000", _ptBr)}" : "—";
}
