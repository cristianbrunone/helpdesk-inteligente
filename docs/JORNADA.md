# Como construímos o HelpDesk Inteligente

Este documento conta a história do projeto, fase a fase, seguindo o processo de arquitetura adotado. Cada fase lista os artefatos produzidos e as decisões tomadas, com links para os documentos detalhados.

## Processo adotado

1. Entendimento do negócio e requisitos não funcionais.
2. Estilo arquitetural e trade-offs (início do ADD, C4 de Contexto/Contêiner, primeiros ADRs).
3. Modelagem de dados e estratégia de comunicação (ER, contratos de API, novos ADRs), fechando com o **planejamento das sprints**.
4. Walking Skeleton, que corresponde à Sprint 0: CI, observabilidade, segurança base e subida real do ambiente.
5. Padrões de engenharia: `/docs/adr`, guias de teste, linters e revisão.

Regra para os ADRs: toda decisão relevante compara **pelo menos duas alternativas** e registra a escolha, os trade-offs e as consequências.

---

## Fase 1 — Entendimento do negócio e requisitos

**Artefato:** [`01-requisitos.md`](01-requisitos.md)

O que fizemos:

- Convertemos o enunciado em requisitos rastreáveis (RF, RN, NFR), classificados por MoSCoW.
- Definimos o posicionamento do produto para uma vaga de IA Engineer conversacional: **triagem com RAG** e **copiloto com tool calling**, além do mínimo exigido.
- Quantificamos os atributos de qualidade. O mais importante: a criação do chamado **não depende** do LLM.
- Registramos as premissas onde o enunciado era ambíguo. Exemplo: a categoria e a prioridade são opcionais na criação, porque é justamente isso que a IA sugere.

Decisão de escopo relevante: RAG e tool calling **não** são aplicados no mesmo lugar. A triagem usa um pipeline RAG determinístico; o tool calling fica no copiloto conversacional. A justificativa completa virá em ADR na Fase 2.

---

## Fase 2 — Estilo arquitetural e trade-offs

**Artefatos:** [`02-add.md`](02-add.md) (ADD v0.1) e [`adr/0001`](adr/0001-monolito-modular.md) a [`adr/0006`](adr/0006-gemini-free-tier-e-lgpd.md)

O que fizemos:

- Extraímos dos requisitos os **7 direcionadores arquiteturais**. Os decisivos são: a criação não depende do LLM, o LLM é tratado como não confiável, nenhum dado pessoal sai do sistema e o provedor é trocável com fake por padrão.
- Desenhamos o **C4 de Contexto e de Contêiner**. A fronteira de confiança fica explícita: só texto mascarado atravessa para o provedor de LLM.
- Tomamos as decisões de estilo, cada uma comparando duas alternativas:

| ADR | Rejeitamos | Escolhemos | Argumento central |
|---|---|---|---|
| 0001 | Microsserviços | Monólito modular, API + Worker | Uma transação local garante chamado + triagem sem dual-write; o Worker isola o LLM. |
| 0002 | Vertical Slice pura | Clean Architecture pragmática | Máquina de estados e mascaramento são transversais e precisam de um lar único; sem mediator nem AutoMapper. |
| 0003 | Broker externo (RabbitMQ) | Tabela como fila com `SKIP LOCKED` | O broker *também* exigiria outbox; a tabela já é a outbox, com zero infraestrutura extra. |
| 0004 | Agente na triagem | Pipeline RAG determinístico (e agente só no copiloto) | Usar agente só onde a sequência de passos não é conhecida de antemão. |
| 0005 | SDK nativo por provedor | Microsoft.Extensions.AI + endpoint OpenAI-compatível | Um adaptador atende Gemini, OpenAI e Ollama; o fake no nível mais baixo exercita a validação real. |
| 0006 | Só Ollama ou tier pago | Gemini free tier opt-in + mascaramento tipado | O free tier pode usar os dados, então o mascaramento vira garantia de compilação (`TextoMascarado`). |

Pesquisa feita nesta fase: confirmamos na documentação oficial que o Gemini oferece endpoint compatível com OpenAI (tools, saída estruturada, embeddings) e que o free tier pode usar os dados para melhoria de produto. Os dois fatos mudaram decisões (ADR-0005 e ADR-0006).

Riscos levados para a Fase 4 (PoC): as lacunas de compatibilidade do Gemini com `json_schema` e tools, e o rate limit do free tier.

---

## Fase 3 — Modelagem de dados e comunicação

**Artefatos:** [`03-modelo-de-dados.md`](03-modelo-de-dados.md), [`04-contratos-api.md`](04-contratos-api.md), ADD v0.2 (seções 10–12), [`adr/0007`](adr/0007-pgvector-no-postgres.md) a [`adr/0013`](adr/0013-minimal-apis.md) e [`05-sprints.md`](05-sprints.md)

O que fizemos:

- **Modelamos os dados pensando no que o banco deve garantir sozinho.**
  - As regras verificáveis viraram `CHECK`: `resolvido_em` coerente com o status, Crítico nunca cancelado e uma única triagem pendente por chamado.
  - A máquina de estados ficou só no domínio, porque duplicá-la num trigger seria manter a mesma regra em duas linguagens.
  - Enums nativos cuja ordem de declaração é a ordem de negócio, o que faz o `ORDER BY prioridade` funcionar sem `CASE`.
- **Justificamos cada índice** a partir dos filtros e ordenações da listagem. Também registramos os índices que decidimos **não** criar, e por quê.
- **Escrevemos o SQL do dashboard antes do código**, com `FILTER`, `ROLLUP` e `NULLIF`, porque a qualidade das consultas é um critério de avaliação.
- **Definimos os contratos da API**:
  - catálogo de erros com semântica clara entre 400, 409, 412 e 422;
  - `transicoesPermitidas` calculadas pelo domínio e entregues ao frontend;
  - contrato de eventos SSE do copiloto.
- **Desenhamos a topologia**: o Nginx faz o proxy de `/api` (sem CORS), e as migrations rodam num serviço one-shot.

Decisões desta fase:

| ADR | Rejeitamos | Escolhemos | Argumento central |
|---|---|---|---|
| 0007 | Qdrant | pgvector | Vetor e dado de negócio na mesma transação e na mesma consulta; FK com cascade. |
| 0008 | Full-text search | `pg_trgm` + `unaccent` | O atendente busca códigos e trechos (`ERR-5`), não radicais linguísticos. |
| 0009 | LINQ no dashboard | SQL explícito | O SQL é o artefato avaliado, então precisa estar visível. |
| 0010 | Tabela genérica de jobs | Filas derivadas do estado | Nenhum evento pode se perder; trocar o modelo de embedding reindexa sozinho. |
| 0011 | Embedding em coluna | Tabela `documentos_rag` com 768 dimensões | Busca unificada, chunking, e fake e real compatíveis. |
| 0012 | JSON completo | SSE | Numa vaga de IA conversacional, mostrar "Consultando chamados semelhantes…" é parte do produto. |
| 0013 | Controllers | Minimal APIs | Endpoints finos por construção, com validação nativa do .NET 10. |

**Planejamento:** fechamos a fase com 6 sprints em fatias verticais (Sprint 0 = Walking Skeleton + PoC de IA). Cada sprint termina demonstrável, e há uma linha de corte explícita caso o prazo aperte.

Aprendizado: a pergunta "onde fica a fila?", deixada em aberto no ADR-0003, só foi bem respondida com o modelo de dados na mesa. A resposta (estado da entidade = fila) eliminou uma tabela inteira e um problema de sincronização.

---

## Fase 4 — Walking Skeleton (Sprint 0)

_(a preencher)_
