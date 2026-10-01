-- Dashboard 5) Consumo de IA nos últimos 30 dias (modelo §6, RF-17, NFR-11).
-- O filtro por janela usa o índice #14 (uso_llm (criado_em)). Cada tentativa ao provedor é uma linha, então
-- as falhas transitórias também aparecem no custo e na latência.
SELECT operacao,
       modelo,
       COUNT(*)::int                          AS chamadas,
       COUNT(*) FILTER (WHERE NOT sucesso)::int AS falhas,
       SUM(tokens_entrada)::bigint            AS tokens_entrada,
       SUM(tokens_saida)::bigint              AS tokens_saida,
       PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY latencia_ms) AS latencia_p95_ms
FROM uso_llm
WHERE criado_em >= now() - interval '30 days'
GROUP BY operacao, modelo
ORDER BY operacao, modelo;
