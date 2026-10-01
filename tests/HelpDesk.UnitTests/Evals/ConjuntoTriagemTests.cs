using System.Text.Json;
using HelpDesk.Infrastructure.Persistencia.Seed;

namespace HelpDesk.UnitTests.Evals;

/// <summary>
/// O conjunto rotulado de <c>evals/triagem/casos.jsonl</c> (ADR-0018): composição, rótulos coerentes com o domínio
/// e independência em relação aos textos do seed.
/// </summary>
public sealed class ConjuntoTriagemTests
{
    private static readonly string[] _categorias =
        ["Acesso/Login", "Financeiro", "Bug no sistema", "Dúvida", "Infraestrutura"];

    private static readonly string[] _prioridades = ["Baixa", "Media", "Alta", "Critica"];

    private static readonly IReadOnlyList<Caso> _casos = Carregar();

    [Fact]
    public void Conjunto_Composicao_SegueOAdr0018()
    {
        _casos.Count.ShouldBe(30);
        _casos.CountBy(c => c.Grupo).ToDictionary().ShouldBe(new Dictionary<string, int>
        {
            ["claro"] = 15,
            ["ambiguo"] = 6,
            ["prioridade"] = 4,
            ["injecao"] = 3,
            ["pii"] = 2,
        });
        _casos.Select(c => c.Id).ShouldBeUnique();
    }

    [Fact]
    public void Conjunto_Claros_TemTresPorCategoriaComUmaSoCategoriaCerta()
    {
        var claros = _casos.Where(c => c.Grupo == "claro").ToList();

        claros.ShouldAllBe(c => c.Categorias.Length == 1);
        claros.GroupBy(c => c.Categorias[0]).ShouldAllBe(g => g.Count() == 3);
        claros.Select(c => c.Categorias[0]).Distinct().Order().ShouldBe(_categorias.Order());
    }

    [Fact]
    public void Conjunto_HeldOut_DezCasosEmTodosOsGrupos()
    {
        var reservados = _casos.Where(c => c.HeldOut).ToList();

        reservados.Count.ShouldBe(10);
        reservados.Select(c => c.Grupo).Distinct().Count().ShouldBe(5);
        // Um claro reservado por categoria: o ajuste do prompt não pode "ver" nenhuma categoria inteira.
        reservados.Where(c => c.Grupo == "claro").Select(c => c.Categorias[0]).ShouldBeUnique();
    }

    [Fact]
    public void Conjunto_Rotulos_UsamCategoriasEPrioridadesDoDominio()
    {
        _casos.ShouldAllBe(c => c.Categorias.Length >= 1 && c.Categorias.All(cat => _categorias.Contains(cat)));
        _casos.ShouldAllBe(c => _prioridades.Contains(c.Prioridade));
        _casos.Where(c => c.Grupo == "ambiguo").ShouldAllBe(c => c.Categorias.Length == 2);
        _casos.ShouldAllBe(c => c.Titulo.Length >= 5 && c.Titulo.Length <= 150);
        _casos.ShouldAllBe(c => c.Descricao.Length >= 10 && c.Descricao.Length <= 5000);
        _casos.ShouldAllBe(c => c.SolicitanteEmail.EndsWith("@example.com"));
    }

    [Fact]
    public void Conjunto_Seguranca_InjecaoTemPrioridadeProibidaEPiiTemOsDadosNoTexto()
    {
        foreach (var caso in _casos.Where(c => c.Grupo == "injecao"))
        {
            caso.PrioridadeProibida.ShouldNotBeNull();
            caso.PrioridadeProibida.ShouldNotBe(caso.Prioridade);
            caso.Descricao.ShouldContain(caso.PrioridadeProibida, Case.Insensitive);
        }

        foreach (var caso in _casos.Where(c => c.Grupo == "pii"))
        {
            caso.DadosPessoais.ShouldNotBeNull().ShouldNotBeEmpty();
            // Todo dado listado está no chamado (texto ou cadastro): é o que o sistema poderia vazar.
            caso.DadosPessoais.ShouldAllBe(d => caso.Descricao.Contains(d) || caso.SolicitanteNome.Contains(d)
                || caso.SolicitanteEmail == d);
        }

        _casos.Where(c => c.Grupo is not ("injecao" or "pii"))
            .ShouldAllBe(c => c.PrioridadeProibida == null && c.DadosPessoais == null);
    }

    [Fact]
    public void Conjunto_Textos_SaoIndependentesDosModelosDoSeed()
    {
        // ADR-0018: avaliar com os próprios exemplos do seed inflaria o resultado (o RAG os recuperaria inteiros).
        var trechosDoSeed = ModelosChamado.PorCategoria.Values.SelectMany(m => m)
            .SelectMany(m => new[] { m.Titulo, m.Descricao, m.Resolucao })
            .Select(t => t.Split('{')[0].Trim())
            .Where(t => t.Length >= 20)
            .ToList();

        foreach (var caso in _casos)
        {
            var texto = $"{caso.Titulo} {caso.Descricao}";
            trechosDoSeed.ShouldAllBe(trecho => !texto.Contains(trecho, StringComparison.OrdinalIgnoreCase),
                $"O caso {caso.Id} reaproveita um texto do seed.");
        }
    }

    private static List<Caso> Carregar()
    {
        var caminho = LocalizarNaRaiz(Path.Combine("evals", "triagem", "casos.jsonl"));
        var opcoes = new JsonSerializerOptions(JsonSerializerDefaults.Web) { UnmappedMemberHandling = default };
        return
        [
            .. File.ReadLines(caminho)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => JsonSerializer.Deserialize<Caso>(l, opcoes)!),
        ];
    }

    /// <summary>Sobe a partir da pasta do teste até achar o arquivo (a raiz do repositório).</summary>
    private static string LocalizarNaRaiz(string relativo)
    {
        for (var pasta = new DirectoryInfo(AppContext.BaseDirectory); pasta is not null; pasta = pasta.Parent)
        {
            var candidato = Path.Combine(pasta.FullName, relativo);
            if (File.Exists(candidato))
            {
                return candidato;
            }
        }

        throw new FileNotFoundException($"Arquivo {relativo} não encontrado acima de {AppContext.BaseDirectory}.");
    }

    private sealed record Caso(
        string Id,
        string Grupo,
        bool HeldOut,
        string Titulo,
        string Descricao,
        string SolicitanteNome,
        string SolicitanteEmail,
        string[] Categorias,
        string Prioridade,
        string? PrioridadeProibida,
        string[]? DadosPessoais);
}
