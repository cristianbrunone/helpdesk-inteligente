using System.Text.RegularExpressions;
using HelpDesk.Domain.Conhecimento;
using HelpDesk.Infrastructure.Persistencia.Seed;

namespace HelpDesk.UnitTests.Persistencia;

public sealed partial class GeradorSeedArtigosTests
{
    private static readonly DateTimeOffset _agora = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<string, short> _categorias = new()
    {
        ["Acesso/Login"] = 1,
        ["Financeiro"] = 2,
        ["Bug no sistema"] = 3,
        ["Dúvida"] = 4,
        ["Infraestrutura"] = 5,
    };

    private static readonly IReadOnlyList<ArtigoConhecimento> _artigos = GeradorSeedArtigos.Gerar(_categorias, _agora);

    [Fact]
    public void Gerar_Padrao_Gera25ArtigosAtivosCincoPorCategoria()
    {
        _artigos.Count.ShouldBe(25);
        _artigos.ShouldAllBe(a => a.Ativo);
        _artigos.GroupBy(a => a.CategoriaId).ShouldAllBe(g => g.Key != null && g.Count() == 5);
        _artigos.Select(a => a.Titulo).ShouldBeUnique();
    }

    [Fact]
    public void Gerar_Conteudo_TemSecoesMarkdownParaOChunking()
    {
        // ADR-0011: o chunking é por seção; todo artigo precisa de pelo menos duas.
        _artigos.ShouldAllBe(a => Secao().Matches(a.Conteudo).Count >= 2);
        _artigos.ShouldAllBe(a => a.Conteudo.StartsWith("## "));
    }

    [Fact]
    public void Gerar_Conteudo_NaoTemDadoPessoal()
    {
        _artigos.ShouldAllBe(a => !a.Conteudo.Contains('@') && !Cpf().IsMatch(a.Conteudo) && !Telefone().IsMatch(a.Conteudo));
    }

    [Fact]
    public void Gerar_Datas_FicamNoPassadoESemRepetir()
    {
        _artigos.ShouldAllBe(a => a.CriadoEm < _agora && a.CriadoEm == a.AtualizadoEm);
        _artigos.Select(a => a.CriadoEm).ShouldBeUnique();
    }

    [Fact]
    public void Gerar_TemaDoCriterioDeAceite_TemArtigoFinanceiroSobre403EmBoletos()
    {
        // Critério da Sprint 3: "erro 403 em boletos" recupera artigos financeiros.
        _artigos.ShouldContain(a => a.CategoriaId == _categorias["Financeiro"]
            && a.Conteudo.Contains("403") && a.Conteudo.Contains("boletos"));
    }

    [GeneratedRegex(@"^## \S", RegexOptions.Multiline)]
    private static partial Regex Secao();

    [GeneratedRegex(@"\d{3}\.\d{3}\.\d{3}-\d{2}")]
    private static partial Regex Cpf();

    [GeneratedRegex(@"\(\d{2}\) ?9?\d{4}-\d{4}")]
    private static partial Regex Telefone();
}
