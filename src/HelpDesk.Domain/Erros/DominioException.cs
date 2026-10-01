namespace HelpDesk.Domain.Erros;

/// <summary>
/// Base dos erros de domínio. O <see cref="Codigo"/> é o mesmo do catálogo de erros da API
/// (<c>docs/04-contratos-api.md</c>); a conversão em ProblemDetails acontece num único <c>IExceptionHandler</c>.
/// </summary>
public abstract class DominioException(string codigo, string mensagem) : Exception(mensagem)
{
    public string Codigo { get; } = codigo;
}
