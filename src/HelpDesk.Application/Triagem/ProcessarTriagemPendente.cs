using System.Diagnostics;
using HelpDesk.Application.Chamados;
using HelpDesk.Domain.Triagem;

namespace HelpDesk.Application.Triagem;

/// <summary>
/// Processa uma triagem reservada da fila (Worker): roda o pipeline e grava o resultado. Se a mesma triagem já foi
/// reservada mais vezes que o limite (o Worker caiu no meio repetidamente), ela vira <c>Falhou</c>: é a
/// "dead-letter queue" do ADR-0003, visível na UI e resolvida com "Refazer".
/// </summary>
public sealed class ProcessarTriagemPendente(
    IRepositorioTriagens triagens,
    IRepositorioChamados chamados,
    PipelineTriagem pipeline,
    IClienteLlmTriagem cliente,
    MontadorPromptTriagem montador,
    TimeProvider relogio)
{
    public const string CodigoInterrompida = "interrompida";

    private static readonly ActivitySource _fonte = new(PipelineTriagem.NomeFonteAtividades);

    /// <returns>O resultado, ou <c>null</c> se a triagem não está mais pendente (outro Worker já a resolveu).</returns>
    public async Task<ResultadoPipeline?> ExecutarAsync(
        Guid triagemId, int maxReservas, CancellationToken cancellationToken)
    {
        var triagem = await triagens.ObterAsync(triagemId, cancellationToken);
        if (triagem is not { Status: StatusTriagem.Pendente })
        {
            return null;
        }

        // ADR-0019: o processamento é um trace próprio, vinculado (span link) ao trace da criação do chamado.
        using var raiz = IniciarRaiz(triagem);
        ResultadoPipeline resultado;
        if (triagem.Tentativas > maxReservas)
        {
            triagem.Falhar("O processamento desta triagem foi interrompido várias vezes. Tente refazer.",
                new ExecucaoTriagem(cliente.Provedor, cliente.Modelo, montador.Versao),
                relogio.GetUtcNow());
            resultado = new ResultadoPipeline(StatusTriagem.Falhou, CodigoInterrompida);
        }
        else
        {
            var chamado = await chamados.ObterParaAlteracaoAsync(triagem.ChamadoId, cancellationToken)
                ?? throw new InvalidOperationException($"O chamado da triagem {triagemId} não existe.");
            resultado = await pipeline.ProcessarAsync(triagem, chamado, cancellationToken);
        }

        // Mesma unidade de trabalho: só a triagem muda (o chamado não é alterado pela triagem).
        await chamados.SalvarAsync(cancellationToken);
        raiz?.SetTag("triagem.status", resultado.Status.ToString());
        raiz?.SetTag("triagem.falha", resultado.Codigo);
        return resultado;
    }

    private static Activity? IniciarRaiz(TriagemIA triagem)
    {
        ActivityLink[] links = ActivityContext.TryParse(triagem.TraceParent, null, out var criacao)
            ? [new ActivityLink(criacao)]
            : [];
        var raiz = _fonte.StartActivity("triagem.processar", ActivityKind.Consumer, parentContext: default,
            links: links);
        raiz?.SetTag("triagem.id", triagem.Id);
        raiz?.SetTag("chamado.id", triagem.ChamadoId);
        raiz?.SetTag("triagem.reserva", (int)triagem.Tentativas);
        return raiz;
    }
}
