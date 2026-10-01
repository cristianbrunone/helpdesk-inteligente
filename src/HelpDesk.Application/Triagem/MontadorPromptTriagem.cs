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
/// versão usada fica gravada em cada triagem. A versão em uso vem da configuração (<c>TRIAGEM_PROMPT_VERSAO</c>):
/// uma versão nova só vira a padrão depois de passar pelo harness de evals (ADR-0018).
/// <para>
/// Uma versão "usa contexto" quando as instruções dela descrevem o bloco <c>&lt;contexto&gt;</c>: só então o
/// pipeline faz a recuperação do RAG. Assim a <c>triagem.v1</c> continua sendo a linha de base sem RAG.
/// </para>
/// </summary>
public sealed class MontadorPromptTriagem(ICatalogoPrompts catalogo, string versao = MontadorPromptTriagem.VersaoPadrao)
{
    public const string VersaoPadrao = "triagem.v1";
    public const string MarcadorCategorias = "{{CATEGORIAS}}";
    public const string BlocoContexto = "<contexto>";

    public string Versao { get; } = versao;

    /// <summary>Confere na subida que o prompt configurado existe e tem os marcadores (falha cedo, não por triagem).</summary>
    public async Task ValidarAsync(CancellationToken cancellationToken) => await ModeloAsync(cancellationToken);

    public async Task<bool> UsaContextoAsync(CancellationToken cancellationToken) =>
        (await ModeloAsync(cancellationToken)).Contains(BlocoContexto, StringComparison.Ordinal);

    public async Task<PromptTriagem> MontarAsync(
        TextoMascarado titulo,
        TextoMascarado descricao,
        IReadOnlyList<CategoriaResumo> categorias,
        IReadOnlyList<TextoMascarado> contexto,
        CancellationToken cancellationToken)
    {
        var modelo = await ModeloAsync(cancellationToken);

        // As categorias vêm do banco (dado confiável), não do usuário.
        var lista = string.Join('\n', categorias.Select(c => $"- {c.Nome}"));
        var usaContexto = modelo.Contains(BlocoContexto, StringComparison.Ordinal);
        return new PromptTriagem(Versao, modelo.Replace(MarcadorCategorias, lista, StringComparison.Ordinal),
            titulo, descricao, usaContexto ? contexto : []);
    }

    private async Task<string> ModeloAsync(CancellationToken cancellationToken)
    {
        var modelo = await catalogo.ObterAsync(Versao, cancellationToken);
        return modelo.Contains(MarcadorCategorias, StringComparison.Ordinal)
            ? modelo
            : throw new InvalidOperationException($"O prompt {Versao} não tem o marcador {MarcadorCategorias}.");
    }
}
