using HelpDesk.Domain.Erros;

namespace HelpDesk.Application;

/// <summary>
/// Outra pessoa alterou o chamado (412 <c>versao_desatualizada</c>, contrato §2): o <c>If-Match</c> não confere ou a
/// gravação perdeu a corrida para outra (o <c>xmin</c> mudou entre a leitura e o <c>SaveChanges</c>).
/// </summary>
public sealed class VersaoDesatualizadaException()
    : DominioException(
        "versao_desatualizada",
        "O chamado foi alterado por outra pessoa. Recarregue para ver a versão atual e tente de novo.");
