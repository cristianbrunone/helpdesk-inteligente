using HelpDesk.Application.Autenticacao;
using HelpDesk.Domain.Usuarios;

namespace HelpDesk.Application.Chamados;

/// <summary>Detalhe com comentários e histórico (RF-05), mais a versão para o <c>ETag</c>.</summary>
public sealed class ObterChamado(IConsultaChamados consulta)
{
    /// <summary>
    /// Devolve o chamado respeitando o perfil (ADR-0026): atendente vê tudo; solicitante vê só o próprio
    /// (chamado de outro dá 404, sem vazar a existência), sem os dados de triagem da IA e com transições vazias.
    /// </summary>
    public async Task<ChamadoVersionado> ExecutarAsync(
        Guid id, UsuarioAutenticado usuario, CancellationToken cancellationToken)
    {
        var encontrado = await ExecutarAsync(id, cancellationToken);

        if (usuario.Perfil == PerfilUsuario.Solicitante)
        {
            if (!string.Equals(encontrado.Chamado.SolicitanteEmail, usuario.Email, StringComparison.OrdinalIgnoreCase))
            {
                throw RecursoNaoEncontradoException.Chamado(id);
            }

            var paraSolicitante = encontrado.Chamado with
            {
                Triagem = null,
                TransicoesPermitidas = [],
            };
            return new ChamadoVersionado(paraSolicitante, encontrado.Versao);
        }

        return encontrado;
    }

    public async Task<ChamadoVersionado> ExecutarAsync(Guid id, CancellationToken cancellationToken) =>
        await consulta.ObterDetalheAsync(id, cancellationToken)
        ?? throw RecursoNaoEncontradoException.Chamado(id);
}
