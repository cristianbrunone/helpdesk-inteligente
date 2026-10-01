using HelpDesk.Application.Triagem;

namespace HelpDesk.Infrastructure.Ia;

/// <summary>
/// Etapa "Recuperar" sem RAG: nenhum contexto. É a linha de base dos evals (<c>--rag off</c>, ADR-0018), que medem
/// o efeito do RAG comparando com o <see cref="RecuperadorRag"/>.
/// </summary>
internal sealed class RecuperadorSemRag : IRecuperadorContexto
{
    public Task<ContextoRecuperado> RecuperarAsync(
        TextoMascarado titulo, TextoMascarado descricao, CancellationToken cancellationToken) =>
        Task.FromResult(ContextoRecuperado.Vazio);
}
