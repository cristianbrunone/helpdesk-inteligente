using HelpDesk.Application.Autenticacao;
using HelpDesk.Domain.Usuarios;

namespace HelpDesk.Application.Chamados;

/// <summary>Corpo de <c>POST /api/chamados/{id}/comentarios</c>.</summary>
public sealed record NovoComentario(string? Autor, string? Texto);

/// <summary>O comentário criado e a nova versão do chamado (comentar também altera o chamado).</summary>
public sealed record ComentarioCriado(ComentarioDetalhe Comentario, string VersaoChamado);

/// <summary>
/// Comenta um chamado (RF-07). Fechado e Cancelado recusam (RN-04, 409); Resolvido aceita (P-06). Mesma ordem de
/// verificações da mudança de status: 404 → 412 → 422 → 409.
/// </summary>
public sealed class AdicionarComentario(IRepositorioChamados repositorio, TimeProvider relogio)
{
    /// <summary>Comenta o chamado; solicitante só comenta nos próprios (chamado de outro dá 404: ADR-0026).</summary>
    public async Task<ComentarioCriado> ExecutarAsync(
        Guid chamadoId,
        NovoComentario dados,
        UsuarioAutenticado usuario,
        IReadOnlyCollection<string>? versoesAceitas,
        CancellationToken cancellationToken)
    {
        var chamado = await repositorio.ObterParaAlteracaoAsync(chamadoId, cancellationToken)
            ?? throw RecursoNaoEncontradoException.Chamado(chamadoId);

        if (usuario.Perfil == PerfilUsuario.Solicitante
            && !string.Equals(chamado.SolicitanteEmail, usuario.Email, StringComparison.OrdinalIgnoreCase))
        {
            throw RecursoNaoEncontradoException.Chamado(chamadoId);
        }

        Precondicao.ExigirVersao(versoesAceitas, repositorio.Versao(chamado));

        var comentario = chamado.Comentar(dados.Autor, dados.Texto, relogio.GetUtcNow());
        await repositorio.SalvarAsync(cancellationToken);

        return new ComentarioCriado(
            new ComentarioDetalhe(comentario.Id, comentario.Autor, comentario.Texto, comentario.CriadoEm),
            repositorio.Versao(chamado));
    }

    public async Task<ComentarioCriado> ExecutarAsync(
        Guid chamadoId,
        NovoComentario dados,
        IReadOnlyCollection<string>? versoesAceitas,
        CancellationToken cancellationToken)
    {
        var chamado = await repositorio.ObterParaAlteracaoAsync(chamadoId, cancellationToken)
            ?? throw RecursoNaoEncontradoException.Chamado(chamadoId);

        Precondicao.ExigirVersao(versoesAceitas, repositorio.Versao(chamado));

        var comentario = chamado.Comentar(dados.Autor, dados.Texto, relogio.GetUtcNow());
        await repositorio.SalvarAsync(cancellationToken);

        return new ComentarioCriado(
            new ComentarioDetalhe(comentario.Id, comentario.Autor, comentario.Texto, comentario.CriadoEm),
            repositorio.Versao(chamado));
    }
}
