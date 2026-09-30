# ADR-0008 — Busca textual da listagem com `pg_trgm` (substring) em vez de full-text search

- **Status:** Aceita
- **Data:** 2026-09-30
- **Fase:** 3 — Modelagem de dados
- **Requisitos relacionados:** RF-03, NFR-02, NFR-14

## Contexto

A listagem precisa filtrar por "texto no título/descrição". No uso real, o atendente busca **pedaços** de texto: códigos de erro (`ERR-504`), nomes de sistema (`SAP`), números de nota fiscal e palavras incompletas (`boleto` e `boletos`). Muitas vezes digita **sem acento** (`nao consigo`, `configuracao`). A busca precisa usar índice (NFR-02). Busca por *significado* já existe no sistema via embeddings (ADR-0007), então esta decisão trata apenas da busca literal.

## Alternativas consideradas

### A) Full-text search do PostgreSQL (`tsvector` com configuração `portuguese`)
- ✅ Faz stemming (`boletos` → `boleto`), remove stopwords e tem ranking por relevância (`ts_rank`).
- ✅ Índice GIN eficiente.
- ❌ **Não encontra substrings nem códigos:** `ERR-5` não acha `ERR-504`, e tokens técnicos são quebrados de forma pouco intuitiva.
- ❌ O stemming em português às vezes surpreende o usuário (o termo buscado não aparece literalmente no resultado).
- ❌ A semântica é "contém as palavras", não "contém o texto". Isso é mais difícil de explicar e de testar.

### B) `ILIKE '%termo%'` acelerado por índice GIN com `pg_trgm`, insensível a acento
- ✅ A semântica é previsível: "contém este trecho", que é o que o usuário espera de uma caixa de busca de chamados.
- ✅ Encontra códigos, prefixos, sufixos e palavras parciais.
- ✅ O índice GIN de trigramas atende `ILIKE` com termos de 3 ou mais caracteres.
- ✅ Com uma expressão `f_unaccent(lower(...))`, a busca fica insensível a maiúsculas e acentos.
- ❌ Não há ranking por relevância (a ordenação continua sendo por data ou prioridade, como pede o enunciado).
- ❌ Não há stemming: buscar `boletos` não encontra um texto que só tem `boleto` (o inverso funciona, por ser substring).
- ❌ Termos com 1 ou 2 caracteres não usam o índice (e são rejeitados pela validação da API).

## Decisão

Escolhemos **B: `pg_trgm` com uma expressão normalizada**:

```sql
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS unaccent;

-- unaccent() não é IMMUTABLE; o wrapper permite usá-la em índice de expressão
CREATE FUNCTION f_unaccent(text) RETURNS text
  LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT
  AS $$ SELECT public.unaccent('public.unaccent'::regdictionary, $1) $$;

CREATE INDEX ix_chamados_busca_trgm ON chamados
  USING gin (f_unaccent(lower(titulo || ' ' || descricao)) gin_trgm_ops);

-- consulta (o termo é normalizado da mesma forma e passado como parâmetro)
WHERE f_unaccent(lower(titulo || ' ' || descricao)) LIKE '%' || f_unaccent(lower(@termo)) || '%'
```

Os caracteres curinga do termo (`%`, `_`) são escapados na aplicação.

## Trade-offs aceitos

- A busca não ordena por relevância. Busca "inteligente" por significado fica a cargo do copiloto e dos embeddings.
- O índice de trigramas ocupa mais espaço que um `tsvector`. É irrelevante nesse volume.

## Consequências

- A API exige no mínimo 3 caracteres no parâmetro `q` e responde **400** se receber menos.
- Há um teste de integração que verifica que a busca funciona sem acento (`configuracao` encontra `configuração`) e por código (`ERR-5`).
- Há um teste que confere, com `EXPLAIN`, que a consulta usa o `ix_chamados_busca_trgm`.
- **Gatilho de reavaliação:** pedidos de ordenação por relevância. Nesse caso, adicionar um `tsvector` gerado e combinar os dois (FTS para ranking, trigram para substring), ou busca híbrida com embeddings.
