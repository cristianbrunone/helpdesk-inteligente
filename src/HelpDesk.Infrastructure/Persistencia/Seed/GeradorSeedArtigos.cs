using HelpDesk.Domain.Conhecimento;

namespace HelpDesk.Infrastructure.Persistencia.Seed;

/// <summary>
/// Artigos da base de conhecimento (modelo §7). Sem aleatoriedade: os textos são fixos e as datas ficam espalhadas
/// entre 125 e 5 dias antes de "agora", um artigo a cada 5 dias. Sem embeddings: o reconciliador indexa depois.
/// </summary>
public static class GeradorSeedArtigos
{
    public static IReadOnlyList<ArtigoConhecimento> Gerar(IReadOnlyDictionary<string, short> categorias, DateTimeOffset agora)
    {
        agora = new DateTimeOffset(agora.UtcTicks - (agora.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero);

        return
        [
            .. ModelosArtigo.Todos.Select((modelo, i) => ArtigoConhecimento.Criar(
                modelo.Titulo,
                modelo.Conteudo,
                categorias.TryGetValue(modelo.Categoria, out var id) ? id : null,
                agora.AddDays(-125 + (i * 5)))),
        ];
    }
}
