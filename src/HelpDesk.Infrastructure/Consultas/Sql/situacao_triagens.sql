-- Dashboard 4) Situação da fila e das falhas de triagem (contrato: ia.pendentes e ia.falhas).
-- Uma varredura com FILTER; pendentes é o tamanho da fila agora (ADR-0010).
SELECT COUNT(*) FILTER (WHERE status = 'pendente')::int AS pendentes,
       COUNT(*) FILTER (WHERE status = 'falhou')::int   AS falhas
FROM triagens_ia;
