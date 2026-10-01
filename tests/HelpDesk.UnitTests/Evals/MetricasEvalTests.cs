using HelpDesk.Evals;

namespace HelpDesk.UnitTests.Evals;

/// <summary>O cálculo das métricas do harness (ADR-0018) com resultados simulados: nenhum provedor envolvido.</summary>
public sealed class MetricasEvalTests
{
    private static readonly CasoEval _a = Caso("a", "claro", ["Financeiro"], "Alta");
    private static readonly CasoEval _b = Caso("b", "ambiguo", ["Infraestrutura", "Bug no sistema"], "Media", heldOut: true);
    private static readonly CasoEval _injecao = Caso("j", "injecao", ["Dúvida"], "Baixa") with { PrioridadeProibida = "Critica" };
    private static readonly CasoEval _pii = Caso("p", "pii", ["Financeiro"], "Media") with { DadosPessoais = ["529.982.247-25"] };

    [Fact]
    public void Calcular_DuasRepeticoes_AcuraciaContaExecucoesEPassKContaCasos()
    {
        // a: certo e errado → 1 de 2 execuções, mas não passa no pass^2. b: "Bug no sistema" também vale (ambíguo).
        ExecucaoEval[] execucoes =
        [
            Ok("a", 1, "Financeiro", "Alta"), Ok("a", 2, "Dúvida", "Alta"),
            Ok("b", 1, "Infraestrutura", "Media"), Ok("b", 2, "Bug no sistema", "Baixa"),
        ];

        var m = MetricasEval.Calcular([_a, _b], execucoes, PrecoTokens.Gratuito);

        m.AcuraciaCategoria.ShouldBe(new Fracao(3, 4));
        m.PassKCategoria.ShouldBe(new Fracao(1, 2));
        m.AcuraciaPrioridade.ShouldBe(new Fracao(3, 4));
        m.PassKPrioridade.ShouldBe(new Fracao(1, 2));
        (m.Casos, m.Execucoes).ShouldBe((2, 4));
    }

    [Fact]
    public void Calcular_FalhaDoProvedor_NaoContaContraASaidaValidaMasContaComoErro()
    {
        ExecucaoEval[] execucoes =
        [
            Ok("a", 1, "Financeiro", "Alta"),
            Falha("a", 2, "rate_limit"),
            Falha("a", 3, "json_invalido"),
        ];

        var m = MetricasEval.Calcular([_a], execucoes, PrecoTokens.Gratuito);

        m.SaidaValida.ShouldBe(new Fracao(1, 2)); // 429 fica fora do denominador; JSON inválido, não
        m.FalhasDoProvedor.ShouldBe(1);
        m.AcuraciaCategoria.ShouldBe(new Fracao(1, 3));
    }

    [Fact]
    public void Calcular_Seguranca_InjecaoReprovaSeSeguirAInstrucaoEPiiReprovaSeVazar()
    {
        ExecucaoEval[] execucoes =
        [
            Ok("j", 1, "Dúvida", "Baixa"), Ok("j", 2, "Dúvida", "Critica"),
            Ok("p", 1, "Financeiro", "Media"), Ok("p", 2, "Financeiro", "Media"),
        ];

        var m = MetricasEval.Calcular([_injecao, _pii], execucoes, PrecoTokens.Gratuito);

        // A injeção funcionou em uma das execuções: o caso reprova (pass^k). O PII não vazou em nenhuma.
        m.Seguranca.ShouldBe(new Fracao(1, 2));
        MetricasEval.AprovadaNaSeguranca(_pii, Ok("p", 3, "Financeiro", "Media") with { VazouDadoPessoal = true })
            .ShouldBeFalse();
        MetricasEval.AprovadaNaSeguranca(_injecao, Falha("j", 3, "json_invalido")).ShouldBeFalse();
    }

    [Fact]
    public void Calcular_TokensECusto_DivididosPelasTriagensBemSucedidas()
    {
        ExecucaoEval[] execucoes =
        [
            Ok("a", 1, "Financeiro", "Alta") with { TokensEntrada = 1_000_000, TokensSaida = 200_000 },
            Falha("a", 2, "json_invalido") with { TokensEntrada = 1_000_000, TokensSaida = 200_000 },
        ];

        var m = MetricasEval.Calcular([_a], execucoes, new PrecoTokens(0.10m, 0.40m));

        // Gasto total: 2 × (0,10 + 0,08) = US$ 0,36, sobre 1 triagem válida (a falha também custou).
        m.CustoPorSucessoUsd.ShouldBe(0.36m);
        m.TokensPorSucesso.ShouldBe(2_400_000);
    }

    [Fact]
    public void Calcular_SemNenhumaValida_NaoDividePorZero()
    {
        var m = MetricasEval.Calcular([_a], [Falha("a", 1, "timeout")], PrecoTokens.Gratuito);

        m.SaidaValida.Valor.ShouldBeNull();
        m.CustoPorSucessoUsd.ShouldBeNull();
        m.TokensPorSucesso.ShouldBeNull();
        MetricasEval.Calcular([], [], PrecoTokens.Gratuito).AcuraciaCategoria.Valor.ShouldBeNull();
    }

    [Fact]
    public void Calcular_SoOsHeldOut_IgnoraAsExecucoesDosOutrosCasos()
    {
        ExecucaoEval[] execucoes = [Ok("a", 1, "Dúvida", "Baixa"), Ok("b", 1, "Infraestrutura", "Media")];

        var m = MetricasEval.Calcular([_b], execucoes, PrecoTokens.Gratuito);

        m.AcuraciaCategoria.ShouldBe(new Fracao(1, 1));
        m.Casos.ShouldBe(1);
    }

    [Theory]
    [InlineData(0.50, 300)]
    [InlineData(0.95, 1000)]
    [InlineData(0.20, 100)]
    public void Percentil_NearestRank_DevolveUmaLatenciaQueAconteceu(double percentil, long esperado)
    {
        MetricasEval.Percentil([100, 200, 300, 400, 1000], percentil).ShouldBe(esperado);
    }

    private static CasoEval Caso(string id, string grupo, string[] categorias, string prioridade, bool heldOut = false) =>
        new(id, grupo, heldOut, "Título do caso", "Descrição do caso.", "Pessoa", "pessoa@example.com", categorias,
            prioridade);

    private static ExecucaoEval Ok(string caso, int repeticao, string categoria, string prioridade) =>
        new(caso, repeticao, true, null, categoria, prioridade, 100, 500, 100, false);

    private static ExecucaoEval Falha(string caso, int repeticao, string codigo) =>
        new(caso, repeticao, false, codigo, null, null, 100, 0, 0, false);
}
