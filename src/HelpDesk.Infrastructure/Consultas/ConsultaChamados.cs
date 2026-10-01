using System.Globalization;
using HelpDesk.Application.Categorias;
using HelpDesk.Application.Chamados;
using HelpDesk.Domain.Chamados;
using HelpDesk.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure.Consultas;

/// <summary>
/// Detalhe com EF Core (regra "leitura × escrita"): carrega o agregado porque <c>transicoesPermitidas</c> e
/// <c>podeComentar</c> são calculados pelo domínio, nunca replicados aqui.
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

        return encontrado is null
            ? null
            : new ChamadoVersionado(
                Mapear(encontrado.Chamado, encontrado.Categoria),
                encontrado.Versao.ToString(CultureInfo.InvariantCulture));
    }

    private static ChamadoDetalhe Mapear(Chamado c, CategoriaResumo? categoria) => new(
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
            .Select(h => new HistoricoDetalhe(h.StatusAnterior, h.StatusNovo, h.AlteradoEm, h.AlteradoPor))]);
}
