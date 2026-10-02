using HelpDesk.Application.Copiloto;
using HelpDesk.Infrastructure.Ia;
using HelpDesk.Infrastructure.Ia.Fake;
using Microsoft.Extensions.AI;

namespace HelpDesk.UnitTests.Infraestrutura;

/// <summary>
/// O roteiro do copiloto no fake, rodando pelo laço real de ferramentas do <c>FunctionInvokingChatClient</c>
/// (ADR-0004): tool call → resultado → resposta em pedaços, sem chave e sem rede.
/// </summary>
public sealed class FakeCopilotoTests
{
    private const string Sistema = """
        Você é o copiloto do atendente.
        ## Chamado em contexto
        Número: #42
        Título: Erro 403 ao emitir boletos
        Categoria: Financeiro
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly List<(string Ferramenta, IDictionary<string, object?> Argumentos)> _invocadas = [];

    [Fact]
    public async Task Conversar_JaTivemosCasosParecidos_ChamaBuscarSimilaresComOAssuntoECitaOsNumeros()
    {
        var (texto, atualizacoes) = await ConversarAsync(new FakeChatClient(), "Já tivemos casos parecidos?");

        var (ferramenta, argumentos) = _invocadas.ShouldHaveSingleItem();
        ferramenta.ShouldBe("buscar_chamados_similares");
        argumentos["consulta"]!.ToString().ShouldBe("Erro 403 ao emitir boletos");
        texto.ShouldContain("#877");
        texto.ShouldContain("#912");
        // O laço de ferramentas aparece no stream: é daqui que saem os eventos "ferramenta" (ADR-0012).
        atualizacoes.SelectMany(a => a.Contents).OfType<FunctionCallContent>().ShouldHaveSingleItem();
        atualizacoes.SelectMany(a => a.Contents).OfType<FunctionResultContent>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Conversar_RespostaFinal_ChegaEmVariosPedacosEOUsoVemNoFim()
    {
        var (texto, atualizacoes) = await ConversarAsync(new FakeChatClient(), "Já tivemos casos parecidos?");

        var pedacosDeTexto = atualizacoes.Where(a => !string.IsNullOrEmpty(a.Text)).ToList();
        pedacosDeTexto.Count.ShouldBeGreaterThan(5);
        pedacosDeTexto.ShouldAllBe(a => a.Text.Length <= FakeCopiloto.TamanhoPedaco);
        string.Concat(pedacosDeTexto.Select(a => a.Text)).ShouldBe(texto);
        atualizacoes.Last().Contents.OfType<UsageContent>().ShouldHaveSingleItem().Details.OutputTokenCount
            .ShouldNotBeNull().ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData("Mude o status para fechado")]
    [InlineData("Pode aceitar a sugestão da IA?")]
    public async Task Conversar_PedidoDeEscrita_RecusaSemChamarFerramenta(string pedido)
    {
        var (texto, _) = await ConversarAsync(new FakeChatClient(), pedido);

        _invocadas.ShouldBeEmpty();
        texto.ShouldBe(FakeCopiloto.RespostaRecusa);
    }

    [Fact]
    public async Task Conversar_MetricasDaCategoria_UsaACategoriaDoChamadoEmContexto()
    {
        var (texto, _) = await ConversarAsync(new FakeChatClient(), "Qual o tempo médio dessa categoria?");

        var (ferramenta, argumentos) = _invocadas.ShouldHaveSingleItem();
        ferramenta.ShouldBe("obter_metricas_da_categoria");
        argumentos["categoria"].ShouldBe("Financeiro");
        texto.ShouldContain("Na categoria Financeiro, há 40 chamados, 30 resolvidos");
    }

    [Fact]
    public async Task Conversar_Historico_ChamaSemArgumentosEResumeOResultado()
    {
        var (texto, _) = await ConversarAsync(new FakeChatClient(), "O que já foi feito neste chamado?");

        _invocadas.ShouldHaveSingleItem().Ferramenta.ShouldBe("obter_historico_do_chamado");
        texto.ShouldContain("1 mudanças de status e 2 comentários; o status atual é EmAndamento");
    }

    [Fact]
    public async Task Conversar_Artigos_CitaOArtigoPeloTitulo()
    {
        var (texto, _) = await ConversarAsync(new FakeChatClient(), "Tem algum artigo sobre isso?");

        _invocadas.ShouldHaveSingleItem().Ferramenta.ShouldBe("buscar_artigos");
        texto.ShouldContain("\"Erro 403 no módulo de boletos\"");
    }

    [Fact]
    public async Task Conversar_ModoVazaDados_CpfCruzaPedacosECitaChamadoInexistente()
    {
        var (texto, atualizacoes) = await ConversarAsync(new FakeChatClient(ModoFake.VazaDados),
            "Já tivemos casos parecidos?");

        texto.ShouldContain(FakeCopiloto.CpfVazado);
        texto.ShouldContain($"#{FakeCopiloto.NumeroInventado}");
        // Nenhum pedaço sozinho tem o CPF inteiro: é o cenário que o buffer do guardrail precisa cobrir.
        atualizacoes.ShouldAllBe(a => !a.Text.Contains(FakeCopiloto.CpfVazado));
    }

    [Fact]
    public async Task Conversar_ComOrcamentoDeSaidaPequeno_CortaETerminaPorLimite()
    {
        var (texto, atualizacoes) = await ConversarAsync(new FakeChatClient(), "Já tivemos casos parecidos?",
            maxTokens: 10);

        texto.Length.ShouldBe(40);
        atualizacoes.Last(a => a.FinishReason is not null).FinishReason.ShouldBe(ChatFinishReason.Length);
    }

    [Fact]
    public async Task Conversar_PerguntaSemIntencaoConhecida_ExplicaOQueSabeFazer()
    {
        var (texto, _) = await ConversarAsync(new FakeChatClient(), "Bom dia!");

        _invocadas.ShouldBeEmpty();
        texto.ShouldBe(FakeCopiloto.RespostaAjuda);
    }

    [Fact]
    public async Task Responder_SemFerramentas_SegueSendoATriagem()
    {
        var resposta = await new FakeChatClient().GetResponseAsync(
            [new(ChatRole.System, "## Categorias válidas\n- Financeiro"), new(ChatRole.User, "<chamado>\nTítulo: boleto")],
            cancellationToken: Ct);

        resposta.Text.ShouldStartWith("{\"categoria\":\"Financeiro\"");
    }

    private async Task<(string Texto, List<ChatResponseUpdate> Atualizacoes)> ConversarAsync(
        FakeChatClient fake, string pergunta, int? maxTokens = null)
    {
        using var cliente = new ChatClientBuilder(fake)
            .UseFunctionInvocation(configure: f => f.MaximumIterationsPerRequest = 3)
            .Build();
        var opcoes = new ChatOptions { Tools = Ferramentas(), MaxOutputTokens = maxTokens };

        var atualizacoes = new List<ChatResponseUpdate>();
        await foreach (var atualizacao in cliente.GetStreamingResponseAsync(
            [new(ChatRole.System, Sistema), new(ChatRole.User, pergunta)], opcoes, Ct))
        {
            atualizacoes.Add(atualizacao);
        }

        return (string.Concat(atualizacoes.Select(a => a.Text)), atualizacoes);
    }

    private List<AITool> Ferramentas() =>
    [
        AIFunctionFactory.Create((string consulta, string? categoria = null, int? limite = null) =>
        {
            Invocada("buscar_chamados_similares", ("consulta", consulta), ("categoria", categoria), ("limite", limite));
            return new[]
            {
                new ChamadoSimilarCopiloto(877, "Erro 403 ao abrir boletos", "Resolvido com novo certificado.", 0.71),
                new ChamadoSimilarCopiloto(912, "Boleto não abre", "Cache do navegador.", 0.6),
            };
        }, "buscar_chamados_similares"),
        AIFunctionFactory.Create((string consulta, int? limite = null) =>
        {
            Invocada("buscar_artigos", ("consulta", consulta), ("limite", limite));
            return new[] { new TrechoArtigoCopiloto("Erro 403 no módulo de boletos", "Passo 1...", 0.69) };
        }, "buscar_artigos"),
        AIFunctionFactory.Create(() =>
        {
            Invocada("obter_historico_do_chamado");
            return new HistoricoCopiloto(42, "EmAndamento",
                [new MudancaStatusCopiloto("Aberto", "EmAndamento", DateTimeOffset.UnixEpoch)],
                [new ComentarioCopiloto("a", DateTimeOffset.UnixEpoch), new ComentarioCopiloto("b", DateTimeOffset.UnixEpoch)],
                0);
        }, "obter_historico_do_chamado"),
        AIFunctionFactory.Create((string categoria) =>
        {
            Invocada("obter_metricas_da_categoria", ("categoria", categoria));
            return new MetricasCategoriaCopiloto(categoria, 40, 30, 12.3, 0.75);
        }, "obter_metricas_da_categoria"),
    ];

    private void Invocada(string ferramenta, params (string Nome, object? Valor)[] argumentos) =>
        _invocadas.Add((ferramenta, argumentos.ToDictionary(a => a.Nome, a => a.Valor)));
}
