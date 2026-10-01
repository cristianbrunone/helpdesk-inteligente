namespace HelpDesk.Domain.Erros;

/// <summary>
/// Dados que violam regras de validação (422). <see cref="Erros"/> agrupa as mensagens por campo, com o nome da
/// propriedade do domínio (a API converte para camelCase).
/// </summary>
public sealed class ValidacaoException(IReadOnlyDictionary<string, string[]> erros)
    : DominioException("validacao", "Um ou mais campos são inválidos.")
{
    public IReadOnlyDictionary<string, string[]> Erros { get; } = erros;
}
