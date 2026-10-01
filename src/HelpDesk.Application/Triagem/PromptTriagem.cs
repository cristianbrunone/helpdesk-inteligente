namespace HelpDesk.Application.Triagem;

/// <summary>
/// Prompt pronto para o provedor. As instruções (<see cref="Sistema"/>) vêm do arquivo versionado; o conteúdo do
/// chamado só existe aqui como <see cref="TextoMascarado"/>, e a mensagem do usuário é composta a partir dele.
/// </summary>
public sealed record PromptTriagem(string Versao, string Sistema, TextoMascarado Titulo, TextoMascarado Descricao)
{
    private const string AberturaChamado = "<chamado>";
    private const string FechamentoChamado = "</chamado>";

    /// <summary>
    /// O chamado entre delimitadores. Uma tag de delimitação escrita pelo próprio usuário é neutralizada, para ele
    /// não conseguir "fechar" o bloco de dados e continuar com instruções (prompt injection).
    /// </summary>
    public string MensagemDoUsuario() =>
        $"{AberturaChamado}\nTítulo: {Neutralizar(Titulo.Valor)}\nDescrição: {Neutralizar(Descricao.Valor)}\n{FechamentoChamado}";

    private static string Neutralizar(string texto) => texto
        .Replace(FechamentoChamado, "[/chamado]", StringComparison.OrdinalIgnoreCase)
        .Replace(AberturaChamado, "[chamado]", StringComparison.OrdinalIgnoreCase);
}
