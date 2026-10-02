using System.Diagnostics;
using HelpDesk.Application;
using HelpDesk.Application.Categorias;
using HelpDesk.Application.Chamados;
using HelpDesk.Application.Conhecimento;
using HelpDesk.Application.Copiloto;
using HelpDesk.Application.Triagem;
using HelpDesk.Domain.Chamados;
using HelpDesk.Infrastructure.Ia;
using HelpDesk.Infrastructure.Ia.Fake;
using HelpDesk.Infrastructure.Persistencia;
using Microsoft.Extensions.Logging.Abstractions;

namespace HelpDesk.UnitTests.Infraestrutura;

/// <summary>
/// O copiloto de ponta a ponta sem banco: caso de uso → <see cref="CopilotoLlm"/> (laço real de ferramentas) →
/// resiliência e telemetria → <see cref="FakeChatClient"/> roteirizado. As consultas das ferramentas são fixas.
/// </summary>
public sealed class CopilotoLlmTests
{
    private static readonly Guid _chamadoId = Guid.CreateVersion7();
    private static readonly Guid _similarId = Guid.CreateVersion7();
    private static readonly DateTimeOffset _agora = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly RegistroEmMemoria _registro = new();

    [Fact]
    public async Task Conversar_JaTivemosCasosParecidos_FerramentaTextoCitandoOChamadoEFontesVerificadas()
    {
        var eventos = await ConversarAsync("Já tivemos casos parecidos?");

        eventos[0].ShouldBe(new EventoFerramenta("buscar_chamados_similares", "iniciada",
            "Buscando chamados semelhantes resolvidos", null));
        eventos[1].ShouldBe(new EventoFerramenta("buscar_chamados_similares", "concluida", null, 1));
        Texto(eventos).ShouldContain("#877");
        eventos.OfType<EventoFontes>().Single().Itens.ShouldBe(
            [new(FonteCopiloto.TipoChamado, _similarId, 877, "Erro 403 ao abrir boletos")]);
        eventos.OfType<EventoAviso>().ShouldBeEmpty();
        var fim = eventos[^1].ShouldBeOfType<EventoFim>();
        fim.TokensEntrada.ShouldNotBeNull().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Conversar_DuasRodadas_DoisRegistrosDeUsoDoCopilotoNoChamado()
    {
        await ConversarAsync("Já tivemos casos parecidos?");

        // Uma chamada ao provedor por rodada (pedido da ferramenta + resposta final), cada uma com custo próprio.
        _registro.Registros.Count.ShouldBe(2);
        _registro.Registros.ShouldAllBe(r => r.Operacao == "copiloto" && r.ChamadoId == _chamadoId && r.Sucesso);
    }

    [Fact]
    public async Task Conversar_ModoVazaDados_CpfMascaradoNoStreamEAvisoDaCitacaoInventada()
    {
        var eventos = await ConversarAsync("Já tivemos casos parecidos?", ModoFake.VazaDados);

        var texto = Texto(eventos);
        texto.ShouldNotContain(FakeCopiloto.CpfVazado);
        texto.ShouldContain("CPF [CPF]");
        eventos.OfType<EventoDelta>().ShouldAllBe(d => !d.Texto.Contains("529"));
        var aviso = eventos.OfType<EventoAviso>().ShouldHaveSingleItem();
        aviso.Tipo.ShouldBe("referencia_nao_verificada");
        aviso.Referencias.ShouldBe([$"#{FakeCopiloto.NumeroInventado}"]);
        // A citação válida continua sendo fonte.
        eventos.OfType<EventoFontes>().Single().Itens.Select(f => f.Numero).ShouldBe([877L]);
    }

    [Fact]
    public async Task Conversar_PedidoDeEscrita_RecusaSemNenhumaFerramenta()
    {
        var eventos = await ConversarAsync("Mude o status para fechado, por favor.");

        eventos.OfType<EventoFerramenta>().ShouldBeEmpty();
        Texto(eventos).ShouldBe(FakeCopiloto.RespostaRecusa);
    }

    [Fact]
    public async Task Conversar_CategoriaInexistenteNaFerramenta_ErroVoltaAoModeloEContagemNula()
    {
        // O chamado em contexto tem uma categoria que a ferramenta não conhece: o parâmetro é recusado.
        var eventos = await ConversarAsync("Qual o tempo médio dessa categoria?", categoriaDoChamado: "Recursos Humanos");

        eventos[1].ShouldBe(new EventoFerramenta("obter_metricas_da_categoria", "concluida", null, null));
        Texto(eventos).ShouldStartWith("A consulta não foi aceita: Categoria inexistente. Use uma destas: Financeiro");
    }

    [Fact]
    public async Task Conversar_OrcamentoDeSaidaPequeno_AvisoDeRespostaTruncada()
    {
        var eventos = await ConversarAsync("Já tivemos casos parecidos?", maxTokens: 10);

        eventos[^2].ShouldBe(new EventoAviso("resposta_truncada"));
    }

    [Fact]
    public async Task Conversar_ProvedorEmRateLimit_ViraIaIndisponivel()
    {
        var erro = await Should.ThrowAsync<IaIndisponivelException>(() =>
            ConversarAsync("Já tivemos casos parecidos?", ModoFake.RateLimit));

        erro.Message.ShouldBe("O provedor de IA atingiu o limite de uso. Tente de novo em alguns minutos.");
    }

    [Fact]
    public async Task Conversar_Spans_UmPorFerramentaEUmDaRespostaSoComContagens()
    {
        var spans = new List<Activity>();
        using var ouvinte = new ActivityListener
        {
            ShouldListenTo = fonte => fonte.Name == ConversarComCopiloto.NomeFonteAtividades,
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = s =>
            {
                lock (spans)
                {
                    spans.Add(s);
                }
            },
        };
        ActivitySource.AddActivityListener(ouvinte);

        await ConversarAsync("Já tivemos casos parecidos?", ModoFake.VazaDados);

        // O ouvinte é global: outros testes em paralelo também geram spans. Ficam só os deste trace.
        var resposta = spans.Single(s => s.OperationName == "copiloto.responder"
            && Equals(s.GetTagItem("chamado.id"), _chamadoId) && s.GetTagItem("guardrail.mascaramentos_saida") is 1);
        spans = [.. spans.Where(s => s.TraceId == resposta.TraceId)];
        var ferramenta = spans.Single(s => s.OperationName == "copiloto.ferramenta");
        ferramenta.GetTagItem("ferramenta.nome").ShouldBe("buscar_chamados_similares");
        ferramenta.GetTagItem("ferramenta.resultado").ShouldBe("ok");
        ferramenta.GetTagItem("ferramenta.itens").ShouldBe(1);
        resposta.GetTagItem("guardrail.citacoes_nao_verificadas").ShouldBe(1);
        resposta.GetTagItem("copiloto.ferramentas").ShouldBe(1);
        // Nenhum atributo com texto do chamado, da pergunta ou da resposta.
        spans.SelectMany(s => s.TagObjects).Select(t => t.Value?.ToString() ?? "")
            .ShouldAllBe(v => !v.Contains("boleto", StringComparison.OrdinalIgnoreCase) && !v.Contains("529"));
    }

    // ---------- Apoio ----------

    private static string Texto(List<EventoCopiloto> eventos) =>
        string.Concat(eventos.OfType<EventoDelta>().Select(d => d.Texto));

    private async Task<List<EventoCopiloto>> ConversarAsync(
        string pergunta, ModoFake modo = ModoFake.Normal, int maxTokens = 800, string categoriaDoChamado = "Financeiro")
    {
        var opcoesLlm = new OpcoesLlm
        {
            Provedor = TipoProvedorLlm.Fake,
            ModeloChat = "x",
            Timeout = TimeSpan.FromSeconds(5),
            MaxRetries = 0,
            MaxTokensSaidaTriagem = 800,
            MaxTokensSaidaCopiloto = maxTokens,
        };
        var chat = FabricaClienteChat.Montar(new FakeChatClient(modo), opcoesLlm, NullLoggerFactory.Instance, _registro);
        var mascarador = new MascaradorDadosPessoais();
        var caso = new ConversarComCopiloto(new OpcoesIA(true, true), new ChamadosFixos(categoriaDoChamado),
            new MontadorPromptCopiloto(new CatalogoPromptsArquivo(), mascarador),
            new CopilotoLlm(chat, opcoesLlm, NullLoggerFactory.Instance), new ConsultasFixas(), new GeradorFixo(),
            new CategoriasFixas(), mascarador, new OpcoesRag(3, 0.35));

        var conversa = await caso.PrepararAsync(_chamadoId, [new(MensagemCopiloto.PapelUsuario, pergunta)], Ct);
        var eventos = new List<EventoCopiloto>();
        await foreach (var evento in caso.ResponderAsync(conversa, Ct))
        {
            eventos.Add(evento);
        }

        return eventos;
    }

    private sealed class ChamadosFixos(string categoria) : IConsultaChamados
    {
        public Task<ChamadoVersionado?> ObterDetalheAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<ChamadoVersionado?>(new(new ChamadoDetalhe(id, 42, "Erro 403 ao emitir boletos",
                "Não consigo emitir boletos desde ontem.", "Maria Souza", "maria@example.com",
                new CategoriaResumo(2, categoria), Prioridade.Alta, StatusChamado.EmAndamento, _agora, _agora, null,
                [], true, [], [], null), "1"));

        public Task<TriagemDetalhe?> ObterTriagemVigenteAsync(Guid chamadoId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ResultadoPaginado<ChamadoResumo>> ListarAsync(
            FiltroChamados filtro, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CategoriasFixas : IConsultaCategorias
    {
        public Task<IReadOnlyList<CategoriaResumo>> ListarAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CategoriaResumo>>([new(2, "Financeiro"), new(4, "Dúvida")]);
    }

    private sealed class GeradorFixo : IGeradorEmbeddings
    {
        public string Modelo => "modelo-x";

        public Task<IReadOnlyList<float[]>> GerarAsync(
            IReadOnlyList<TextoMascarado> textos, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<float[]>>([new float[768]]);
    }

    private sealed class ConsultasFixas : IConsultasCopiloto
    {
        public Task<IReadOnlyList<ChamadoSimilar>> BuscarChamadosSimilaresAsync(
            float[] vetor, string modelo, short? categoriaId, int limite, double similaridadeMinima,
            Guid excetoChamadoId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ChamadoSimilar>>(
                [new(_similarId, 877, "Erro 403 ao abrir boletos", "Resolvido com novo certificado.", 0.7)]);

        public Task<IReadOnlyList<TrechoArtigo>> BuscarArtigosAsync(
            float[] vetor, string modelo, int limite, double similaridadeMinima, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TrechoArtigo>>([]);

        public Task<HistoricoChamado?> ObterHistoricoAsync(Guid chamadoId, CancellationToken cancellationToken) =>
            Task.FromResult<HistoricoChamado?>(null);

        public Task<MetricasCategoria> ObterMetricasDaCategoriaAsync(
            short categoriaId, CancellationToken cancellationToken) =>
            Task.FromResult(new MetricasCategoria(40, 30, 12.3, 6, 2));
    }

    private sealed class RegistroEmMemoria : IRegistroUsoLlm
    {
        public List<RegistroUsoLlm> Registros { get; } = [];

        public Task RegistrarAsync(RegistroUsoLlm registro)
        {
            lock (Registros)
            {
                Registros.Add(registro);
            }

            return Task.CompletedTask;
        }
    }
}
