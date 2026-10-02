using HelpDesk.Application.Autenticacao;
using HelpDesk.Application.Categorias;
using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Triagem;

namespace HelpDesk.Application.Chamados;

/// <summary>Parâmetros de <c>GET /api/chamados</c> como chegam da query string (contrato §3).</summary>
public sealed record ParametrosListagem(
    IReadOnlyList<StatusChamado> Status,
    IReadOnlyList<Prioridade> Prioridades,
    IReadOnlyList<short> CategoriaIds,
    bool SemCategoria,
    string? Q,
    DateOnly? CriadoDe,
    DateOnly? CriadoAte,
    string? OrdenarPor,
    string? Direcao,
    int? Pagina,
    int? TamanhoPagina);

public enum OrdenacaoChamados
{
    CriadoEm,
    Prioridade,
}

/// <summary>Filtro já validado e normalizado, entregue à consulta.</summary>
public sealed record FiltroChamados(
    IReadOnlyList<StatusChamado> Status,
    IReadOnlyList<Prioridade> Prioridades,
    IReadOnlyList<short> CategoriaIds,
    bool SemCategoria,
    string? Texto,
    DateOnly? CriadoDe,
    DateOnly? CriadoAte,
    OrdenacaoChamados OrdenarPor,
    bool Ascendente,
    int Pagina,
    int TamanhoPagina,
    string? SolicitanteEmail = null);

/// <summary>
/// Item da listagem. Sem o e-mail do solicitante (minimização de dados, contrato §3). <see cref="TriagemStatus"/> é o
/// da triagem vigente, ou <c>null</c> se não houver.
/// </summary>
public sealed record ChamadoResumo(
    Guid Id,
    long Numero,
    string Titulo,
    StatusChamado Status,
    Prioridade Prioridade,
    CategoriaResumo? Categoria,
    string SolicitanteNome,
    DateTimeOffset CriadoEm,
    DateTimeOffset AtualizadoEm,
    StatusTriagem? TriagemStatus);

/// <summary>Página da listagem no formato do contrato (<c>itens</c>, <c>pagina</c>, <c>totalPaginas</c>...).</summary>
public sealed record ResultadoPaginado<T>(IReadOnlyList<T> Itens, int Pagina, int TamanhoPagina, int TotalItens, int TotalPaginas);

/// <summary>Listagem paginada com filtros (RF-03, RF-04).</summary>
public sealed class ListarChamados(IConsultaChamados consulta)
{
    public const int TamanhoPaginaPadrao = 20;
    public const int TamanhoPaginaMaximo = 100;
    public const int TextoTamanhoMinimo = 3;
    public const int TextoTamanhoMaximo = 100;

    /// <summary>Lista chamados respeitando o perfil: atendente vê todos, solicitante vê só os próprios (ADR-0026).</summary>
    public Task<ResultadoPaginado<ChamadoResumo>> ExecutarAsync(
        ParametrosListagem parametros, UsuarioAutenticado usuario, CancellationToken cancellationToken)
    {
        var email = usuario.Perfil == HelpDesk.Domain.Usuarios.PerfilUsuario.Solicitante ? usuario.Email : null;
        return consulta.ListarAsync(Validar(parametros, email), cancellationToken);
    }

    public Task<ResultadoPaginado<ChamadoResumo>> ExecutarAsync(ParametrosListagem parametros, CancellationToken cancellationToken) =>
        consulta.ListarAsync(Validar(parametros), cancellationToken);

    /// <summary>Converte a query string no filtro, ou lança 400 com todos os problemas encontrados.</summary>
    public static FiltroChamados Validar(ParametrosListagem p, string? solicitanteEmail = null)
    {
        var problemas = new List<string>();

        if (p.Status.Any(s => !Enum.IsDefined(s)) || p.Prioridades.Any(s => !Enum.IsDefined(s)))
        {
            problemas.Add("Os filtros 'status' e 'prioridade' aceitam apenas os valores do contrato.");
        }

        var texto = string.IsNullOrWhiteSpace(p.Q) ? null : p.Q.Trim();
        if (texto is not null && (texto.Length < TextoTamanhoMinimo || texto.Length > TextoTamanhoMaximo))
        {
            problemas.Add($"O parâmetro 'q' deve ter entre {TextoTamanhoMinimo} e {TextoTamanhoMaximo} caracteres.");
        }

        if (p.CriadoDe > p.CriadoAte)
        {
            problemas.Add("O parâmetro 'criadoDe' não pode ser posterior a 'criadoAte'.");
        }

        OrdenacaoChamados? ordenarPor = p.OrdenarPor?.ToLowerInvariant() switch
        {
            null or "criadoem" => OrdenacaoChamados.CriadoEm,
            "prioridade" => OrdenacaoChamados.Prioridade,
            _ => null,
        };
        if (ordenarPor is null)
        {
            problemas.Add("O parâmetro 'ordenarPor' aceita 'criadoEm' ou 'prioridade'.");
        }

        bool? ascendente = p.Direcao?.ToLowerInvariant() switch
        {
            null or "desc" => false,
            "asc" => true,
            _ => null,
        };
        if (ascendente is null)
        {
            problemas.Add("O parâmetro 'direcao' aceita 'asc' ou 'desc'.");
        }

        var pagina = p.Pagina ?? 1;
        var tamanho = p.TamanhoPagina ?? TamanhoPaginaPadrao;
        if (pagina < 1)
        {
            problemas.Add("O parâmetro 'pagina' deve ser maior ou igual a 1.");
        }

        if (tamanho < 1 || tamanho > TamanhoPaginaMaximo)
        {
            problemas.Add($"O parâmetro 'tamanhoPagina' deve estar entre 1 e {TamanhoPaginaMaximo}.");
        }
        else if ((long)(pagina - 1) * tamanho > int.MaxValue)
        {
            problemas.Add("O parâmetro 'pagina' está além do limite.");
        }

        if (problemas.Count > 0)
        {
            throw new RequisicaoInvalidaException(string.Join(" ", problemas));
        }

        return new FiltroChamados(
            p.Status.Distinct().ToArray(),
            p.Prioridades.Distinct().ToArray(),
            p.CategoriaIds.Distinct().ToArray(),
            p.SemCategoria,
            texto,
            p.CriadoDe,
            p.CriadoAte,
            ordenarPor!.Value,
            ascendente!.Value,
            pagina,
            tamanho,
            solicitanteEmail);
    }
}
