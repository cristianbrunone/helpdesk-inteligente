-- Dashboard 1) Totais por status e por prioridade (modelo §6, ADR-0009).
-- Duas agregações sobre chamados, unidas numa só ida ao banco. O GROUP BY status é atendido pelo índice #2
-- (chamados (status, criado_em)) com index-only scan. Os valores voltam como texto do enum nativo; os que não
-- aparecem (total zero) são completados na aplicação, na ordem de negócio.
SELECT 'status' AS dimensao, status::text AS valor, COUNT(*)::int AS total
FROM chamados
GROUP BY status
UNION ALL
SELECT 'prioridade', prioridade::text, COUNT(*)::int
FROM chamados
GROUP BY prioridade;
