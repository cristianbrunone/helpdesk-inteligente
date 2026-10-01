-- Dashboard 3) Aceitas x rejeitadas por categoria sugerida e taxa de aceitação (modelo §6, RN-12, RF-43).
-- ROLLUP acrescenta a linha do total geral (categoria NULL). NULLIF evita a divisão por zero: sem decisão,
-- a taxa é nula, e não 0.
SELECT c.nome AS categoria,
       COUNT(*) FILTER (WHERE t.status = 'aceita')::int    AS aceitas,
       COUNT(*) FILTER (WHERE t.status = 'rejeitada')::int AS rejeitadas,
       ROUND(COUNT(*) FILTER (WHERE t.status = 'aceita')::numeric
             / NULLIF(COUNT(*) FILTER (WHERE t.status IN ('aceita', 'rejeitada')), 0), 3)
         AS taxa_aceitacao
FROM triagens_ia t
JOIN categorias c ON c.id = t.categoria_sugerida_id
WHERE t.status IN ('aceita', 'rejeitada')
GROUP BY ROLLUP (c.nome)
ORDER BY c.nome NULLS LAST;
