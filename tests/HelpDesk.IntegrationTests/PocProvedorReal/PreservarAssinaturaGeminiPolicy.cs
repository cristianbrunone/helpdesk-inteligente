using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace HelpDesk.IntegrationTests.PocProvedorReal;

/// <summary>
/// PROTÓTIPO da PoC (plano B do ADR-0005, opção A). O Gemini 3 devolve cada chamada de ferramenta com
/// <c>extra_content.google.thought_signature</c> e exige recebê-la de volta na rodada seguinte; o SDK da OpenAI
/// descarta esse campo. Esta política guarda o <c>extra_content</c> por <c>tool_call.id</c> nas respostas e o
/// reinjeta nas requisições. Provedores que não enviam o campo (OpenAI, Ollama) não são afetados.
/// Limitações assumidas no protótipo: só respostas bufferizadas (sem streaming) e dicionário sem expiração.
/// A versão definitiva vai para a Infrastructure na Sprint 4, com streaming e testes unitários.
/// </summary>
internal sealed class PreservarAssinaturaGeminiPolicy : PipelinePolicy
{
    private readonly ConcurrentDictionary<string, JsonNode> _extrasPorChamada = new();

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        Reinjetar(message);
        ProcessNext(message, pipeline, currentIndex);
        Capturar(message);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        Reinjetar(message);
        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
        Capturar(message);
    }

    private void Reinjetar(PipelineMessage message)
    {
        if (_extrasPorChamada.IsEmpty || message.Request.Content is not { } conteudo)
        {
            return;
        }

        using var buffer = new MemoryStream();
        conteudo.WriteTo(buffer);
        if (JsonNode.Parse(buffer.ToArray()) is not JsonObject corpo || corpo["messages"] is not JsonArray mensagens)
        {
            return;
        }

        var alterado = false;
        foreach (var chamada in mensagens.OfType<JsonObject>()
                     .Select(m => m["tool_calls"]).OfType<JsonArray>()
                     .SelectMany(chamadas => chamadas.OfType<JsonObject>()))
        {
            if (chamada["extra_content"] is null
                && chamada["id"]?.GetValue<string>() is { } id
                && _extrasPorChamada.TryGetValue(id, out var extra))
            {
                chamada["extra_content"] = extra.DeepClone();
                alterado = true;
            }
        }

        if (alterado)
        {
            message.Request.Content = BinaryContent.Create(BinaryData.FromString(corpo.ToJsonString()));
        }
    }

    private void Capturar(PipelineMessage message)
    {
        if (!message.BufferResponse || message.Response is not { IsError: false } resposta)
        {
            return;
        }

        if (JsonNode.Parse(resposta.Content.ToMemory().Span) is not JsonObject corpo
            || corpo["choices"] is not JsonArray escolhas)
        {
            return;
        }

        foreach (var chamada in escolhas.OfType<JsonObject>()
                     .Select(e => e["message"]?["tool_calls"]).OfType<JsonArray>()
                     .SelectMany(chamadas => chamadas.OfType<JsonObject>()))
        {
            if (chamada["id"]?.GetValue<string>() is { } id && chamada["extra_content"] is { } extra)
            {
                _extrasPorChamada[id] = extra.DeepClone();
            }
        }
    }
}
