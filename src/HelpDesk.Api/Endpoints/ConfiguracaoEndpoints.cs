using HelpDesk.Application.Triagem;

namespace HelpDesk.Api.Endpoints;

internal static class ConfiguracaoEndpoints
{
    public static IEndpointRouteBuilder MapConfiguracao(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/config/ia", (OpcoesIA opcoes) =>
                TypedResults.Ok(new ConfiguracaoIA(opcoes.TriagemHabilitada, opcoes.CopilotoHabilitado)))
            .WithTags("Configuração")
            .WithName("ObterConfiguracaoIA")
            .WithSummary("Quais funcionalidades de IA estão ativas (kill switches, ADR-0021), para a UI se adaptar.");

        return app;
    }
}

internal sealed record ConfiguracaoIA(bool Triagem, bool Copiloto);
