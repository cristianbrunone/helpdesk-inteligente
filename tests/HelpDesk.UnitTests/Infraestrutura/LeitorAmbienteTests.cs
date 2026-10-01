using HelpDesk.Infrastructure.Configuracao;

namespace HelpDesk.UnitTests.Infraestrutura;

public sealed class LeitorAmbienteTests
{
    private static LeitorAmbiente Com(string chave, string? valor) =>
        new(c => c == chave ? valor : null);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Booleano_AusenteOuVazio_UsaOPadrao(string? valor)
    {
        // "IA_TRIAGEM_HABILITADA=" no .env é "não configurado", não "false" (ADR-0023).
        Com("X", valor).Booleano("X", padrao: true).ShouldBeTrue();
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData(" False ", false)]
    [InlineData("TRUE", true)]
    public void Booleano_ValorValido_IgnoraCaixaEEspacos(string valor, bool esperado)
    {
        Com("X", valor).Booleano("X", padrao: !esperado).ShouldBe(esperado);
    }

    [Theory]
    [InlineData("talvez")]
    [InlineData("0")]
    [InlineData("sim")]
    public void Booleano_ValorInvalido_FalhaNaSubidaComMensagemClara(string valor)
    {
        Should.Throw<InvalidOperationException>(() => Com("IA_TRIAGEM_HABILITADA", valor)
                .Booleano("IA_TRIAGEM_HABILITADA", padrao: true))
            .Message.ShouldBe($"A variável IA_TRIAGEM_HABILITADA tem o valor '{valor}', mas deve ser 'true' ou 'false'.");
    }

    [Theory]
    [InlineData(null, 800)]
    [InlineData("", 800)]
    [InlineData("1500", 1500)]
    public void Inteiro_AusenteVazioOuValido_DevolveOValorCerto(string? valor, int esperado)
    {
        Com("X", valor).Inteiro("X", padrao: 800, minimo: 1, maximo: 10_000).ShouldBe(esperado);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("10001")]
    [InlineData("1,5")]
    [InlineData("mil")]
    public void Inteiro_ForaDaFaixaOuNaoNumerico_FalhaNaSubida(string valor)
    {
        Should.Throw<InvalidOperationException>(() => Com("X", valor).Inteiro("X", 800, 1, 10_000))
            .Message.ShouldContain("um inteiro entre 1 e 10000");
    }

    [Fact]
    public void OpcoesIA_SemVariaveis_DeixaTudoHabilitado()
    {
        var opcoes = new LeitorAmbiente(_ => null).OpcoesIA();

        opcoes.TriagemHabilitada.ShouldBeTrue();
        opcoes.CopilotoHabilitado.ShouldBeTrue();
    }

    [Fact]
    public void OpcoesIA_TriagemDesligada_DesligaSoATriagem()
    {
        var opcoes = Com(LeitorAmbiente.IaTriagemHabilitada, "false").OpcoesIA();

        opcoes.TriagemHabilitada.ShouldBeFalse();
        opcoes.CopilotoHabilitado.ShouldBeTrue();
    }
}
