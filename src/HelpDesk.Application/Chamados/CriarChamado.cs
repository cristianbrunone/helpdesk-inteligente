using System.Diagnostics;
using HelpDesk.Application.Triagem;
using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Erros;
using HelpDesk.Domain.Triagem;

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
/// Abre um chamado (RF-01). Grava o chamado, o histórico inicial e a triagem pendente numa transação e responde na
/// hora (RF-02): a triagem por IA é processada pelo Worker e nunca é aguardada aqui. Com a triagem desativada
/// (ADR-0021), o chamado nasce sem triagem e nada fica acumulado na fila.
/// </summary>
public sealed class CriarChamado(
    IRepositorioChamados repositorio,
    IRepositorioTriagens triagens,
    IConsultaChamados consulta,
    OpcoesIA opcoesIA,
    TimeProvider relogio)
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
        if (opcoesIA.TriagemHabilitada)
        {
            // O trace da criação fica gravado na triagem: o processamento no Worker se vincula a ele (ADR-0019).
            triagens.Adicionar(TriagemIA.Criar(chamado, chamado.CriadoEm, Activity.Current?.Id));
        }

        await repositorio.SalvarAsync(cancellationToken);

        return await consulta.ObterDetalheAsync(chamado.Id, cancellationToken)
            ?? throw new InvalidOperationException("O chamado recém-criado não foi encontrado.");
    }
}
