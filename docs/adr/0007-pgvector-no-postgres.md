# ADR-0007 — pgvector no próprio PostgreSQL como armazenamento vetorial

- **Status:** Aceita
- **Data:** 2026-09-30
- **Fase:** 3 — Modelagem de dados
- **Requisitos relacionados:** RF-15, RF-21, RF-31, RN-11, NFR-02, NFR-08, D5, D6

## Contexto

O RAG da triagem e as ferramentas do copiloto precisam de busca por similaridade sobre dois conjuntos: chamados resolvidos (~centenas) e artigos da base de conhecimento (~dezenas, com seus trechos). A busca frequentemente combina o **vetor** com **filtros relacionais** (por exemplo, só chamados em `Resolvido`/`Fechado`, ou só uma categoria). O banco relacional (PostgreSQL) já é obrigatório pelo enunciado, e o próprio enunciado sugere pgvector.

## Alternativas consideradas

### A) Banco vetorial dedicado (Qdrant, Weaviate, Milvus)
- ✅ Especializado: ANN de alta performance, filtros por payload e escala para milhões de vetores.
- ✅ Recursos avançados prontos (busca híbrida, quantização, multi-tenancy).
- ❌ **Mais um contêiner** e mais um ponto de falha no `docker compose up`.
- ❌ **Consistência dual:** o chamado é resolvido no Postgres e o vetor vive em outro sistema. Isso exige sincronização (outbox) e reconciliação, e abre janelas de divergência.
- ❌ Os filtros relacionais precisam ser **duplicados** como payload no vetor store.
- ❌ O volume do projeto (< 10 mil vetores) não usa nenhuma das vantagens de escala.

### B) Extensão pgvector no mesmo PostgreSQL
- ✅ Zero infraestrutura nova: basta trocar a imagem do Postgres por uma com a extensão.
- ✅ **Vetor e dado de negócio na mesma transação e na mesma consulta.** O `JOIN`/`WHERE` com status e categoria é SQL comum.
- ✅ Integridade referencial: o documento vetorial tem FK para o chamado/artigo, com `ON DELETE CASCADE`.
- ✅ O índice HNSW entrega latência de milissegundos nessa escala.
- ✅ O backup, as migrations e os testes (Testcontainers) cobrem tudo de uma vez.
- ❌ A dimensão do vetor é fixa por coluna, então a troca de modelo com dimensão diferente exige uma migration (mitigado no ADR-0011).
- ❌ Em escala muito grande (dezenas de milhões de vetores, alto QPS), a busca compete por recursos com o OLTP.

## Decisão

Escolhemos **B: pgvector**, usando **distância de cosseno** (`vector_cosine_ops`) e um **índice HNSW**.

## Trade-offs aceitos

- A carga vetorial compartilha CPU e memória com a carga transacional.
- A busca híbrida (vetor + palavra-chave com re-ranking) fica para uma próxima versão.

## Consequências

- A imagem do banco no Compose passa a ser a `pgvector/pgvector` (tag fixada na Sprint 0), e a migration inicial executa `CREATE EXTENSION IF NOT EXISTS vector`.
- A busca é encapsulada na porta `IBuscaSemantica`, então a `Application` não conhece o pgvector.
- Os testes de integração usam a mesma imagem via Testcontainers, sem mock da busca vetorial.
- **Gatilho de reavaliação:** mais de ~5 milhões de vetores, p95 da busca acima de 100 ms com HNSW ajustado, ou necessidade de busca híbrida nativa em escala. Nesse caso, migrar para um vector store dedicado atrás da mesma porta.
