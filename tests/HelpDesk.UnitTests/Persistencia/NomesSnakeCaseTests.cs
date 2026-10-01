using HelpDesk.Infrastructure.Persistencia;

namespace HelpDesk.UnitTests.Persistencia;

public sealed class NomesSnakeCaseTests
{
    [Theory]
    [InlineData("Id", "id")]
    [InlineData("Nome", "nome")]
    [InlineData("CriadoEm", "criado_em")]
    [InlineData("CategoriaSugeridaId", "categoria_sugerida_id")]
    [InlineData("EmAndamento", "em_andamento")]
    [InlineData("PK_categorias", "pk_categorias")]
    [InlineData("IX_chamados_CategoriaId", "ix_chamados_categoria_id")]
    [InlineData("HTTPStatus", "http_status")]
    [InlineData("ja_em_snake", "ja_em_snake")]
    public void Converter_NomeEmPascalCaseOuMisto_RetornaSnakeCase(string nome, string esperado)
    {
        NomesSnakeCase.Converter(nome).ShouldBe(esperado);
    }
}
