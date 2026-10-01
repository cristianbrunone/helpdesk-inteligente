namespace HelpDesk.Application.Chamados;

/// <summary>Detalhe com comentários e histórico (RF-05), mais a versão para o <c>ETag</c>.</summary>
public sealed class ObterChamado(IConsultaChamados consulta)
{
    public async Task<ChamadoVersionado> ExecutarAsync(Guid id, CancellationToken cancellationToken) =>
        await consulta.ObterDetalheAsync(id, cancellationToken)
        ?? throw RecursoNaoEncontradoException.Chamado(id);
}
