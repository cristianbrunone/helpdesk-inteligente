-- Copiloto: obter_metricas_da_categoria (contrato §copiloto). As mesmas regras do dashboard, para uma categoria:
-- tempo médio só com resolvido_em preenchido (RN-13; o AVG ignora os nulos) e aceitação só com decisões (RN-12),
-- pela categoria sugerida pela IA. O filtro de chamados usa o índice #4 do modelo §5; o de triagens varre a
-- tabela, como a consulta de aceitação do dashboard (uma pergunta por vez ao copiloto não justifica outro índice).
WITH chamados_da_categoria AS (
    SELECT COUNT(*)::int                                     AS total_chamados,
           COUNT(*) FILTER (WHERE resolvido_em IS NOT NULL)::int AS resolvidos,
           AVG(EXTRACT(EPOCH FROM (resolvido_em - criado_em)) / 3600.0)::float8 AS tempo_medio_horas
    FROM chamados
    WHERE categoria_id = @categoria_id
), decisoes AS (
    SELECT COUNT(*) FILTER (WHERE status = 'aceita')::int    AS aceitas,
           COUNT(*) FILTER (WHERE status = 'rejeitada')::int AS rejeitadas
    FROM triagens_ia
    WHERE categoria_sugerida_id = @categoria_id
      AND status IN ('aceita', 'rejeitada')
)
SELECT total_chamados, resolvidos, tempo_medio_horas, aceitas, rejeitadas
FROM chamados_da_categoria, decisoes;
