# ADR-0016 — Logs estruturados com o logging nativo do .NET (JSON no stdout)

- **Status:** Aceita
- **Data:** 2026-09-30
- **Fase:** 4 — Walking Skeleton (Sprint 0), decisão de plataforma
- **Requisitos relacionados:** NFR-06, NFR-11; ADR-0019 (tracing)

## Contexto

O NFR-11 exige logs estruturados em JSON com correlation id, e as chamadas ao LLM devem registrar provedor, modelo, latência, tokens e sucesso/falha, sem conteúdo. O NFR-06 proíbe dados pessoais em logs. O critério de aceite da Sprint 0 pede que **cada requisição gere um log JSON com `CorrelationId`** e que o header `X-Correlation-Id` volte na resposta.

Os processos rodam em contêineres, então o destino natural dos logs é o **stdout**. A partir da Sprint 2, o ADR-0019 adiciona OpenTelemetry com exportação OTLP (Aspire Dashboard opcional), e os logs precisam aparecer ligados aos traces.

## Alternativas consideradas

### A) Serilog (`Serilog.AspNetCore` + formatter JSON compacto)
- ✅ Biblioteca madura e muito conhecida, com enrichers e configuração por `appsettings`.
- ✅ `UseSerilogRequestLogging()` gera uma linha resumida por requisição sem código próprio.
- ✅ Ecossistema grande de sinks (Seq, Elastic, arquivo...).
- ❌ Dois pacotes agora e mais um na Sprint 2 (`Serilog.Sinks.OpenTelemetry`), para que os logs cheguem ao Aspire ligados aos traces.
- ❌ É um segundo pipeline de logging, paralelo ao `Microsoft.Extensions.Logging`, com modelo de configuração próprio.
- ❌ Não resolve PII sozinho: a disciplina de templates sem conteúdo pessoal continua sendo nossa.

### B) Logging nativo do .NET (`AddJsonConsole` + scopes)
- ✅ **Nenhum pacote novo.** O JSON console formatter é nativo e inclui os scopes, por onde o `CorrelationId` entra.
- ✅ **Alinhado ao ADR-0019:** o provider de logs do OpenTelemetry (no pacote já aprovado `OpenTelemetry.Extensions.Hosting`) exporta os mesmos `ILogger` via OTLP, com `TraceId` e `SpanId` automáticos. Log e trace se ligam sem ponte extra.
- ✅ `[LoggerMessage]` (source generator) valida templates em tempo de compilação e evita alocações.
- ❌ Formato JSON pouco customizável: os nomes dos campos (`Timestamp`, `LogLevel`, `Category`, `State`, `Scopes`) são fixos.
- ❌ A linha resumida por requisição exige um middleware pequeno nosso. O `AddHttpLogging` foi descartado porque, mal configurado, registra headers e corpo (risco de PII).
- ❌ Não há sinks além de console e OTLP.

## Decisão

Escolhemos **B: logging nativo do .NET, em JSON no stdout**.

- **Formato:** `AddJsonConsole` com `IncludeScopes = true`, `UseUtcTimestamp = true` e timestamp ISO 8601. O JSON indentado fica desligado (uma linha por evento).
- **Correlação:** um middleware aceita o `X-Correlation-Id` (ou gera um), devolve o header na resposta, abre um scope `CorrelationId` e grava o valor no `ProblemDetails`. Na Sprint 2, o Worker abre o mesmo scope por item processado.
- **Linha por requisição:** o mesmo middleware registra, ao final, método, rota, status e duração. **Nunca** query string, headers ou corpo.
- **Níveis** por variável de ambiente (`Logging__LogLevel__Default`, `Logging__LogLevel__Microsoft.AspNetCore`...). Os padrões ficam em `appsettings.json`.
- **Dados pessoais:** templates estruturados sem conteúdo do usuário (a regra CA2254 já é `warning` no `.editorconfig`); o `EnableSensitiveDataLogging` do EF Core fica **sempre** desligado, então parâmetros de SQL nunca aparecem.
- **IA (Sprint 2):** provedor, modelo, latência, tokens, resultado e tipo de erro, como campos estruturados, sem prompt nem resposta.

## Trade-offs aceitos

- Nomes de campos do JSON definidos pelo .NET, e não por nós.
- O middleware de correlação e resumo é código nosso (cerca de 40 linhas), com teste.

## Consequências

- `HelpDesk.Api` (e, depois, `Worker` e `Migrator`) configura o `AddJsonConsole` no `Program.cs`.
- Teste de integração: uma requisição com e sem `X-Correlation-Id` → o header volta, e o log capturado traz o mesmo `CorrelationId`.
- Na Sprint 2, entra o provider OTLP de logs junto com o tracing (ADR-0019), sem mudar o código que escreve logs.
- **Gatilho de reavaliação:** precisar de destinos além de stdout e OTLP (Seq, Elastic direto) ou de enriquecimento que o pipeline nativo não suporte. Nesse caso, entra o Serilog com o sink de OpenTelemetry, sem mudar as chamadas a `ILogger`.
