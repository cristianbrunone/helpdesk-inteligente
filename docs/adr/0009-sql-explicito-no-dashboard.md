# ADR-0009 — Leituras analíticas em SQL explícito; escritas com EF Core

- **Status:** Aceita
- **Data:** 2026-09-30
- **Fase:** 3 — Modelagem de dados
- **Requisitos relacionados:** RF-40..43, NFR-02, D6

## Contexto

O enunciado exige que o dashboard seja resolvido com **agregações no banco** (`GROUP BY`, `AVG`...), sem carregar registros em memória, e avalia explicitamente a **"qualidade das consultas SQL"**. As agregações envolvem funções específicas do PostgreSQL: `COUNT(*) FILTER (WHERE ...)`, `EXTRACT(EPOCH FROM intervalo)` e `NULLIF` para evitar divisão por zero. Já as escritas (chamado, status, comentários, triagem) têm regras de domínio e se beneficiam de um ORM (change tracking, concorrência otimista, migrations).

## Alternativas consideradas

### A) Tudo em EF Core via LINQ (`GroupBy` + `Select`)
- ✅ Uma única tecnologia de acesso a dados, com tipagem e refatoração seguras.
- ✅ O EF Core traduz `GroupBy` com agregados para SQL.
- ❌ O SQL gerado fica **invisível** para o avaliador, e é justamente o que está sendo avaliado.
- ❌ `FILTER (WHERE ...)`, os intervalos em horas e as expressões condicionais geram LINQ verboso ou traduções subótimas. Há risco de avaliação no cliente se a tradução falhar.
- ❌ É mais difícil ajustar a consulta olhando o `EXPLAIN ANALYZE`.

### B) SQL explícito para o lado de leitura analítico, EF Core para as escritas
As consultas do dashboard ficam em arquivos `.sql` versionados, executados com `Database.SqlQuery<T>()` do próprio EF Core (sem adicionar Dapper).
- ✅ **O SQL é o artefato:** fica legível, revisável e comentado, e pode ser testado com `EXPLAIN`.
- ✅ Usa os recursos idiomáticos do PostgreSQL sem malabarismo de LINQ.
- ✅ Não traz uma dependência nova: `SqlQuery<T>` mapeia para records.
- ✅ Encaixa-se no "CQRS leve" do ADR-0002 (a leitura pula o domínio).
- ❌ Duas formas de acessar dados no código.
- ❌ Renomear uma coluna exige atualizar os `.sql` (mitigado por testes de integração que executam cada consulta).

## Decisão

Escolhemos **B**. A regra é:

- **Escritas e leituras de detalhe:** EF Core.
- **Listagem:** EF Core com projeção (`Select` direto para DTO, `AsNoTracking`). Os filtros são dinâmicos e a tipagem ajuda.
- **Dashboard e métricas de IA:** SQL explícito em `Infrastructure/Consultas/*.sql`.

Exemplo (tempo médio de resolução por categoria):

```sql
SELECT c.id                AS categoria_id,
       c.nome              AS categoria,
       COUNT(ch.id)        AS resolvidos,
       ROUND(AVG(EXTRACT(EPOCH FROM (ch.resolvido_em - ch.criado_em)) / 3600.0)::numeric, 1)
                           AS tempo_medio_horas
FROM categorias c
LEFT JOIN chamados ch
       ON ch.categoria_id = c.id
      AND ch.resolvido_em IS NOT NULL          -- RN-13: Resolvido ou Fechado
GROUP BY c.id, c.nome
ORDER BY c.nome;
```

## Trade-offs aceitos

- Parte do acesso a dados fica fora do alcance do compilador. A compensação é teste de integração obrigatório para cada `.sql`.

## Consequências

- Cada consulta do dashboard tem um teste de integração com dados controlados, que valida os números esperados.
- O README mostra as consultas e o raciocínio (atende "qualidade das consultas SQL").
- **Gatilho de reavaliação:** se as consultas analíticas crescerem muito, considerar views materializadas ou um schema de leitura.
