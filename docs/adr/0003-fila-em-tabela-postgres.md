# ADR-0003 — Fila de trabalho em tabela PostgreSQL (SKIP LOCKED)

- **Status:** Aceita
- **Data:** 2026-09-30
- **Fase:** 2 — Estilo arquitetural
- **Requisitos relacionados:** RF-02, RF-12, RF-31, NFR-01, NFR-04, NFR-08, NFR-13, D1, D6

## Contexto

A triagem precisa ser assíncrona (D1): o `POST /api/chamados` responde sem esperar o LLM. O pedido de triagem precisa nascer **atomicamente** com o chamado. Se o chamado for gravado e o pedido de triagem se perder, o sistema fica inconsistente (o clássico problema do *dual write*). O enunciado cita fila e background job como diferencial e menciona "tabela outbox" como opção válida.

Também foi descartado, de saída, o *fire-and-forget* dentro da API (`Task.Run` ou um `Channel` em memória): um restart do processo perde trabalho, e a API volta a ficar acoplada ao LLM.

## Alternativas consideradas

### A) Broker externo (RabbitMQ / Redis Streams) + outbox
A API grava o chamado e uma linha de outbox. Um relay publica a mensagem no broker, e o Worker consome.
- ✅ É o padrão de mercado para arquitetura orientada a eventos, com roteamento, DLQ nativa e fan-out.
- ✅ Escala bem para alto volume.
- ❌ Mais um contêiner, mais configuração e mais um ponto de falha no `docker compose up`.
- ❌ **Mesmo assim exige a outbox** para não ter dual-write. O broker vira uma camada *a mais* sobre a tabela, não em vez dela.
- ❌ O volume esperado (dezenas a centenas de triagens por dia) não justifica o custo operacional.

### B) A tabela como fila, consumida com `FOR UPDATE SKIP LOCKED`
A própria linha de `TriagemIA` com status `pendente` (mais os campos de controle: tentativas, próximo agendamento e lock) **é** o item da fila. O Worker faz polling curto e reivindica itens com `SELECT ... FOR UPDATE SKIP LOCKED`.
- ✅ **Atomicidade grátis:** o chamado e a triagem pendente vão no mesmo `INSERT` transacional.
- ✅ Zero infraestrutura extra. O Postgres já está no Compose.
- ✅ `SKIP LOCKED` permite várias réplicas do Worker sem processamento duplicado.
- ✅ Estado observável por SQL simples (quantas pendentes, quantas falharam), o que é útil para o dashboard e para debug.
- ✅ Retry com backoff modelado em colunas (`tentativas`, `proxima_tentativa_em`).
- ❌ O polling adiciona latência (de 1 a 2 s) e gera carga leve no banco. É mitigável com `LISTEN/NOTIFY`.
- ❌ Não serve para fan-out a vários consumidores nem para alto throughput. Esses não são requisitos.

## Decisão

Escolhemos **B: tabela como fila com `SKIP LOCKED`**.

O mesmo mecanismo atende três tipos de trabalho: triagem (criação e "refazer"), indexação de chamado resolvido e indexação de artigos.

Um ponto fica em aberto para a Fase 3: usar a tabela `TriagemIA` diretamente como fila ou criar uma tabela genérica `trabalho_ia` (tipo, payload, tentativas...). Essa decisão sai junto com o modelo ER.

## Trade-offs aceitos

- A latência de pickup é de 1 a 2 s. É irrelevante perto da latência do LLM (NFR-03: < 30 s).
- Se o volume crescer muito, a fila migra para um broker. A troca fica contida numa porta (`IFilaTrabalho`).

## Consequências

- O Worker roda um `BackgroundService` com um loop de polling configurável (`WORKER_POLL_INTERVAL_MS`), que processa em lote (`WORKER_BATCH_SIZE`).
- Os itens que ficam presos (Worker morto no meio do processamento) são recuperados por *lease timeout* (`lock_expira_em`).
- Depois de N tentativas (`LLM_MAX_RETRIES`), o item vai para `falhou` com o motivo registrado. Isso funciona como uma dead-letter queue, visível na UI.
- Há um teste de integração que garante que duas instâncias concorrentes não processam o mesmo item.
- **Gatilho de reavaliação:** mais de ~50 triagens/s, ou necessidade de outros consumidores dos eventos (notificações, analytics). A tabela vira outbox, e o broker é adicionado atrás da porta.
