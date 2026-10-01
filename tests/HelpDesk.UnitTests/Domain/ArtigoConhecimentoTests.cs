using HelpDesk.Domain.Conhecimento;

namespace HelpDesk.UnitTests.Domain;

public sealed class ArtigoConhecimentoTests
{
    private static readonly DateTimeOffset _agora = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Criar_DadosValidos_NasceAtivoComTextoAparado()
    {
        var artigo = ArtigoConhecimento.Criar("  Permissões do financeiro ", "\n## Sintoma\n\nErro 403.\n", 2, _agora);

        artigo.Titulo.ShouldBe("Permissões do financeiro");
        artigo.Conteudo.ShouldBe("## Sintoma\n\nErro 403.");
        artigo.Ativo.ShouldBeTrue();
        artigo.CriadoEm.ShouldBe(_agora);
        artigo.AtualizadoEm.ShouldBe(_agora);
        artigo.Id.Version.ShouldBe(7);
    }

    [Theory]
    [InlineData(" ", "conteúdo")]
    [InlineData("Título", "")]
    public void Criar_TituloOuConteudoVazio_Lanca(string titulo, string conteudo)
    {
        Should.Throw<ArgumentException>(() => ArtigoConhecimento.Criar(titulo, conteudo, null, _agora));
    }

    [Fact]
    public void Criar_TituloAcimaDoLimite_Lanca()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            ArtigoConhecimento.Criar(new string('x', ArtigoConhecimento.TituloTamanhoMaximo + 1), "conteúdo", null, _agora));
    }

    [Fact]
    public void Desativar_ArtigoAtivo_SaiDoIndiceEAtualizaAData()
    {
        var artigo = ArtigoConhecimento.Criar("Título", "conteúdo", null, _agora);

        artigo.Desativar(_agora.AddDays(1));

        artigo.Ativo.ShouldBeFalse();
        artigo.AtualizadoEm.ShouldBe(_agora.AddDays(1));
    }
}
