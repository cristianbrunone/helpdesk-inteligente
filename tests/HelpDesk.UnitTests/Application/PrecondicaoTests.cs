using HelpDesk.Application;
using HelpDesk.Application.Chamados;

namespace HelpDesk.UnitTests.Application;

public sealed class PrecondicaoTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("*")]
    [InlineData("41")]
    [InlineData("10,41")]
    public void ExigirVersao_SemPrecondicaoOuVersaoQueConfere_NaoLanca(string? ifMatch)
    {
        Should.NotThrow(() => Precondicao.ExigirVersao(ifMatch?.Split(','), "41"));
    }

    [Theory]
    [InlineData("40")]
    [InlineData("")]
    public void ExigirVersao_VersaoDiferenteOuIfMatchInvalido_LancaVersaoDesatualizada(string ifMatch)
    {
        var versoes = ifMatch.Split(',', StringSplitOptions.RemoveEmptyEntries);

        Should.Throw<VersaoDesatualizadaException>(() => Precondicao.ExigirVersao(versoes, "41"))
            .Codigo.ShouldBe("versao_desatualizada");
    }
}
