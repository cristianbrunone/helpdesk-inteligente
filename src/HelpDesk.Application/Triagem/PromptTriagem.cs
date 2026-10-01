namespace HelpDesk.Application.Triagem;

/// <summary>
/// Prompt pronto para o provedor. As instruções (<see cref="Sistema"/>) vêm do arquivo versionado; o conteúdo do
/// chamado e o contexto recuperado só existem aqui como <see cref="TextoMascarado"/>, e a mensagem do usuário é
/// composta a partir deles.
/// </summary>
public sealed record PromptTriagem(
    string Versao, string Sistema, TextoMascarado Titulo, TextoMascarado Descricao,
    IReadOnlyList<TextoMascarado> Contexto)
{
    private const string AberturaChamado = "<chamado>";
    private const string FechamentoChamado = "</chamado>";
    private const string AberturaContexto = "<contexto>";
    private const string FechamentoContexto = "</contexto>";

    public PromptTriagem(string versao, string sistema, TextoMascarado titulo, TextoMascarado descricao)
        : this(versao, sistema, titulo, descricao, [])
    {
    }

    /// <summary>
    /// O contexto recuperado (se houver) e o chamado, cada um entre delimitadores. Os dois são dados, não instruções:
    /// os trechos do contexto também foram escritos por usuários (outros chamados). Uma tag de delimitação escrita
    /// dentro do texto é neutralizada, para ninguém conseguir "fechar" o bloco e continuar com instruções.
    /// </summary>
    public string MensagemDoUsuario()
    {
        var chamado =
            $"{AberturaChamado}\nTítulo: {Neutralizar(Titulo.Valor)}\nDescrição: {Neutralizar(Descricao.Valor)}\n{FechamentoChamado}";
        if (Contexto.Count == 0)
        {
            return chamado;
        }

        var trechos = Contexto.Select((t, i) => $"[{i + 1}]\n{Neutralizar(t.Valor)}");
        return $"{AberturaContexto}\n{string.Join("\n\n", trechos)}\n{FechamentoContexto}\n\n{chamado}";
    }

    private static string Neutralizar(string texto) => texto
        .Replace(FechamentoChamado, "[/chamado]", StringComparison.OrdinalIgnoreCase)
        .Replace(AberturaChamado, "[chamado]", StringComparison.OrdinalIgnoreCase)
        .Replace(FechamentoContexto, "[/contexto]", StringComparison.OrdinalIgnoreCase)
        .Replace(AberturaContexto, "[contexto]", StringComparison.OrdinalIgnoreCase);
}
