# ADR-0024 — Novas tentativas ao provedor de IA no cliente de chat, e não na fila

- **Status:** Aceita
- **Data:** 2026-10-01
- **Fase:** 4 — Sprint 2 (triagem por IA)
- **Requisitos relacionados:** NFR-04, NFR-17, RF-11
- **Decisões relacionadas:** [ADR-0003](0003-fila-em-tabela-postgres.md), [ADR-0005](0005-abstracao-provedor-llm.md), [ADR-0010](0010-filas-derivadas-do-estado.md), [ADR-0019](0019-tracing-opentelemetry.md)

## Contexto

O provedor real falha de forma transitória com frequência: a PoC mediu 429 (limite por minuto do free tier), 503 ("high demand") e latências de 5 a 21 s. O NFR-04 exige timeout e novas tentativas configuráveis, com 429/5xx tratados como transitórios.

Os ADRs anteriores deixavam ambíguo **onde** essas tentativas acontecem:

- o ADR-0005 previa um componente de resiliência no adaptador (timeout + retry com backoff);
- o ADR-0003 dizia que, "depois de N tentativas, o item vai para `falhou`", e o ADR-0010 previa backoff exponencial em `proxima_tentativa_em`, isto é, tentativas pela fila.

Fazer as duas coisas multiplicaria as tentativas (3 retries no cliente × 3 reservas na fila = até 12 chamadas por triagem) e queimaria a cota diária do free tier.

## Alternativas consideradas

### A) No cliente de chat (middleware do `IChatClient`)
Um `DelegatingChatClient` faz até `LLM_MAX_RETRIES` novas tentativas dentro da mesma chamada, com timeout por tentativa, backoff exponencial + jitter e respeito ao `Retry-After`. Esgotadas as tentativas, a triagem fica `Falhou` ("provedor indisponível") e o atendente pode refazer. A fila (lease + backoff) fica só para retomar triagens de um Worker que caiu no meio.
- ✅ O trace de uma triagem mostra todas as tentativas juntas, uma por span, como o ADR-0019 descreve.
- ✅ Funciona igual com o fake e com qualquer provedor; os modos de falha do fake exercitam o caminho inteiro em teste.
- ✅ O critério "timeout → retries → `Falhou`" é testável em segundos, sem manipular o relógio da fila.
- ✅ Uma regra só para triagem e copiloto (o copiloto não passa pela fila).
- ❌ O Worker fica ocupado durante as esperas (segundos, limitadas a 30 s cada; 60 s se o provedor pedir).
- ❌ Um 429 de limite **por minuto** pode esgotar as tentativas antes de a janela abrir; a triagem vira `Falhou` e precisa ser refeita.

### B) Pela fila
Sem retry dentro da chamada: cada falha transitória devolve a triagem para `Pendente` com `proxima_tentativa_em = agora + backoff` (que pode ser de minutos).
- ✅ Esperas longas sem ocupar o Worker, bom para limites por minuto.
- ✅ Sobrevive a reinícios do Worker entre as tentativas.
- ❌ Cada tentativa vira um trace separado: a investigação de uma triagem fica espalhada.
- ❌ O copiloto (síncrono, Sprint 4) precisaria de outra estratégia, e haveria duas regras de retry no sistema.
- ❌ Testes mais lentos ou dependentes de manipular `proxima_tentativa_em`.

## Decisão

Escolhemos **A: as novas tentativas ficam no cliente de chat** (`ResilienciaChatClient`), com o retry interno do SDK da OpenAI **desligado** para não multiplicar tentativas. A fila mantém o lease e o backoff **apenas** para recuperar triagens de um Worker que caiu, e uma triagem reservada mais de 3 vezes vira `Falhou` ("interrompida"), a dead-letter do ADR-0003.

## Trade-offs aceitos

- Um 429 por minuto que dure mais que as tentativas configuradas resulta em `Falhou`; o atendente refaz. Com o modelo padrão (`gemini-3.5-flash-lite`, 15 RPM), isso só acontece sob rajadas.
- O Worker processa uma triagem por vez dentro do lote; as esperas de backoff atrasam as seguintes. Aceitável no volume do projeto.

## Consequências

- Cadeia do cliente: resiliência → telemetria (`uso_llm`, uma linha **por tentativa**) → span GenAI do OpenTelemetry → provedor.
- Falhas transitórias: 429, 500, 502, 503, 504, timeout e falhas de transporte (o SDK as entrega como `ClientResultException` sem resposta HTTP, status 0). Falhas definitivas (400, 401, 404) e o cancelamento do Worker não são repetidos.
- O lease padrão (`WORKER_LEASE_SECONDS`) cobre o pior caso das tentativas: `timeout × (retries + 1) + 60 s`, no mínimo 300 s.
- **Gatilho de reavaliação:** se a taxa de triagens `Falhou` por `rate_limit` for relevante no dashboard (`uso_llm`), mover o caso específico do 429 para a fila (alternativa B só para ele), mantendo os demais no cliente.
