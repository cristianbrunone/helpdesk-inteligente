using HelpDesk.Application.Triagem;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure.Persistencia;

/// <summary>
/// Reserva atômica (ADR-0003, ADR-0010): um único <c>UPDATE ... RETURNING</c> sobre um <c>SELECT ... FOR UPDATE
/// SKIP LOCKED</c>, apoiado no índice parcial da fila (#9). Linhas já travadas por outra instância são puladas, não
/// esperadas. A reserva conta a tentativa, abre o lease e empurra <c>proxima_tentativa_em</c> com backoff
/// exponencial: se o Worker cair no meio, a triagem volta à fila só depois disso.
/// </summary>
internal sealed class FilaTriagem(HelpDeskDbContext db) : IFilaTriagem
{
    public async Task<IReadOnlyList<Guid>> ReservarAsync(int quantidade, TimeSpan lease, CancellationToken cancellationToken)
    {
        var segundos = (int)Math.Ceiling(lease.TotalSeconds);
        return await db.Database.SqlQuery<Guid>($"""
            UPDATE triagens_ia AS t
            SET tentativas = t.tentativas + 1,
                lock_expira_em = now() + make_interval(secs => {segundos}),
                proxima_tentativa_em = now() + make_interval(secs => {segundos} * power(2, t.tentativas)::int)
            WHERE t.id IN (
                SELECT id FROM triagens_ia
                WHERE status = 'pendente'
                  AND proxima_tentativa_em <= now()
                  AND (lock_expira_em IS NULL OR lock_expira_em < now())
                ORDER BY proxima_tentativa_em
                LIMIT {quantidade}
                FOR UPDATE SKIP LOCKED)
            RETURNING t.id AS "Value"
            """).ToListAsync(cancellationToken);
    }
}
