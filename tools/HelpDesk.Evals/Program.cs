using HelpDesk.Infrastructure.Configuracao;
using Microsoft.Extensions.Logging.Abstractions;

namespace HelpDesk.Evals;

/// <summary>
/// Harness de evals offline da triagem (ADR-0018). Classe nomeada (e não top-level) para não colidir com o
/// <c>Program</c> da API nos testes de integração, que referenciam os dois projetos. Exemplos:
/// <code>
/// LLM_PROVIDER=fake dotnet run --project tools/HelpDesk.Evals -- --rag off --repeticoes 1
/// (com o .env carregado) dotnet run --project tools/HelpDesk.Evals -- --rag on --repeticoes 3 --intervalo-ms 4500
/// </code>
/// </summary>
internal static class Programa
{
    public static async Task<int> Main(string[] args)
    {
        OpcoesEval opcoes;
        try
        {
            opcoes = OpcoesEval.Ler(args);
        }
        catch (ArgumentException erro)
        {
            await Console.Error.WriteLineAsync(erro.Message);
            await Console.Error.WriteLineAsync(OpcoesEval.Uso);
            return 1;
        }

        try
        {
            // Mesmas variáveis do Worker; a chave de API nunca é impressa (LeitorAmbiente e OpcoesLlm.ToString).
            var ambiente = new LeitorAmbiente(Environment.GetEnvironmentVariable);
            var resultado = await Harness.ExecutarAsync(opcoes, ambiente,
                Environment.GetEnvironmentVariable("ConnectionStrings__Default"), NullLoggerFactory.Instance,
                Console.Out, CancellationToken.None);

            var m = resultado.Metricas;
            await Console.Out.WriteLineAsync();
            await Console.Out.WriteLineAsync(
                $"Categoria: {m.AcuraciaCategoria.Acertos}/{m.AcuraciaCategoria.Total} · " +
                $"prioridade: {m.AcuraciaPrioridade.Acertos}/{m.AcuraciaPrioridade.Total} · " +
                $"saída válida: {m.SaidaValida.Acertos}/{m.SaidaValida.Total} · " +
                $"segurança: {m.Seguranca.Acertos}/{m.Seguranca.Total}");
            await Console.Out.WriteLineAsync($"Relatório: {resultado.CaminhoRelatorio}");

            // Smoke do CI: o harness precisa produzir pelo menos uma triagem válida.
            return m.SaidaValida.Acertos > 0 ? 0 : 3;
        }
        catch (Exception erro) when (erro is InvalidOperationException or InvalidDataException or FileNotFoundException)
        {
            await Console.Error.WriteLineAsync(erro.Message);
            return 2;
        }
    }
}
