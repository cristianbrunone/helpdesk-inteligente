# ADR-0005 — Abstração do provedor de LLM: Microsoft.Extensions.AI + adaptador OpenAI-compatível

- **Status:** Aceita (a validar na PoC da Fase 4)
- **Data:** 2026-09-30
- **Fase:** 2 — Estilo arquitetural
- **Requisitos relacionados:** NFR-04, NFR-08, NFR-09, NFR-10, NFR-11, RF-17, D2, D4

## Contexto

O enunciado exige que a integração com o LLM fique atrás de uma interface, com uma implementação fake selecionada por variável de ambiente, e que o projeto rode sem chave de API. Queremos o Gemini (free tier) como provedor real de demonstração, **sem fechar a porta** para OpenAI, Azure OpenAI, Anthropic ou Ollama. Precisamos de três capacidades:

- chat com **saída estruturada** (triagem);
- chat com **tool calling** (copiloto);
- **embeddings** (RAG).

Fatos verificados em 2026-09-30:

- O Gemini expõe um **endpoint compatível com a API da OpenAI**, que suporta function calling, saída estruturada e embeddings ([Google AI — OpenAI compatibility](https://ai.google.dev/gemini-api/docs/openai)). O mesmo vale para Ollama e outros.
- O pacote **Microsoft.Extensions.AI** define as abstrações padrão do ecossistema .NET (`IChatClient`, `IEmbeddingGenerator<TInput,TEmbedding>`), com helpers para invocação automática de ferramentas e middlewares de telemetria e cache ([Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai)). O pacote `Microsoft.Extensions.AI.OpenAI` implementa essas abstrações para a OpenAI e para **endpoints compatíveis**.

## Alternativas consideradas

### A) Interface própria + SDK nativo por provedor
Criamos `ILlmProvider` e `IEmbeddingProvider`, com uma implementação por provedor usando o SDK oficial de cada um (Google GenAI, OpenAI, Anthropic...).
- ✅ Controle total e acesso a recursos específicos de cada provedor.
- ✅ Nenhuma dependência de uma abstração de terceiros.
- ❌ Um adaptador inteiro (chat, tools, schema, embeddings, tokens) **por provedor**.
- ❌ Reimplementar o loop de tool calling, a telemetria e o retry.
- ❌ Não é o padrão do ecossistema .NET, então quem ler o código precisa aprender a nossa abstração.

### B) Microsoft.Extensions.AI como abstração + um adaptador OpenAI-compatível
A `Application` depende de `IChatClient` e `IEmbeddingGenerator`. Um único adaptador (`Microsoft.Extensions.AI.OpenAI`) configurado por `base_url` atende Gemini, OpenAI, Ollama e outros. O fake implementa as mesmas interfaces.
- ✅ **Um adaptador atende vários provedores:** trocar de provedor = trocar `LLM_BASE_URL`, `LLM_API_KEY` e `LLM_MODEL`.
- ✅ É o padrão .NET, com loop de tool calling pronto (`UseFunctionInvocation`) e middlewares de OpenTelemetry e logging.
- ✅ Provedores sem compatibilidade OpenAI (por exemplo, a Anthropic nativa) entram como **outra** implementação de `IChatClient`, sem tocar na `Application`.
- ✅ O fake no nível de `IChatClient` faz o **parsing e a validação reais rodarem** em modo fake e nos testes (D4).
- ❌ A compatibilidade OpenAI do Gemini pode ter lacunas em modelos ou recursos específicos, como o suporte a `json_schema`.
- ❌ Há dependência de um pacote Microsoft (que é oficial e estável).

## Decisão

Escolhemos **B**, com uma **porta de caso de uso fina por cima**, para que o domínio não "vaze" para a abstração de chat:

```
Application
  ITriagemLlm.SugerirAsync(ContextoTriagem) → ResultadoLlm   // porta do caso de uso
  ICopilotoLlm.ResponderAsync(conversa, ferramentas)
  (ambas implementadas com IChatClient; embeddings via IEmbeddingGenerator)

Infrastructure
  Provedores/
    Fake/          FakeChatClient (determinístico), FakeEmbeddingGenerator (hash → vetor)
    OpenAiCompat/  configuração do cliente OpenAI com base_url (Gemini, OpenAI, Ollama...)
  Resiliencia/     timeout + retry com backoff/jitter (tratando 429/5xx como transitórios)
```

Seleção por ambiente:

| Variável | Exemplo | Observação |
|---|---|---|
| `LLM_PROVIDER` | `fake` \| `openai-compatible` | O padrão é `fake`. |
| `LLM_BASE_URL` | `https://generativelanguage.googleapis.com/v1beta/openai/` | Gemini. |
| `LLM_API_KEY` | (só no `.env`, nunca no repositório) | |
| `LLM_CHAT_MODEL` / `LLM_EMBEDDING_MODEL` | um modelo Flash do free tier | Configurável. Não fica fixo no código. |
| `LLM_TIMEOUT_SECONDS` / `LLM_MAX_RETRIES` | `15` / `2` | NFR-04. |

## Trade-offs aceitos

- Recursos exclusivos de um provedor (por exemplo, o *grounding* do Google) ficam de fora do caminho padrão.
- A validação da saída **não confia** no `json_schema` do provedor. O schema é pedido, mas o parsing e a validação próprios são sempre executados (NFR-05). Isso também cobre provedores que só suportam `json_object`.

## Consequências

- **PoC obrigatória na Fase 4:** validar, com o Gemini real, o structured output, uma rodada de tool calling e os embeddings pelo endpoint compatível. Se falhar, o plano B é um adaptador `IChatClient` nativo para o Gemini, sem impacto na `Application`.
- A telemetria de IA (latência, tokens, modelo, sucesso/falha) é implementada como um middleware de `IChatClient`, num único lugar para triagem e copiloto.
- O README documenta como apontar para OpenAI, Ollama local ou outro endpoint compatível.
- **Gatilho de reavaliação:** necessidade de um provedor sem endpoint compatível. Nesse caso, entra uma nova implementação de `IChatClient`, e esta decisão continua válida.
