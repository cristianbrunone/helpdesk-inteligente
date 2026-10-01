#!/usr/bin/env bash
# Smoke test do ambiente completo (ADR-0022): verifica os critérios de aceite da Sprint 0 contra o
# docker compose já em execução. Usado pelo CI e executável localmente:
#   docker compose up --build -d --wait && bash scripts/smoke-compose.sh
set -euo pipefail

API="http://localhost:${API_PORTA_HOST:-5080}"
WEB="http://localhost:${WEB_PORTA_HOST:-8080}"
falhas=0

ok() { echo "✅ $1"; }
falha() { echo "❌ $1"; falhas=$((falhas + 1)); }
verificar() { local descricao="$1"; shift; if "$@"; then ok "$descricao"; else falha "$descricao"; fi; }

status_http() { curl -s -o /dev/null -w '%{http_code}' --max-time 10 "$1"; }

# Sem jq de propósito: o script roda no runner do CI e também no Git Bash do Windows.
saude_e() { curl -s --max-time 10 "$API/health" | grep -q "^{\"status\":\"$1\""; }

migrator_terminou_com_zero() {
  [ "$(docker inspect "$(docker compose ps -aq migrator)" --format '{{.State.ExitCode}}')" = "0" ]
}

categorias_pelo_nginx() {
  local cabecalhos corpo
  cabecalhos="$(mktemp)"
  corpo="$(curl -fsS --max-time 10 -D "$cabecalhos" "$WEB/api/categorias")"
  [ "$(echo "$corpo" | grep -o '"id":' | wc -l)" -eq 5 ] && grep -qi '^x-correlation-id:' "$cabecalhos"
}

shell_do_frontend() { curl -fsS --max-time 10 "$WEB/" | grep -q '<title>HelpDesk Inteligente</title>'; }

logs_da_api_em_json() {
  # Toda linha do log da API deve ser JSON (ADR-0016), e a requisição deve trazer o CorrelationId.
  # ID único por execução: um log de execução anterior não pode fazer esta verificação passar.
  local id logs
  id="smoke-$(date +%s)-$RANDOM"
  curl -s -o /dev/null -H "X-Correlation-Id: $id" "$API/api/categorias"
  sleep 1
  logs="$(docker compose logs api --no-log-prefix --no-color)"
  ! echo "$logs" | grep -v '^{' | grep -q . && echo "$logs" | grep -q "\"CorrelationId\":\"$id\""
}

health_responde() {
  # Espera até ~20 s o /health devolver o status HTTP esperado (o banco leva alguns segundos para voltar).
  local esperado="$1"
  for _ in $(seq 1 20); do
    [ "$(status_http "$API/health")" = "$esperado" ] && return 0
    sleep 1
  done
  return 1
}

echo "== Smoke test do docker compose (API: $API | Web: $WEB)"
verificar "migrator terminou com código 0" migrator_terminou_com_zero
verificar "GET /health → Healthy" saude_e Healthy
verificar "frontend servido pelo Nginx (<title>)" shell_do_frontend
verificar "GET /api/categorias pelo Nginx → 5 categorias + X-Correlation-Id" categorias_pelo_nginx
verificar "Swagger UI em /swagger → 200" test "$(status_http "$API/swagger/index.html")" = "200"
verificar "logs da API em JSON, com CorrelationId" logs_da_api_em_json

docker compose stop db > /dev/null 2>&1
verificar "banco parado → GET /health → 503" health_responde 503
docker compose start db > /dev/null 2>&1
verificar "banco de volta → GET /health → 200" health_responde 200

if [ "$falhas" -gt 0 ]; then
  echo "== $falhas verificação(ões) falharam"
  exit 1
fi
echo "== Todas as verificações passaram"
