namespace HelpDesk.Application.Chamados;

/// <summary>Precondição de concorrência das escritas sobre o chamado (contrato §1: <c>If-Match</c> opcional).</summary>
public static class Precondicao
{
    /// <summary>
    /// Sem <c>If-Match</c>, ou com <c>*</c>, não há precondição. Caso contrário, a versão atual precisa estar entre as
    /// enviadas; se não estiver, outra pessoa alterou o chamado depois que o cliente o leu (412).
    /// </summary>
    public static void ExigirVersao(IReadOnlyCollection<string>? versoesAceitas, string versaoAtual)
    {
        if (versoesAceitas is null || versoesAceitas.Contains("*") || versoesAceitas.Contains(versaoAtual))
        {
            return;
        }

        throw new VersaoDesatualizadaException();
    }
}
