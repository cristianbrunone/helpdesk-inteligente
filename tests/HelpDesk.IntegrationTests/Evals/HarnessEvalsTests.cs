using HelpDesk.Evals;
using HelpDesk.Infrastructure.Configuracao;
using HelpDesk.IntegrationTests.Conhecimento;
using Microsoft.Extensions.Logging.Abstractions;

namespace HelpDesk.IntegrationTests.Evals;

/// <summary>
/// O harness de evals de ponta a ponta com o provedor fake e o conjunto real (é o mesmo smoke do CI, ADR-0018):
/// não mede qualidade, garante que o harness e o pipeline continuam funcionando juntos.
/// </summary>
public sealed class HarnessEvalsTests(BuscaSemanticaTests.IndiceDoSeed indice)
    : IClassFixture<BuscaSemanticaTests.IndiceDoSeed>, IDisposable
{
    private readonly string _saida = Path.Combine(Path.GetTempPath(), $"evals-{Guid.NewGuid():N}");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Executar_SemRag_RodaOsTrintaCasosEGravaORelatorio()
    {
        var resultado = await ExecutarAsync(["--rag", "off", "--repeticoes", "1"], connectionString: null);

        resultado.Execucoes.Count.ShouldBe(30);
        resultado.Metricas.SaidaValida.ShouldBe(new Fracao(30, 30));
        // Os dados pessoais são mascarados antes do provedor: os casos de PII passam até com o fake.
        resultado.Metricas.Seguranca.Total.ShouldBe(5);
        resultado.Execucoes.ShouldAllBe(e => !e.VazouDadoPessoal);
        Path.GetFileName(resultado.CaminhoRelatorio).ShouldEndWith("-triagem-v1-sem-rag.md");
        (await File.ReadAllTextAsync(resultado.CaminhoRelatorio, Ct)).ShouldContain("| RAG | desligado |");
    }

    [Fact]
    public async Task Executar_ComRag_UsaOIndiceEContaOsTokensDosEmbeddings()
    {
        var semRag = await ExecutarAsync(["--rag", "off", "--repeticoes", "1"], connectionString: null);
        var comRag = await ExecutarAsync(["--rag", "on", "--repeticoes", "1"], indice.ConnectionString);

        comRag.Metricas.SaidaValida.ShouldBe(new Fracao(30, 30));
        // A v2 recebe o contexto: mais tokens por triagem (o prompt maior e o embedding da consulta).
        comRag.Metricas.TokensPorSucesso.ShouldNotBeNull().ShouldBeGreaterThan(semRag.Metricas.TokensPorSucesso!.Value);
        var relatorio = await File.ReadAllTextAsync(comRag.CaminhoRelatorio, Ct);
        relatorio.ShouldContain("| Prompt | triagem.v2 |");
        relatorio.ShouldContain("ligado: top-k 3 por tipo");
    }

    [Fact]
    public async Task Executar_RelatorioGravado_NaoTemTextoDosChamadosNemDadosPessoais()
    {
        var resultado = await ExecutarAsync(["--rag", "off", "--repeticoes", "1"], connectionString: null);

        var relatorio = await File.ReadAllTextAsync(resultado.CaminhoRelatorio, Ct);
        foreach (var proibido in new[] { "529.982.247-25", "Joana", "Rafael", "98765-4321", "Ignore todas as regras" })
        {
            relatorio.ShouldNotContain(proibido);
        }
    }

    [Fact]
    public async Task Executar_ComRagSemBanco_FalhaComMensagemClara()
    {
        var erro = await Should.ThrowAsync<InvalidOperationException>(() =>
            ExecutarAsync(["--rag", "on", "--repeticoes", "1"], connectionString: null));

        erro.Message.ShouldContain("ConnectionStrings__Default");
    }

    public void Dispose()
    {
        if (Directory.Exists(_saida))
        {
            Directory.Delete(_saida, recursive: true);
        }
    }

    private Task<ResultadoHarness> ExecutarAsync(string[] args, string? connectionString)
    {
        var opcoes = OpcoesEval.Ler([.. args, "--casos", LocalizarConjunto(), "--saida", _saida]);
        // Sempre o fake: o CLAUDE.md proíbe provedor real nos testes que rodam no CI.
        var ambiente = new LeitorAmbiente(chave => chave == LeitorAmbiente.LlmProvider ? "fake" : null);
        return Harness.ExecutarAsync(opcoes, ambiente, connectionString, NullLoggerFactory.Instance, TextWriter.Null, Ct);
    }

    private static string LocalizarConjunto()
    {
        for (var pasta = new DirectoryInfo(AppContext.BaseDirectory); pasta is not null; pasta = pasta.Parent)
        {
            var candidato = Path.Combine(pasta.FullName, "evals", "triagem", "casos.jsonl");
            if (File.Exists(candidato))
            {
                return candidato;
            }
        }

        throw new FileNotFoundException("evals/triagem/casos.jsonl não encontrado acima da pasta dos testes.");
    }
}
