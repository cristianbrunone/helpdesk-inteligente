namespace HelpDesk.Application.Autenticacao;

/// <summary>
/// Hash de senha (ADR-0026). A Application decide quando calcular e conferir; o algoritmo e os parâmetros são da
/// Infrastructure. O hash guarda os próprios parâmetros, então mudar as iterações não invalida os já gravados.
/// </summary>
public interface IHashSenha
{
    string Gerar(string senha);

    /// <summary>Compara em tempo constante. Um hash em formato desconhecido simplesmente não confere.</summary>
    bool Verificar(string senha, string hash);
}
