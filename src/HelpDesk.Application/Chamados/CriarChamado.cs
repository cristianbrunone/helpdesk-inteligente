using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Erros;

namespace HelpDesk.Application.Chamados;

/// <summary>Corpo de <c>POST /api/chamados</c>. Categoria e prioridade são opcionais (P-02).</summary>
public sealed record NovoChamado(
    string? Titulo,
    string? Descricao,
    string? SolicitanteNome,
    string? SolicitanteEmail,
    short? CategoriaId,
    Prioridade? Prioridade);

/// <summary>
/// Abre um chamado (RF-01). Grava o chamado e o histórico inicial numa transação e responde na hora: a triagem
/// por IA (Sprint 2) nunca é aguardada aqui.
/// </summary>
public sealed class CriarChamado(IRepositorioChamados repositorio, IConsultaChamados consulta, TimeProvider relogio)
{
    private const string Categoria = nameof(NovoChamado.CategoriaId);
    private const string CategoriaInexistente = "A categoria informada não existe.";

    public async Task<ChamadoVersionado> ExecutarAsync(NovoChamado dados, CancellationToken cancellationToken)
    {
        var categoriaInexistente = dados.CategoriaId is { } categoriaId
            && !await repositorio.CategoriaExisteAsync(categoriaId, cancellationToken);

        Chamado chamado;
        try
        {
            chamado = Chamado.Abrir(
                dados.Titulo,
                dados.Descricao,
                dados.SolicitanteNome,
                dados.SolicitanteEmail,
                dados.CategoriaId,
                dados.Prioridade,
                relogio.GetUtcNow());
        }
        catch (ValidacaoException erro) when (categoriaInexistente)
        {
            // Um único 422 com todos os campos: os do domínio e o da categoria.
            throw new ValidacaoException(new Dictionary<string, string[]>(erro.Erros) { [Categoria] = [CategoriaInexistente] });
        }

        if (categoriaInexistente)
        {
            throw new ValidacaoException(new Dictionary<string, string[]> { [Categoria] = [CategoriaInexistente] });
        }

        repositorio.Adicionar(chamado);
        await repositorio.SalvarAsync(cancellationToken);

        return await consulta.ObterDetalheAsync(chamado.Id, cancellationToken)
            ?? throw new InvalidOperationException("O chamado recém-criado não foi encontrado.");
    }
}
