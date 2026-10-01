using System.Globalization;
using HelpDesk.Application.Categorias;
using HelpDesk.Application.Chamados;
using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Triagem;
using HelpDesk.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure.Consultas;

/// <summary>
/// Detalhe com EF Core (regra "leitura × escrita"): carrega o agregado porque <c>transicoesPermitidas</c> e
/// <c>podeComentar</c> são calculados pelo domínio, nunca replicados aqui. A listagem é projeção pura
/// (<c>AsNoTracking</c> + <c>Select</c>), paginada por offset, apoiada nos índices 1 a 5 do modelo §5.
/// </summary>
internal sealed class ConsultaChamados(HelpDeskDbContext db) : IConsultaChamados
{
    public async Task<ChamadoVersionado?> ObterDetalheAsync(Guid id, CancellationToken cancellationToken)
    {
        var encontrado = await db.Chamados
            .AsNoTracking()
            .AsSingleQuery()
            .Include(c => c.Comentarios)
            .Include(c => c.Historico)
            .Where(c => c.Id == id)
            .Select(c => new
            {
                Chamado = c,
                Versao = EF.Property<uint>(c, HelpDeskDbContext.VersaoChamado),
                Categoria = db.Categorias
                    .Where(k => k.Id == c.CategoriaId)
                    .Select(k => new CategoriaResumo(k.Id, k.Nome))
                    .FirstOrDefault(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (encontrado is null)
        {
            return null;
        }

        var triagem = await ObterTriagemVigenteAsync(id, cancellationToken);
        return new ChamadoVersionado(
            Mapear(encontrado.Chamado, encontrado.Categoria, triagem),
            encontrado.Versao.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>A vigente é a mais recente (P-04, índice 8); o total mostra quantas vezes o chamado foi triado.</summary>
    public async Task<TriagemDetalhe?> ObterTriagemVigenteAsync(Guid chamadoId, CancellationToken cancellationToken)
    {
        var doChamado = db.Triagens.AsNoTracking().Where(t => t.ChamadoId == chamadoId);
        var vigente = await doChamado
            .OrderByDescending(t => t.CriadoEm)
            .ThenByDescending(t => t.Id)
            .Select(t => new
            {
                Triagem = t,
                CategoriaNome = db.Categorias
                    .Where(k => k.Id == t.CategoriaSugeridaId)
                    .Select(k => k.Nome)
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (vigente is null)
        {
            return null;
        }

        var total = await doChamado.CountAsync(cancellationToken);
        var t = vigente.Triagem;
        return new TriagemDetalhe(
            t.Id,
            t.Status,
            t.CategoriaSugeridaId is { } categoriaId ? new CategoriaResumo(categoriaId, vigente.CategoriaNome!) : null,
            t.PrioridadeSugerida,
            t.Resumo,
            t.RespostaSugerida,
            t.Confianca,
            t.Modelo,
            t.PromptVersao,
            t.Fontes,
            t.Status == StatusTriagem.Falhou ? t.ErroMotivo : null,
            t.CriadoEm,
            t.ConcluidaEm,
            t.DecididaPor,
            t.DecididaEm,
            total);
    }

    public async Task<ResultadoPaginado<ChamadoResumo>> ListarAsync(
        FiltroChamados filtro, CancellationToken cancellationToken)
    {
        var consulta = Filtrar(db.Chamados.AsNoTracking(), filtro);

        var total = await consulta.CountAsync(cancellationToken);
        var linhas = await Ordenar(consulta, filtro)
            .Skip((filtro.Pagina - 1) * filtro.TamanhoPagina)
            .Take(filtro.TamanhoPagina)
            .Select(c => new
            {
                c.Id,
                c.Numero,
                c.Titulo,
                c.Status,
                c.Prioridade,
                c.CategoriaId,
                // Subconsulta escalar por PK, só para as linhas da página.
                CategoriaNome = db.Categorias.Where(k => k.Id == c.CategoriaId).Select(k => k.Nome).FirstOrDefault(),
                c.SolicitanteNome,
                c.CriadoEm,
                c.AtualizadoEm,
                // Status da triagem vigente: subconsulta pelo índice 8, só para as linhas da página.
                TriagemStatus = db.Triagens
                    .Where(t => t.ChamadoId == c.Id)
                    .OrderByDescending(t => t.CriadoEm)
                    .Select(t => (StatusTriagem?)t.Status)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var itens = linhas.Select(l => new ChamadoResumo(
                l.Id,
                l.Numero,
                l.Titulo,
                l.Status,
                l.Prioridade,
                l.CategoriaId is { } categoriaId ? new CategoriaResumo(categoriaId, l.CategoriaNome!) : null,
                l.SolicitanteNome,
                l.CriadoEm,
                l.AtualizadoEm,
                l.TriagemStatus))
            .ToList();

        var totalPaginas = (int)Math.Ceiling(total / (double)filtro.TamanhoPagina);
        return new ResultadoPaginado<ChamadoResumo>(itens, filtro.Pagina, filtro.TamanhoPagina, total, totalPaginas);
    }

    private static IQueryable<Chamado> Filtrar(IQueryable<Chamado> consulta, FiltroChamados filtro)
    {
        // Filtros repetíveis: OR entre os valores do mesmo parâmetro, AND entre parâmetros (contrato §3).
        if (filtro.Status.Count > 0)
        {
            var status = filtro.Status;
            consulta = consulta.Where(c => status.Contains(c.Status));
        }

        if (filtro.Prioridades.Count > 0)
        {
            var prioridades = filtro.Prioridades;
            consulta = consulta.Where(c => prioridades.Contains(c.Prioridade));
        }

        var categorias = filtro.CategoriaIds.Select(id => (short?)id).ToList();
        consulta = (categorias.Count > 0, filtro.SemCategoria) switch
        {
            (true, true) => consulta.Where(c => c.CategoriaId == null || categorias.Contains(c.CategoriaId)),
            (true, false) => consulta.Where(c => categorias.Contains(c.CategoriaId)),
            (false, true) => consulta.Where(c => c.CategoriaId == null),
            _ => consulta,
        };

        // Intervalo fechado-aberto [de, ate + 1 dia), em UTC: cabe no índice 1 como range scan.
        if (filtro.CriadoDe is { } de)
        {
            var inicio = InicioDoDia(de);
            consulta = consulta.Where(c => c.CriadoEm >= inicio);
        }

        if (filtro.CriadoAte is { } ate)
        {
            var fim = InicioDoDia(ate.AddDays(1));
            consulta = consulta.Where(c => c.CriadoEm < fim);
        }

        // ADR-0008: a expressão do lado do chamado é idêntica à do índice ix_chamados_busca_trgm.
        if (filtro.Texto is { } texto)
        {
            var padrao = $"%{EscaparCuringas(texto)}%";
            consulta = consulta.Where(c => EF.Functions.Like(
                HelpDeskDbContext.FUnaccent((c.Titulo + " " + c.Descricao).ToLower()),
                HelpDeskDbContext.FUnaccent(padrao.ToLower()),
                @"\"));
        }

        return consulta;
    }

    // Desempate sempre por criado_em e id: a paginação fica estável mesmo com datas iguais.
    private static IQueryable<Chamado> Ordenar(IQueryable<Chamado> consulta, FiltroChamados filtro) =>
        (filtro.OrdenarPor, filtro.Ascendente) switch
        {
            (OrdenacaoChamados.Prioridade, false) => consulta
                .OrderByDescending(c => c.Prioridade).ThenByDescending(c => c.CriadoEm).ThenByDescending(c => c.Id),
            (OrdenacaoChamados.Prioridade, true) => consulta
                .OrderBy(c => c.Prioridade).ThenByDescending(c => c.CriadoEm).ThenByDescending(c => c.Id),
            (_, true) => consulta.OrderBy(c => c.CriadoEm).ThenBy(c => c.Id),
            _ => consulta.OrderByDescending(c => c.CriadoEm).ThenByDescending(c => c.Id),
        };

    /// <summary>O usuário busca texto literal: "100%" não pode virar curinga.</summary>
    private static string EscaparCuringas(string texto) =>
        texto.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    private static DateTimeOffset InicioDoDia(DateOnly dia) => new(dia.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private static ChamadoDetalhe Mapear(Chamado c, CategoriaResumo? categoria, TriagemDetalhe? triagem) => new(
        c.Id,
        c.Numero,
        c.Titulo,
        c.Descricao,
        c.SolicitanteNome,
        c.SolicitanteEmail,
        categoria,
        c.Prioridade,
        c.Status,
        c.CriadoEm,
        c.AtualizadoEm,
        c.ResolvidoEm,
        c.TransicoesPermitidas,
        c.PodeComentar,
        [.. c.Comentarios.OrderBy(m => m.CriadoEm).Select(m => new ComentarioDetalhe(m.Id, m.Autor, m.Texto, m.CriadoEm))],
        [.. c.Historico.OrderBy(h => h.AlteradoEm).ThenBy(h => h.Id)
            .Select(h => new HistoricoDetalhe(h.StatusAnterior, h.StatusNovo, h.AlteradoEm, h.AlteradoPor))],
        triagem);
}
