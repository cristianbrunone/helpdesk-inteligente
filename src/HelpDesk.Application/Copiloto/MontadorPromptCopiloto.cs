using System.Text;
using HelpDesk.Application.Chamados;
using HelpDesk.Application.Triagem;

namespace HelpDesk.Application.Copiloto;

/// <summary>
/// Uma mensagem da conversa já mascarada. <see cref="DoAtendente"/> distingue o atendente das respostas anteriores
/// do copiloto, que o cliente reenvia a cada pergunta (o servidor é stateless, P-08).
/// </summary>
public sealed record MensagemPrompt(bool DoAtendente, TextoMascarado Texto);

/// <summary>O prompt do copiloto: tudo o que vai ao provedor, já mascarado (ADR-0006).</summary>
public sealed record PromptCopiloto(string Versao, TextoMascarado Sistema, IReadOnlyList<MensagemPrompt> Mensagens);

/// <summary>
/// Monta o prompt do copiloto a partir de <c>prompts/copiloto.v1.md</c>: o bloco do chamado em contexto entra no
/// marcador <see cref="MarcadorChamado"/> com título e descrição mascarados, e <b>sem</b> o nome e o e-mail do
/// solicitante (RN-10). Mudou o prompt? Nova versão em arquivo novo.
/// </summary>
public sealed class MontadorPromptCopiloto(
    ICatalogoPrompts catalogo,
    MascaradorDadosPessoais mascarador,
    string versao = MontadorPromptCopiloto.VersaoPadrao)
{
    public const string VersaoPadrao = "copiloto.v1";
    public const string MarcadorChamado = "{{CHAMADO}}";

    public string Versao { get; } = versao;

    /// <summary>Confere na subida que o prompt configurado existe e tem o marcador (falha cedo, não por pergunta).</summary>
    public async Task ValidarAsync(CancellationToken cancellationToken) => await ModeloAsync(cancellationToken);

    public async Task<PromptCopiloto> MontarAsync(
        ChamadoDetalhe chamado, IReadOnlyList<MensagemCopiloto> mensagens, CancellationToken cancellationToken)
    {
        var modelo = await ModeloAsync(cancellationToken);
        string[] nomes = [chamado.SolicitanteNome];

        // Rótulos fixos, uma linha cada: o modelo (e o fake) leem o chamado por eles.
        var bloco = new StringBuilder()
            .Append("Número: #").Append(chamado.Numero).Append('\n')
            .Append("Título: ").Append(mascarador.Mascarar(chamado.Titulo, nomes).Valor).Append('\n')
            .Append("Categoria: ").Append(chamado.Categoria?.Nome ?? "(sem categoria)").Append('\n')
            .Append("Prioridade: ").Append(chamado.Prioridade).Append('\n')
            .Append("Status: ").Append(chamado.Status).Append('\n')
            .Append("Descrição: ").Append(mascarador.Mascarar(chamado.Descricao, nomes).Valor)
            .ToString();

        // O bloco já está mascarado por partes; a nova passada só o embrulha no tipo (marcadores não casam de novo).
        var sistema = mascarador.Mascarar(modelo.Replace(MarcadorChamado, bloco, StringComparison.Ordinal), nomes);
        return new PromptCopiloto(Versao, sistema,
        [
            .. mensagens.Select(m => new MensagemPrompt(m.Papel == MensagemCopiloto.PapelUsuario,
                mascarador.Mascarar(m.Conteudo, nomes))),
        ]);
    }

    private async Task<string> ModeloAsync(CancellationToken cancellationToken)
    {
        var modelo = await catalogo.ObterAsync(Versao, cancellationToken);
        return modelo.Contains(MarcadorChamado, StringComparison.Ordinal)
            ? modelo
            : throw new InvalidOperationException($"O prompt {Versao} não tem o marcador {MarcadorChamado}.");
    }
}
