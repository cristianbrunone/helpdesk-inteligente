-- Dashboard 2) Tempo médio de resolução (horas) por categoria (modelo §6, RN-13).
-- LEFT JOIN a partir de categorias: toda categoria aparece, mesmo sem resolvidos (média nula).
-- resolvido_em só existe em Resolvido/Fechado (CHECK do modelo §4), então o filtro já é a RN-13.
SELECT c.id                AS categoria_id,
       c.nome              AS categoria,
       COUNT(ch.id)::int   AS resolvidos,
       ROUND(AVG(EXTRACT(EPOCH FROM (ch.resolvido_em - ch.criado_em)) / 3600.0)::numeric, 1)
                           AS tempo_medio_horas
FROM categorias c
LEFT JOIN chamados ch
       ON ch.categoria_id = c.id
      AND ch.resolvido_em IS NOT NULL
GROUP BY c.id, c.nome
ORDER BY c.nome;
