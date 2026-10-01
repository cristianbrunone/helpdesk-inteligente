using HelpDesk.Application.Conhecimento;
using HelpDesk.Application.Triagem;
using HelpDesk.Infrastructure.Ia;
using Microsoft.Extensions.Logging.Abstractions;

namespace HelpDesk.UnitTests.Infraestrutura;

public sealed class RecuperadorRagTests
{
    private static readonly MascaradorDadosPessoais _mascarador = new();
    private static readonly OpcoesRag _opcoes = new(3, 0.35);
    private static readonly Guid _artigo = Guid.CreateVersion7();
    private static readonly Guid _chamado = Guid.CreateVersion7();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Recuperar_VariosTrechosDoMesmoArtigo_ViramUmaFonteComAMelhorSimilaridade()
    {
        var busca = new BuscaFixa(
            new("artigo", _artigo, null, "Erro 403 no módulo de boletos", "trecho 1", 0.6921),
            new("chamado", _chamado, 877, "Erro 403 ao abrir boletos", "chamado", 0.649),
            new("artigo", _artigo, null, "Erro 403 no módulo de boletos", "trecho 2", 0.543));

        var contexto = await Criar(new GeradorFixo(), busca).RecuperarAsync(Mascarar("Erro 403"), Mascarar("boletos"), Ct);

        contexto.Fontes.ShouldBe(
        [
            new("artigo", _artigo, null, "Erro 403 no módulo de boletos", 0.692),
            new("chamado", _chamado, 877, "Erro 403 ao abrir boletos", 0.649),
        ]);
        // Para o prompt vão todos os trechos, do mais parecido ao menos.
        contexto.Trechos.Select(t => t.Valor).ShouldBe(["trecho 1", "chamado", "trecho 2"]);
    }

    [Fact]
    public async Task Recuperar_Sempre_BuscaComOModeloDoGeradorEOTextoMascarado()
    {
        var gerador = new GeradorFixo();
        var busca = new BuscaFixa();

        await Criar(gerador, busca).RecuperarAsync(
            _mascarador.Mascarar("Boleto da Maria", ["Maria"]), Mascarar("CPF 529.982.247-25"), Ct);

        busca.Modelo.ShouldBe("modelo-x");
        busca.Opcoes.ShouldBe(_opcoes);
        gerador.Recebido.ShouldNotBeNull().ShouldNotContain("Maria");
        gerador.Recebido.ShouldNotContain("529.982.247-25");
    }

    [Fact]
    public async Task Recuperar_ProvedorDeEmbeddingsFora_SegueSemContexto()
    {
        var busca = new BuscaFixa();

        var contexto = await Criar(new GeradorFixo { Fora = true }, busca)
            .RecuperarAsync(Mascarar("Erro 403"), Mascarar("boletos"), Ct);

        contexto.ShouldBe(ContextoRecuperado.Vazio);
        busca.Modelo.ShouldBeNull(); // nem chegou a buscar
    }

    private static RecuperadorRag Criar(GeradorFixo gerador, BuscaFixa busca) =>
        new(gerador, busca, _mascarador, _opcoes, NullLogger<RecuperadorRag>.Instance);

    private static TextoMascarado Mascarar(string texto) => _mascarador.Mascarar(texto);

    private sealed class GeradorFixo : IGeradorEmbeddings
    {
        public bool Fora { get; init; }

        public string? Recebido { get; private set; }

        public string Modelo => "modelo-x";

        public Task<IReadOnlyList<float[]>> GerarAsync(
            IReadOnlyList<TextoMascarado> textos, CancellationToken cancellationToken)
        {
            if (Fora)
            {
                throw new ProvedorIndisponivelException(ProvedorIndisponivelException.TipoRateLimit, "429");
            }

            Recebido = textos.Single().Valor;
            return Task.FromResult<IReadOnlyList<float[]>>([new float[768]]);
        }
    }

    private sealed class BuscaFixa(params DocumentoRecuperado[] documentos) : IBuscaSemantica
    {
        public string? Modelo { get; private set; }

        public OpcoesRag? Opcoes { get; private set; }

        public Task<IReadOnlyList<DocumentoRecuperado>> BuscarAsync(
            float[] vetor, string modelo, OpcoesRag opcoes, CancellationToken cancellationToken)
        {
            Modelo = modelo;
            Opcoes = opcoes;
            return Task.FromResult<IReadOnlyList<DocumentoRecuperado>>(documentos);
        }
    }
}
