using HelpDesk.Domain.Erros;

namespace HelpDesk.Application;

/// <summary>O recurso pedido não existe (404 <c>nao_encontrado</c>, contrato §2).</summary>
public sealed class RecursoNaoEncontradoException(string mensagem) : DominioException("nao_encontrado", mensagem)
{
    public static RecursoNaoEncontradoException Chamado(Guid id) => new($"O chamado '{id}' não foi encontrado.");
}
