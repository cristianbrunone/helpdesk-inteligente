# ADR-0010 — Filas derivadas do estado das entidades (sem tabela genérica de jobs)

- **Status:** Aceita
- **Data:** 2026-09-30
- **Fase:** 3 — Modelagem de dados
- **Complementa:** [ADR-0003](0003-fila-em-tabela-postgres.md) (tabela como fila com `SKIP LOCKED`)
- **Requisitos relacionados:** RF-02, RF-12, RF-31, NFR-04, NFR-13

## Contexto

O ADR-0003 decidiu usar o PostgreSQL como fila, mas deixou em aberto **qual tabela**. Há três tipos de trabalho assíncrono:

1. **Triagem** de um chamado (na criação e ao "refazer").
2. **Indexação** de um chamado que entrou em Resolvido. Na reabertura, o documento deve ser removido.
3. **Indexação** dos artigos da base de conhecimento (seed e alterações).

Existe ainda um quarto caso importante: **reindexar tudo** quando o modelo de embedding muda.

## Alternativas consideradas

### A) Tabela genérica `trabalhos_ia` (tipo, payload jsonb, status, tentativas, lock)
Toda ação que gera trabalho insere uma linha nessa tabela. O Worker despacha pelo `tipo`.
- ✅ É um mecanismo único, fácil de monitorar num lugar só, e extensível para novos tipos de job.
- ❌ **Dois estados para a mesma coisa:** o status do job e o status da `TriagemIA` precisam ficar sincronizados (job `concluido` com a triagem ainda `pendente` = bug).
- ❌ O payload duplica dados que já estão nas entidades.
- ❌ A reindexação ao trocar de modelo exige **enfileirar N jobs**, e esquecer um caso (por exemplo, a reabertura) deixa o índice inconsistente para sempre.
- ❌ O estilo é imperativo ("faça X"): se um evento se perde, o trabalho se perde.

### B) Filas derivadas do estado ("estado desejado × estado atual")
Cada tipo de trabalho é uma **consulta** sobre o estado das entidades:
- **Triagem:** a própria linha de `triagens_ia` com `status = 'pendente'` e colunas de controle (`tentativas`, `proxima_tentativa_em`, `lock_expira_em`).
- **Indexação:** um *reconciliador* compara o estado desejado com o atual:
  - chamado em Resolvido/Fechado **sem documento**, ou com `hash_conteudo` diferente → indexar;
  - documento cujo chamado **não está mais** resolvido (foi reaberto) → remover;
  - documento com `embedding_modelo` diferente do modelo configurado → reindexar;
  - artigo ativo sem documento ou com hash diferente → indexar.

- ✅ **Uma única fonte de verdade.** O status que a UI mostra **é** o status da fila.
- ✅ **Autocorretivo:** qualquer divergência (worker morto, falha, reabertura, troca de modelo) é resolvida na próxima passada do reconciliador. Nenhum evento pode "se perder".
- ✅ A troca de modelo de embedding reindexa sozinha, sem script de migração.
- ✅ A idempotência é natural (o `hash_conteudo` evita gerar o embedding de novo sem necessidade).
- ❌ O reconciliador faz varreduras periódicas, o que tem custo proporcional ao volume (mitigado por índices parciais e limite por lote).
- ❌ Adicionar um tipo novo de trabalho exige uma consulta e colunas novas, em vez de um novo `tipo` num enum.

## Decisão

Escolhemos **B: filas derivadas do estado**.

- A **triagem** usa `triagens_ia` como fila (claim com `FOR UPDATE SKIP LOCKED`, lease por `lock_expira_em`, backoff exponencial por `proxima_tentativa_em`).
- A **indexação** usa um reconciliador periódico sobre `chamados`, `artigos_conhecimento` e `documentos_rag`, em lotes, também com `SKIP LOCKED`.

O ciclo de vida da triagem permanece com os 5 estados do enunciado. "Em processamento" não é um estado: é `pendente` com `lock_expira_em > now()`.

## Trade-offs aceitos

- O reconciliador adiciona uma pequena carga periódica ao banco (configurável por `WORKER_RECONCILE_INTERVAL_SECONDS`).
- A indexação é eventualmente consistente: o chamado resolvido entra no RAG em segundos, não instantaneamente.

## Consequências

- Os índices parciais `WHERE status = 'pendente'` (triagem) e `WHERE embedding IS NULL` (documentos) mantêm as consultas de fila baratas.
- O índice único parcial `(chamado_id) WHERE status = 'pendente'` impede duas triagens pendentes para o mesmo chamado. "Refazer" com uma triagem pendente responde **409**.
- Os testes de integração cobrem: dois workers concorrentes não pegam a mesma triagem; um lease expirado é retomado; a reabertura remove o documento; a troca de `LLM_EMBEDDING_MODEL` provoca a reindexação.
- **Gatilho de reavaliação:** muitos tipos novos de trabalho não ligados a entidades (por exemplo, notificações). Nesse caso, introduzir uma outbox genérica para *esses* tipos.
