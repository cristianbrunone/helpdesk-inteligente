using HelpDesk.Application.Chamados;
using HelpDesk.Application.Triagem;

namespace HelpDesk.Infrastructure.Ia;

/// <summary>
/// Etapa "Recuperar" sem RAG (decisão da Sprint 2): o pipeline já tem as cinco etapas do ADR-0004, e a busca no
/// pgvector substitui esta implementação na Sprint 3 sem mexer no pipeline.
/// </summary>
internal sealed class RecuperadorSemRag : IRecuperadorContexto
{
    public Task<IReadOnlyList<FonteTriagem>> RecuperarAsync(
        TextoMascarado titulo, TextoMascarado descricao, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FonteTriagem>>([]);
}
