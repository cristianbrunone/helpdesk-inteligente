using HelpDesk.Domain.Erros;

namespace HelpDesk.Application;

/// <summary>
/// Parâmetro de requisição fora do formato aceito (400 <c>requisicao_invalida</c>, contrato §2): por exemplo, <c>q</c>
/// com menos de 3 caracteres ou <c>pagina=0</c>. Não é regra de negócio (essas dão 422/409).
/// </summary>
public sealed class RequisicaoInvalidaException(string mensagem) : DominioException("requisicao_invalida", mensagem);
