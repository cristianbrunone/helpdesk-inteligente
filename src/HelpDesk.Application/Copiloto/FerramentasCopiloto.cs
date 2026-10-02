using System.Globalization;
using HelpDesk.Application.Categorias;
using HelpDesk.Application.Conhecimento;
using HelpDesk.Application.Triagem;

namespace HelpDesk.Application.Copiloto;

/// <summary>Nome e descrição de uma ferramenta: a descrição vai para o modelo e para o evento <c>ferramenta</c>.</summary>
public sealed record DescricaoFerramenta(string Nome, string Descricao);

/// <summary>Uma fonte que uma ferramenta realmente devolveu nesta resposta (base da verificação de citações, ADR-0020).</summary>
public sealed record FonteCopiloto(string Tipo, Guid Id, long? Numero, string Titulo)
{
    public const string TipoChamado = "chamado";
    public const string TipoArtigo = "artigo";
}

/// <summary>Resultados das ferramentas, no formato que o modelo recebe (JSON). Todo texto já está mascarado.</summary>
public sealed record ChamadoSimilarCopiloto(long Numero, string Titulo, string Resumo, double Similaridade);

public sealed record TrechoArtigoCopiloto(string Titulo, string Trecho, double Similaridade);

public sealed record MudancaStatusCopiloto(string? De, string Para, DateTimeOffset Em);

public sealed record ComentarioCopiloto(string Texto, DateTimeOffset Em);

public sealed record HistoricoCopiloto(
    long Numero,
    string StatusAtual,
    IReadOnlyList<MudancaStatusCopiloto> Mudancas,
    IReadOnlyList<ComentarioCopiloto> Comentarios,
    int ComentariosOmitidos);

public sealed record MetricasCategoriaCopiloto(
    string Categoria,
    int TotalChamados,
    int Resolvidos,
    double? TempoMedioResolucaoHoras,
    double? TaxaAceitacaoIa);

/// <summary>
/// Parâmetro de ferramenta inválido. Não é erro HTTP: a mensagem volta para o modelo como resultado da ferramenta,
/// para ele corrigir a chamada (por exemplo, usar uma categoria que existe).
/// </summary>
public sealed class ParametroFerramentaInvalidoException(string mensagem) : Exception(mensagem);

/// <summary>
/// As 4 ferramentas do copiloto (RF-21, contrato §copiloto), uma instância por pergunta. Todas são <b>somente
/// leitura</b> (RF-22), validam os parâmetros que o modelo escolheu, mascaram o que devolvem (ADR-0004) e limitam o
/// tamanho do resultado. O histórico não recebe ID: o escopo é sempre o chamado aberto na tela, o que reduz a
/// superfície de uma prompt injection.
/// <para>
/// Cada fonte devolvida fica registrada em <see cref="Fontes"/>: o guardrail de saída só aceita citações delas.
/// </para>
/// </summary>
public sealed class FerramentasCopiloto(
    Guid chamadoId,
    IConsultasCopiloto consultas,
    IGeradorEmbeddings gerador,
    IConsultaCategorias categorias,
    MascaradorDadosPessoais mascarador,
    OpcoesRag opcoesRag)
{
    public const int LimitePadrao = 3;
    public const int LimiteMaximo = 5;
    public const int ConsultaTamanhoMaximo = 500;
    public const int TrechoTamanhoMaximo = 600;
    public const int ComentarioTamanhoMaximo = 500;
    public const int ComentariosMaximo = 20;

    public static readonly DescricaoFerramenta BuscarChamadosSimilares = new("buscar_chamados_similares",
        "Buscando chamados semelhantes resolvidos");

    public static readonly DescricaoFerramenta BuscarArtigos = new("buscar_artigos",
        "Buscando artigos da base de conhecimento");

    public static readonly DescricaoFerramenta ObterHistoricoDoChamado = new("obter_historico_do_chamado",
        "Lendo o histórico deste chamado");

    public static readonly DescricaoFerramenta ObterMetricasDaCategoria = new("obter_metricas_da_categoria",
        "Consultando as métricas da categoria");

    public static readonly IReadOnlyList<DescricaoFerramenta> Todas =
        [BuscarChamadosSimilares, BuscarArtigos, ObterHistoricoDoChamado, ObterMetricasDaCategoria];

    private readonly Lock _trava = new();
    private readonly Dictionary<(string, Guid), FonteCopiloto> _fontes = [];

    /// <summary>As fontes devolvidas pelas ferramentas até agora, na ordem em que apareceram.</summary>
    public IReadOnlyList<FonteCopiloto> Fontes
    {
        get
        {
            lock (_trava)
            {
                return [.. _fontes.Values];
            }
        }
    }

    /// <summary><c>buscar_chamados_similares(consulta, categoria?, limite? 1–5)</c>.</summary>
    public async Task<IReadOnlyList<ChamadoSimilarCopiloto>> BuscarChamadosSimilaresAsync(
        string? consulta, string? categoria, int? limite, CancellationToken cancellationToken)
    {
        var quantos = ValidarLimite(limite);
        var vetor = await VetorDaConsultaAsync(consulta, cancellationToken);
        var categoriaId = categoria is null ? null : (short?)(await ResolverCategoriaAsync(categoria, cancellationToken)).Id;

        // Só vira fonte o que de fato volta para o modelo (o Take protege contra uma consulta que ignore o limite).
        var chamados = (await consultas.BuscarChamadosSimilaresAsync(vetor, gerador.Modelo, categoriaId, quantos,
            opcoesRag.SimilaridadeMinima, chamadoId, cancellationToken)).Take(quantos).ToList();
        foreach (var chamado in chamados)
        {
            Registrar(new FonteCopiloto(FonteCopiloto.TipoChamado, chamado.Id, chamado.Numero, chamado.Titulo));
        }

        return [.. chamados.Select(c => new ChamadoSimilarCopiloto(c.Numero, Mascarar(c.Titulo),
            Cortar(Mascarar(c.ConteudoMascarado), TrechoTamanhoMaximo), Math.Round(c.Similaridade, 3)))];
    }

    /// <summary><c>buscar_artigos(consulta, limite? 1–5)</c>.</summary>
    public async Task<IReadOnlyList<TrechoArtigoCopiloto>> BuscarArtigosAsync(
        string? consulta, int? limite, CancellationToken cancellationToken)
    {
        var quantos = ValidarLimite(limite);
        var vetor = await VetorDaConsultaAsync(consulta, cancellationToken);

        var trechos = (await consultas.BuscarArtigosAsync(vetor, gerador.Modelo, quantos,
            opcoesRag.SimilaridadeMinima, cancellationToken)).Take(quantos).ToList();
        foreach (var trecho in trechos)
        {
            Registrar(new FonteCopiloto(FonteCopiloto.TipoArtigo, trecho.Id, null, trecho.Titulo));
        }

        return [.. trechos.Select(t => new TrechoArtigoCopiloto(Mascarar(t.Titulo),
            Cortar(Mascarar(t.ConteudoMascarado), TrechoTamanhoMaximo), Math.Round(t.Similaridade, 3)))];
    }

    /// <summary>
    /// <c>obter_historico_do_chamado()</c>: as mudanças de status e os últimos <see cref="ComentariosMaximo"/>
    /// comentários, sem autores e com o nome do solicitante mascarado.
    /// </summary>
    public async Task<HistoricoCopiloto> ObterHistoricoDoChamadoAsync(CancellationToken cancellationToken)
    {
        var historico = await consultas.ObterHistoricoAsync(chamadoId, cancellationToken)
            ?? throw RecursoNaoEncontradoException.Chamado(chamadoId);
        string[] nomes = [historico.SolicitanteNome];

        var recentes = historico.Comentarios.OrderBy(c => c.Em).TakeLast(ComentariosMaximo).ToList();
        return new HistoricoCopiloto(
            historico.Numero,
            historico.Status.ToString(),
            [.. historico.Mudancas.OrderBy(m => m.Em).Select(m => new MudancaStatusCopiloto(m.De?.ToString(),
                m.Para.ToString(), m.Em))],
            // Mascara antes de cortar: um corte no meio de um CPF deixaria dígitos que o regex não reconhece.
            [.. recentes.Select(c => new ComentarioCopiloto(
                Cortar(mascarador.Mascarar(c.Texto, nomes).Valor, ComentarioTamanhoMaximo), c.Em))],
            historico.Comentarios.Count - recentes.Count);
    }

    /// <summary><c>obter_metricas_da_categoria(categoria)</c>.</summary>
    public async Task<MetricasCategoriaCopiloto> ObterMetricasDaCategoriaAsync(
        string? categoria, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(categoria))
        {
            throw new ParametroFerramentaInvalidoException("Informe a categoria.");
        }

        var encontrada = await ResolverCategoriaAsync(categoria, cancellationToken);
        var metricas = await consultas.ObterMetricasDaCategoriaAsync(encontrada.Id, cancellationToken);
        var decididas = metricas.TriagensAceitas + metricas.TriagensRejeitadas;

        return new MetricasCategoriaCopiloto(
            encontrada.Nome,
            metricas.TotalChamados,
            metricas.Resolvidos,
            metricas.TempoMedioResolucaoHoras is { } horas ? Math.Round(horas, 1) : null,
            decididas == 0 ? null : Math.Round((double)metricas.TriagensAceitas / decididas, 3));
    }

    private static int ValidarLimite(int? limite) => limite switch
    {
        null => LimitePadrao,
        >= 1 and <= LimiteMaximo => limite.Value,
        _ => throw new ParametroFerramentaInvalidoException($"O limite deve estar entre 1 e {LimiteMaximo}."),
    };

    /// <summary>A consulta é texto escolhido pelo modelo: mascarada antes de virar embedding, como qualquer texto.</summary>
    private async Task<float[]> VetorDaConsultaAsync(string? consulta, CancellationToken cancellationToken)
    {
        var texto = consulta?.Trim();
        if (string.IsNullOrEmpty(texto))
        {
            throw new ParametroFerramentaInvalidoException("Informe a consulta.");
        }

        if (texto.Length > ConsultaTamanhoMaximo)
        {
            throw new ParametroFerramentaInvalidoException(
                $"A consulta deve ter no máximo {ConsultaTamanhoMaximo} caracteres.");
        }

        return (await gerador.GerarAsync([mascarador.Mascarar(texto)], cancellationToken))[0];
    }

    /// <summary>Pelo nome, sem diferenciar caixa nem acento ("duvida" encontra "Dúvida").</summary>
    private async Task<CategoriaResumo> ResolverCategoriaAsync(string nome, CancellationToken cancellationToken)
    {
        var todas = await categorias.ListarAsync(cancellationToken);
        return todas.FirstOrDefault(c => string.Compare(c.Nome, nome.Trim(), CultureInfo.InvariantCulture,
                   CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) == 0)
            ?? throw new ParametroFerramentaInvalidoException(
                $"Categoria inexistente. Use uma destas: {string.Join(", ", todas.Select(c => c.Nome))}.");
    }

    private void Registrar(FonteCopiloto fonte)
    {
        lock (_trava)
        {
            _fontes.TryAdd((fonte.Tipo, fonte.Id), fonte);
        }
    }

    private string Mascarar(string texto) => mascarador.Mascarar(texto).Valor;

    private static string Cortar(string texto, int limite) =>
        texto.Length <= limite ? texto : string.Concat(texto.AsSpan(0, limite - 1), "…");
}
