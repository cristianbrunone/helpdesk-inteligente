using HelpDesk.Domain.Erros;

namespace HelpDesk.Application;

/// <summary>
/// A funcionalidade de IA está desligada pelo kill switch (503 <c>ia_indisponivel</c>, ADR-0021). Uma falha do
/// provedor na triagem nunca chega aqui: ela vira triagem <c>Falhou</c>.
/// </summary>
public sealed class IaIndisponivelException(string mensagem) : DominioException("ia_indisponivel", mensagem)
{
    public static IaIndisponivelException TriagemDesativada() =>
        new("A triagem por IA está desativada no momento.");
}
