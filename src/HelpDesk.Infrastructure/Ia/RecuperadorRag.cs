using System.Diagnostics;
using HelpDesk.Application.Conhecimento;
using HelpDesk.Application.Triagem;
using HelpDesk.Domain.Triagem;
using Microsoft.Extensions.Logging;

namespace HelpDesk.Infrastructure.Ia;

/// <summary>
/// Etapa "Recuperar" da triagem com RAG (ADR-0004, ADR-0011): gera o embedding do título e da descrição já
/// mascarados e busca os chamados resolvidos e os trechos de artigo mais parecidos. Substitui o
/// <see cref="RecuperadorSemRag"/> sem mexer no pipeline.
/// <para>
/// Se o provedor de embeddings estiver fora, a triagem segue sem contexto (degradação, NFR-04): uma sugestão sem
/// fontes é melhor que nenhuma. O span da etapa registra a falha.
/// </para>
/// </summary>
internal sealed partial class RecuperadorRag(
    IGeradorEmbeddings gerador,
    IBuscaSemantica busca,
    MascaradorDadosPessoais mascarador,
    OpcoesRag opcoes,
    ILogger<RecuperadorRag> logger) : IRecuperadorContexto
{
    public async Task<ContextoRecuperado> RecuperarAsync(
        TextoMascarado titulo, TextoMascarado descricao, CancellationToken cancellationToken)
    {
        float[] vetor;
        try
        {
            // Os dois textos já vêm mascarados; a nova passada só os junta no tipo que o gerador aceita.
            var consulta = mascarador.Mascarar($"{titulo.Valor}\n{descricao.Valor}");
            vetor = (await gerador.GerarAsync([consulta], cancellationToken))[0];
        }
        catch (ProvedorIndisponivelException falha)
        {
            Activity.Current?.SetTag("rag.falha", falha.Tipo);
            LogSemContexto(logger, falha.Tipo);
            return ContextoRecuperado.Vazio;
        }

        var documentos = await busca.BuscarAsync(vetor, gerador.Modelo, opcoes, cancellationToken);

        // Para o prompt vão os trechos (vários do mesmo artigo, se forem os mais parecidos). A fonte exibida ao
        // atendente é a origem, uma vez só, com a similaridade do melhor trecho.
        var fontes = documentos
            .GroupBy(d => (d.Tipo, d.Id))
            .Select(g => g.MaxBy(d => d.Similaridade)!)
            .OrderByDescending(d => d.Similaridade)
            .Select(d => new FonteTriagem(d.Tipo, d.Id, d.Numero, d.Titulo, Math.Round(d.Similaridade, 3)))
            .ToList();
        var trechos = documentos
            .OrderByDescending(d => d.Similaridade)
            .Select(d => mascarador.Mascarar(d.ConteudoMascarado))
            .ToList();
        return new ContextoRecuperado(fontes, trechos);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Recuperação do RAG indisponível ({TipoErro}): a triagem segue sem contexto")]
    private static partial void LogSemContexto(ILogger logger, string tipoErro);
}
