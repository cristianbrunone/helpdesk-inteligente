using HelpDesk.Application.Categorias;

namespace HelpDesk.Application.Triagem;

/// <summary>Porta para os prompts versionados em <c>prompts/*.md</c>.</summary>
public interface ICatalogoPrompts
{
    /// <summary>O conteúdo do prompt da versão pedida (ex.: <c>triagem.v1</c>).</summary>
    Task<string> ObterAsync(string versao, CancellationToken cancellationToken);
}

/// <summary>
/// Etapa "MontarPrompt" do pipeline (ADR-0004). Mudou o prompt? Nova versão em arquivo novo, nunca edição: a
/// versão usada fica gravada em cada triagem.
/// </summary>
public sealed class MontadorPromptTriagem(ICatalogoPrompts catalogo)
{
    public const string VersaoAtual = "triagem.v1";
    public const string MarcadorCategorias = "{{CATEGORIAS}}";

    public async Task<PromptTriagem> MontarAsync(
        TextoMascarado titulo,
        TextoMascarado descricao,
        IReadOnlyList<CategoriaResumo> categorias,
        CancellationToken cancellationToken)
    {
        var modelo = await catalogo.ObterAsync(VersaoAtual, cancellationToken);
        if (!modelo.Contains(MarcadorCategorias, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"O prompt {VersaoAtual} não tem o marcador {MarcadorCategorias}.");
        }

        // As categorias vêm do banco (dado confiável), não do usuário.
        var lista = string.Join('\n', categorias.Select(c => $"- {c.Nome}"));
        return new PromptTriagem(VersaoAtual, modelo.Replace(MarcadorCategorias, lista, StringComparison.Ordinal),
            titulo, descricao);
    }
}
