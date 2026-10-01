using HelpDesk.Application;
using HelpDesk.Application.Chamados;
using HelpDesk.Domain.Chamados;

namespace HelpDesk.UnitTests.Application;

public sealed class ListarChamadosTests
{
    private static ParametrosListagem Parametros(
        string? q = null,
        string? ordenarPor = null,
        string? direcao = null,
        int? pagina = null,
        int? tamanhoPagina = null,
        DateOnly? de = null,
        DateOnly? ate = null,
        StatusChamado[]? status = null) =>
        new(status ?? [], [], [], false, q, de, ate, ordenarPor, direcao, pagina, tamanhoPagina);

    [Fact]
    public void Validar_SemParametros_AplicaOsPadroesDoContrato()
    {
        var filtro = ListarChamados.Validar(Parametros());

        filtro.OrdenarPor.ShouldBe(OrdenacaoChamados.CriadoEm);
        filtro.Ascendente.ShouldBeFalse();
        filtro.Pagina.ShouldBe(1);
        filtro.TamanhoPagina.ShouldBe(20);
        filtro.Texto.ShouldBeNull();
    }

    [Theory]
    [InlineData("prioridade", "asc", OrdenacaoChamados.Prioridade, true)]
    [InlineData("PRIORIDADE", "DESC", OrdenacaoChamados.Prioridade, false)]
    [InlineData("criadoEm", "asc", OrdenacaoChamados.CriadoEm, true)]
    public void Validar_OrdenacaoEDirecao_AceitaQualquerCaixa(
        string ordenarPor, string direcao, OrdenacaoChamados esperada, bool ascendente)
    {
        var filtro = ListarChamados.Validar(Parametros(ordenarPor: ordenarPor, direcao: direcao));

        filtro.OrdenarPor.ShouldBe(esperada);
        filtro.Ascendente.ShouldBe(ascendente);
    }

    [Theory]
    [InlineData("  boleto  ", "boleto")]
    [InlineData("   ", null)]
    [InlineData("", null)]
    public void Validar_Texto_ApareEIgnoraVazio(string q, string? esperado)
    {
        ListarChamados.Validar(Parametros(q: q)).Texto.ShouldBe(esperado);
    }

    [Fact]
    public void Validar_StatusRepetido_RemoveDuplicados()
    {
        var filtro = ListarChamados.Validar(
            Parametros(status: [StatusChamado.Aberto, StatusChamado.Aberto, StatusChamado.Cancelado]));

        filtro.Status.ShouldBe([StatusChamado.Aberto, StatusChamado.Cancelado]);
    }

    [Fact]
    public void Validar_MesmoDiaNoPeriodo_EValido()
    {
        var dia = new DateOnly(2026, 1, 11);

        ListarChamados.Validar(Parametros(de: dia, ate: dia)).CriadoDe.ShouldBe(dia);
    }

    [Fact]
    public void Validar_VariosProblemas_LancaUm400ComTodos()
    {
        var erro = Should.Throw<RequisicaoInvalidaException>(() =>
            ListarChamados.Validar(Parametros(q: "ab", pagina: 0, tamanhoPagina: 500, ordenarPor: "titulo")));

        erro.Codigo.ShouldBe("requisicao_invalida");
        erro.Message.ShouldContain("'q'");
        erro.Message.ShouldContain("'pagina'");
        erro.Message.ShouldContain("'tamanhoPagina'");
        erro.Message.ShouldContain("'ordenarPor'");
    }

    [Fact]
    public void Validar_PaginaQueEstouraOOffset_Lanca400()
    {
        Should.Throw<RequisicaoInvalidaException>(() =>
            ListarChamados.Validar(Parametros(pagina: int.MaxValue, tamanhoPagina: 100)));
    }
}
