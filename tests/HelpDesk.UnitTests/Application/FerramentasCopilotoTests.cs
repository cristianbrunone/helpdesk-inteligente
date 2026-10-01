using HelpDesk.Application;
using HelpDesk.Application.Categorias;
using HelpDesk.Application.Conhecimento;
using HelpDesk.Application.Copiloto;
using HelpDesk.Application.Triagem;
using HelpDesk.Domain.Chamados;

namespace HelpDesk.UnitTests.Application;

public sealed class FerramentasCopilotoTests
{
    private const string Cpf = "529.982.247-25";
    private static readonly Guid _chamadoAtual = Guid.CreateVersion7();
    private static readonly DateTimeOffset _base = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly OpcoesRag _opcoes = new(3, 0.35);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BuscarChamadosSimilares_ConsultaComCpf_MascaraAntesDoEmbedding()
    {
        var gerador = new GeradorFixo();

        await Criar(new ConsultasFixas(), gerador).BuscarChamadosSimilaresAsync($"boleto do CPF {Cpf}", null, null, Ct);

        gerador.Recebido.ShouldNotBeNull().ShouldNotContain(Cpf);
        gerador.Recebido.ShouldContain(MascaradorDadosPessoais.MarcadorCpf);
    }

    [Fact]
    public async Task BuscarChamadosSimilares_SemLimite_UsaOPadraoOModeloDoGeradorEExcluiOChamadoAtual()
    {
        var consultas = new ConsultasFixas();

        await Criar(consultas).BuscarChamadosSimilaresAsync("erro 403", null, null, Ct);

        consultas.Limite.ShouldBe(FerramentasCopiloto.LimitePadrao);
        consultas.Modelo.ShouldBe("modelo-x");
        consultas.SimilaridadeMinima.ShouldBe(_opcoes.SimilaridadeMinima);
        consultas.Exceto.ShouldBe(_chamadoAtual);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public async Task BuscarChamadosSimilares_LimiteForaDe1A5_RecusaOParametro(int limite)
    {
        var consultas = new ConsultasFixas();

        var erro = await Should.ThrowAsync<ParametroFerramentaInvalidoException>(() =>
            Criar(consultas).BuscarChamadosSimilaresAsync("erro 403", null, limite, Ct));

        erro.Message.ShouldContain("entre 1 e 5");
        consultas.Limite.ShouldBeNull(); // nem chegou a consultar
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task BuscarArtigos_ConsultaVazia_RecusaOParametro(string? consulta)
    {
        var gerador = new GeradorFixo();

        await Should.ThrowAsync<ParametroFerramentaInvalidoException>(() =>
            Criar(new ConsultasFixas(), gerador).BuscarArtigosAsync(consulta, null, Ct));

        gerador.Recebido.ShouldBeNull();
    }

    [Fact]
    public async Task BuscarArtigos_ConsultaLongaDemais_RecusaOParametro() =>
        await Should.ThrowAsync<ParametroFerramentaInvalidoException>(() =>
            Criar(new ConsultasFixas()).BuscarArtigosAsync(new string('a', 501), null, Ct));

    [Fact]
    public async Task BuscarChamadosSimilares_CategoriaSemAcentoNemCaixa_FiltraPeloIdDela()
    {
        var consultas = new ConsultasFixas();

        await Criar(consultas).BuscarChamadosSimilaresAsync("como emitir", "  DUVIDA ", 2, Ct);

        consultas.CategoriaId.ShouldBe((short)4);
        consultas.Limite.ShouldBe(2);
    }

    [Fact]
    public async Task BuscarChamadosSimilares_CategoriaInexistente_RecusaListandoAsValidas()
    {
        var erro = await Should.ThrowAsync<ParametroFerramentaInvalidoException>(() =>
            Criar(new ConsultasFixas()).BuscarChamadosSimilaresAsync("erro", "Recursos Humanos", null, Ct));

        erro.Message.ShouldContain("Financeiro, Dúvida");
    }

    [Fact]
    public async Task BuscarChamadosSimilares_ResultadoComDadoPessoalELongo_VoltaMascaradoECortado()
    {
        var id = Guid.CreateVersion7();
        var consultas = new ConsultasFixas
        {
            Chamados = [new(id, 877, $"Boleto do CPF {Cpf}", $"Contato fulano@empresa.com {new string('x', 900)}", 0.71234)],
        };

        var resultado = (await Criar(consultas).BuscarChamadosSimilaresAsync("boleto", null, null, Ct)).Single();

        resultado.Numero.ShouldBe(877);
        resultado.Titulo.ShouldBe("Boleto do CPF [CPF]");
        resultado.Resumo.ShouldStartWith("Contato [EMAIL] ");
        resultado.Resumo.Length.ShouldBe(FerramentasCopiloto.TrechoTamanhoMaximo);
        resultado.Resumo.ShouldEndWith("…");
        resultado.Similaridade.ShouldBe(0.712);
    }

    [Fact]
    public async Task BuscarChamadosSimilares_ConsultaDevolveMaisQueOLimite_SoOLimiteVoltaEViraFonte()
    {
        var consultas = new ConsultasFixas
        {
            Chamados = [.. Enumerable.Range(1, 4).Select(n => new ChamadoSimilar(Guid.CreateVersion7(), n, $"C{n}", "x", 0.9))],
        };
        var ferramentas = Criar(consultas);

        var resultado = await ferramentas.BuscarChamadosSimilaresAsync("erro", null, 2, Ct);

        resultado.Select(c => c.Numero).ShouldBe([1L, 2L]);
        ferramentas.Fontes.Select(f => f.Numero).ShouldBe([1L, 2L]);
    }

    [Fact]
    public async Task Fontes_MesmaOrigemEmDuasChamadas_ApareceUmaVezNaOrdemEmQueSurgiu()
    {
        var chamado = new ChamadoSimilar(Guid.CreateVersion7(), 877, "Erro 403 em boletos", "x", 0.8);
        var artigo = new TrechoArtigo(Guid.CreateVersion7(), "Boletos: erro 403", "y", 0.7);
        var consultas = new ConsultasFixas { Chamados = [chamado], Artigos = [artigo, artigo with { ConteudoMascarado = "z" }] };
        var ferramentas = Criar(consultas);

        await ferramentas.BuscarChamadosSimilaresAsync("erro 403", null, null, Ct);
        await ferramentas.BuscarArtigosAsync("erro 403", null, Ct);
        await ferramentas.BuscarChamadosSimilaresAsync("boletos", null, null, Ct);

        ferramentas.Fontes.ShouldBe(
        [
            new(FonteCopiloto.TipoChamado, chamado.Id, 877, "Erro 403 em boletos"),
            new(FonteCopiloto.TipoArtigo, artigo.Id, null, "Boletos: erro 403"),
        ]);
    }

    [Fact]
    public async Task ObterHistorico_ComentariosComDadosPessoais_MascaraONomeDoSolicitanteECpf()
    {
        var consultas = new ConsultasFixas
        {
            Historico = Historico([new($"A Maria Souza informou o CPF {Cpf}", _base)]),
        };

        var historico = await Criar(consultas).ObterHistoricoDoChamadoAsync(Ct);

        consultas.HistoricoPedido.ShouldBe(_chamadoAtual);
        historico.Numero.ShouldBe(42);
        historico.StatusAtual.ShouldBe("EmAndamento");
        historico.Mudancas.ShouldBe(
        [
            new(null, "Aberto", _base),
            new("Aberto", "EmAndamento", _base.AddHours(1)),
        ]);
        historico.Comentarios.Single().Texto.ShouldBe("A [NOME] [NOME] informou o CPF [CPF]");
    }

    [Fact]
    public async Task ObterHistorico_MaisDe20Comentarios_DevolveOsMaisRecentesEContaOsOmitidos()
    {
        var comentarios = Enumerable.Range(1, 25)
            .Select(i => new ComentarioHistorico($"comentário {i}", _base.AddMinutes(i)))
            .Reverse() // a ordem de chegada não importa
            .ToList();
        var consultas = new ConsultasFixas { Historico = Historico(comentarios) };

        var historico = await Criar(consultas).ObterHistoricoDoChamadoAsync(Ct);

        historico.Comentarios.Count.ShouldBe(FerramentasCopiloto.ComentariosMaximo);
        historico.Comentarios[0].Texto.ShouldBe("comentário 6");
        historico.Comentarios[^1].Texto.ShouldBe("comentário 25");
        historico.ComentariosOmitidos.ShouldBe(5);
    }

    [Fact]
    public async Task ObterHistorico_ChamadoInexistente_LancaNaoEncontrado() =>
        await Should.ThrowAsync<RecursoNaoEncontradoException>(() =>
            Criar(new ConsultasFixas()).ObterHistoricoDoChamadoAsync(Ct));

    [Fact]
    public async Task ObterMetricas_ComDecisoes_CalculaATaxaDeAceitacaoComONomeOficial()
    {
        var consultas = new ConsultasFixas { Metricas = new(40, 30, 12.345, 6, 2) };

        var metricas = await Criar(consultas).ObterMetricasDaCategoriaAsync("financeiro", Ct);

        consultas.MetricasPedidas.ShouldBe((short)2);
        metricas.ShouldBe(new MetricasCategoriaCopiloto("Financeiro", 40, 30, 12.3, 0.75));
    }

    [Fact]
    public async Task ObterMetricas_SemDecisoesNemResolvidos_DevolveNulosEmVezDeZero()
    {
        var consultas = new ConsultasFixas { Metricas = new(3, 0, null, 0, 0) };

        var metricas = await Criar(consultas).ObterMetricasDaCategoriaAsync("Dúvida", Ct);

        metricas.TempoMedioResolucaoHoras.ShouldBeNull();
        metricas.TaxaAceitacaoIa.ShouldBeNull();
    }

    [Fact]
    public async Task ObterMetricas_SemCategoria_RecusaOParametro() =>
        await Should.ThrowAsync<ParametroFerramentaInvalidoException>(() =>
            Criar(new ConsultasFixas()).ObterMetricasDaCategoriaAsync(" ", Ct));

    private static FerramentasCopiloto Criar(ConsultasFixas consultas, GeradorFixo? gerador = null) =>
        new(_chamadoAtual, consultas, gerador ?? new GeradorFixo(), new CategoriasFixas(), new MascaradorDadosPessoais(),
            _opcoes);

    private static HistoricoChamado Historico(IReadOnlyList<ComentarioHistorico> comentarios) =>
        new(42, StatusChamado.EmAndamento, "Maria Souza",
            [
                new(StatusChamado.Aberto, StatusChamado.EmAndamento, _base.AddHours(1)),
                new(null, StatusChamado.Aberto, _base),
            ],
            comentarios);

    private sealed class CategoriasFixas : IConsultaCategorias
    {
        public Task<IReadOnlyList<CategoriaResumo>> ListarAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CategoriaResumo>>([new(2, "Financeiro"), new(4, "Dúvida")]);
    }

    private sealed class GeradorFixo : IGeradorEmbeddings
    {
        public string? Recebido { get; private set; }

        public string Modelo => "modelo-x";

        public Task<IReadOnlyList<float[]>> GerarAsync(
            IReadOnlyList<TextoMascarado> textos, CancellationToken cancellationToken)
        {
            Recebido = textos.Single().Valor;
            return Task.FromResult<IReadOnlyList<float[]>>([new float[768]]);
        }
    }

    private sealed class ConsultasFixas : IConsultasCopiloto
    {
        public IReadOnlyList<ChamadoSimilar> Chamados { get; init; } = [];

        public IReadOnlyList<TrechoArtigo> Artigos { get; init; } = [];

        public HistoricoChamado? Historico { get; init; }

        public MetricasCategoria Metricas { get; init; } = new(0, 0, null, 0, 0);

        public string? Modelo { get; private set; }

        public short? CategoriaId { get; private set; }

        public int? Limite { get; private set; }

        public double? SimilaridadeMinima { get; private set; }

        public Guid? Exceto { get; private set; }

        public Guid? HistoricoPedido { get; private set; }

        public short? MetricasPedidas { get; private set; }

        public Task<IReadOnlyList<ChamadoSimilar>> BuscarChamadosSimilaresAsync(
            float[] vetor, string modelo, short? categoriaId, int limite, double similaridadeMinima,
            Guid excetoChamadoId, CancellationToken cancellationToken)
        {
            (Modelo, CategoriaId, Limite, SimilaridadeMinima, Exceto) =
                (modelo, categoriaId, limite, similaridadeMinima, excetoChamadoId);
            return Task.FromResult(Chamados);
        }

        public Task<IReadOnlyList<TrechoArtigo>> BuscarArtigosAsync(
            float[] vetor, string modelo, int limite, double similaridadeMinima, CancellationToken cancellationToken)
        {
            (Modelo, Limite) = (modelo, limite);
            return Task.FromResult(Artigos);
        }

        public Task<HistoricoChamado?> ObterHistoricoAsync(Guid chamadoId, CancellationToken cancellationToken)
        {
            HistoricoPedido = chamadoId;
            return Task.FromResult(Historico);
        }

        public Task<MetricasCategoria> ObterMetricasDaCategoriaAsync(
            short categoriaId, CancellationToken cancellationToken)
        {
            MetricasPedidas = categoriaId;
            return Task.FromResult(Metricas);
        }
    }
}
