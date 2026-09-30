# ADR-0019 — Tracing distribuído com OpenTelemetry

- **Status:** Aceita
- **Data:** 2026-09-30
- **Fase:** revisão de arquitetura ([registro](../revisoes/2026-09-30-padroes-agenticos.md), lacuna L2)
- **Requisitos relacionados:** NFR-11, NFR-17 (novo), RF-17

## Contexto

O desenho já prevê **logs estruturados** com correlation id e o registro de consumo em `uso_llm` (NFR-11). Isso responde "quantos tokens essa triagem gastou?", mas não responde:

- **em que etapa** do pipeline o tempo foi gasto (recuperar? completar? retries?);
- **quantas tentativas** houve e quanto cada uma demorou;
- como uma requisição da API se relaciona com o trabalho que o Worker fez depois;
- quais ferramentas o copiloto chamou, em que ordem e com que latência.

Esse tipo de pergunta pede **traces**: uma árvore de *spans* aninhados com tempo e atributos. O enunciado cita "métricas ou tracing (OpenTelemetry)" como diferencial. Na Fase 1, o OpenTelemetry tinha ficado como Could.

## Alternativas consideradas

### A) Somente logs estruturados + `uso_llm`
- ✅ Já está planejado, sem dependências novas.
- ❌ Reconstruir a sequência de etapas exige correlacionar linhas de log manualmente.
- ❌ Não há visualização de latência por etapa nem da árvore de chamadas do agente.

### B) OpenTelemetry (traces + métricas), com exportação OTLP e o Aspire Dashboard opcional
- ✅ É um padrão aberto e neutro de fornecedor: o mesmo código exporta para Jaeger, Grafana, Azure Monitor ou Datadog.
- ✅ O `Microsoft.Extensions.AI` já oferece o middleware `UseOpenTelemetry()` para `IChatClient` e `IEmbeddingGenerator`, seguindo as **convenções semânticas de GenAI**: spans de chat e de embedding com modelo, tokens e duração.
- ✅ A instrumentação de ASP.NET Core, HttpClient e Npgsql vem pronta. Os spans do pipeline são poucas linhas com `ActivitySource`.
- ✅ O **Aspire Dashboard** tem uma imagem Docker própria, funciona como visualizador OTLP local e mostra traces, métricas e logs sem nenhuma conta ([documentação](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/dashboard/standalone)).
- ❌ Adiciona 4 ou 5 pacotes e um contêiner opcional.
- ❌ Existe o risco de colocar conteúdo sensível em atributos de span (mitigado abaixo).

## Decisão

Escolhemos **B**, em versão enxuta:

- **Spans do pipeline de triagem:** `triagem.processar` (raiz, no Worker) → `mascarar` · `recuperar` · `montar_prompt` · `completar` (com os spans do `IChatClient` como filhos, um por tentativa) · `validar`.
- **Spans do copiloto:** `copiloto.responder` → um span por rodada de ferramenta (`ferramenta.buscar_chamados_similares`...) → spans do `IChatClient`.
- **Atributos permitidos:** `triagem.id`, `chamado.id`, `prompt.versao`, `rag.top_k`, `rag.documentos`, `validacao.resultado`, `validacao.motivo`, provedor, modelo e tokens. **Nunca** o texto do prompt, da resposta, do chamado ou dos resultados de ferramenta. A opção `EnableSensitiveData` do middleware permanece **desligada**.
- **Propagação:** a API cria a triagem pendente; o Worker inicia um novo trace, **vinculado** (*span link*) ao `traceparent` que foi gravado junto com a triagem. A criação e o processamento assíncrono ficam navegáveis.
- **Exportação:** OTLP via `OTEL_EXPORTER_OTLP_ENDPOINT`. Se a variável não estiver definida, não há exportação (sem custo e sem erro).
- **Visualização:** o serviço `aspire-dashboard` fica no Compose sob o **profile** `observabilidade`. O `docker compose up` padrão continua com 5 serviços. `docker compose --profile observabilidade up` sobe o painel em `http://localhost:18888`.

**Pacotes novos** (a aprovar no `CLAUDE.md`): `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http` e `Npgsql.OpenTelemetry`.

## Trade-offs aceitos

- Sem sampling configurado: todos os traces são exportados, o que é adequado ao volume local. Em produção, seria configurado sampling por taxa.
- O painel é local e efêmero, sem retenção. Serve para desenvolvimento e demonstração, não para operação.

## Consequências

- Um teste de integração verifica, com um `ActivityListener`, que o pipeline emite os 5 spans esperados e que **nenhum atributo contém o texto do chamado** (o teste injeta um CPF conhecido e procura por ele nos atributos).
- O README ganha uma seção "Observabilidade", com uma captura de tela de um trace de triagem.
- A `uso_llm` continua existindo: traces são para investigar, e a tabela é para agregar custo no dashboard.
- **Gatilho de reavaliação:** implantação em nuvem → trocar o destino OTLP pelo coletor do provedor e ativar sampling.
