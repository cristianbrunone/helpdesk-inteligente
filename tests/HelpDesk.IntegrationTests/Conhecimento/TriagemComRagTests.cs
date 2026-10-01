using HelpDesk.Application.Chamados;
using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Triagem;
using HelpDesk.Infrastructure.Persistencia;
using HelpDesk.IntegrationTests.Infraestrutura;
using HelpDesk.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.IntegrationTests.Conhecimento;

/// <summary>
/// A triagem de ponta a ponta no Worker com o índice do seed: com a <c>triagem.v2</c>, o RAG entra no prompt e as
/// fontes ficam gravadas e aparecem no detalhe (RF-16); com a <c>triagem.v1</c>, nada de RAG.
/// </summary>
public sealed class TriagemComRagTests(BuscaSemanticaTests.IndiceDoSeed indice)
    : IClassFixture<BuscaSemanticaTests.IndiceDoSeed>
{
    private static readonly DateTimeOffset _agora = DateTimeOffset.UtcNow;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Processar_ComTriagemV2_GravaAsFontesQueAparecemNoDetalhe()
    {
        await using var servicos = indice.Servicos(versaoPrompt: "triagem.v2");
        var (chamadoId, triagemId) = await CriarPendenteAsync(servicos);
        var embeddingsAntes = await ContarEmbeddingsAsync(servicos);

        await servicos.GetRequiredService<ConsumidorFilaTriagem>().ProcessarLoteAsync(Ct);

        var triagem = await LerAsync(servicos, db => db.Triagens.AsNoTracking().SingleAsync(t => t.Id == triagemId, Ct));
        triagem.Status.ShouldBe(StatusTriagem.Concluida);
        triagem.PromptVersao.ShouldBe("triagem.v2");
        triagem.Fontes.ShouldNotBeEmpty();
        triagem.Fontes[0].ShouldBe(triagem.Fontes.MaxBy(f => f.Similaridade));
        triagem.Fontes.ShouldContain(f => f.Tipo == "artigo" && f.Titulo == "Erro 403 no módulo de boletos");
        triagem.Fontes.ShouldContain(f => f.Tipo == "chamado" && f.Numero != null);
        triagem.Fontes.Select(f => (f.Tipo, f.Id)).ShouldBeUnique();
        (await ContarEmbeddingsAsync(servicos)).ShouldBe(embeddingsAntes + 1); // o embedding da consulta

        await using var escopo = servicos.CreateAsyncScope();
        var detalhe = await escopo.ServiceProvider.GetRequiredService<IConsultaChamados>()
            .ObterDetalheAsync(chamadoId, Ct);
        detalhe.ShouldNotBeNull().Chamado.Triagem.ShouldNotBeNull().Fontes.ShouldBe(triagem.Fontes);
    }

    [Fact]
    public async Task Processar_ComTriagemV1_NaoUsaORagNemGeraEmbedding()
    {
        await using var servicos = indice.Servicos(versaoPrompt: "triagem.v1");
        var (_, triagemId) = await CriarPendenteAsync(servicos);
        var embeddingsAntes = await ContarEmbeddingsAsync(servicos);

        await servicos.GetRequiredService<ConsumidorFilaTriagem>().ProcessarLoteAsync(Ct);

        var triagem = await LerAsync(servicos, db => db.Triagens.AsNoTracking().SingleAsync(t => t.Id == triagemId, Ct));
        triagem.Status.ShouldBe(StatusTriagem.Concluida);
        triagem.PromptVersao.ShouldBe("triagem.v1");
        triagem.Fontes.ShouldBeEmpty();
        (await ContarEmbeddingsAsync(servicos)).ShouldBe(embeddingsAntes);
    }

    private static Task<(Guid ChamadoId, Guid TriagemId)> CriarPendenteAsync(ServiceProvider servicos) =>
        LerAsync(servicos, async db =>
        {
            var chamado = Chamado.Abrir("Erro 403 em boletos",
                "Ao abrir o módulo de boletos aparece erro 403, acesso negado. Meu CPF é 529.982.247-25.",
                "Paula Teste", "paula.teste@example.com", null, null, _agora);
            var triagem = TriagemIA.Criar(chamado, _agora);
            db.Chamados.Add(chamado);
            db.Triagens.Add(triagem);
            await db.SaveChangesAsync(Ct);
            return (chamado.Id, triagem.Id);
        });

    private static Task<int> ContarEmbeddingsAsync(ServiceProvider servicos) =>
        LerAsync(servicos, db => db.UsoLlm.CountAsync(u => u.Operacao == RegistroUsoLlm.OperacaoEmbedding, Ct));

    private static async Task<T> LerAsync<T>(ServiceProvider servicos, Func<HelpDeskDbContext, Task<T>> consulta)
    {
        await using var escopo = servicos.CreateAsyncScope();
        return await consulta(escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>());
    }
}
