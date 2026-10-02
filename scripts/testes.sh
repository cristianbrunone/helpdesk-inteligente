#!/usr/bin/env bash
# Comando único para todos os testes (critério da Sprint 5).
#
#   bash scripts/testes.sh              backend (unitários, integração e arquitetura) + frontend (lint, Vitest, build)
#   bash scripts/testes.sh --completo   o mesmo + um docker compose isolado: smoke dos critérios de aceite e E2E
#
# Pré-requisitos: .NET 10 SDK, Node 24 e Docker (os testes de integração usam Testcontainers).
#
# O modo --completo sobe um ambiente à parte (projeto "helpdesk-testes", portas 8089/5081/55433), sempre com a IA
# fake, e o derruba no fim: não toca no ambiente de desenvolvimento nem usa a chave de API do .env. Para o E2E numa
# máquina que não baixa o Chromium do Playwright (inspeção TLS), defina E2E_NAVEGADOR=msedge.
set -euo pipefail

raiz="$(cd "$(dirname "$0")/.." && pwd)"
completo=false
case "${1:-}" in
  "") ;;
  --completo) completo=true ;;
  *) echo "Uso: bash scripts/testes.sh [--completo]" >&2; exit 2 ;;
esac

etapa() { printf '\n\033[1m== %s\033[0m\n' "$1"; }

etapa "Backend: unitários, integração (Testcontainers) e arquitetura"
(cd "$raiz" && dotnet test --filter "Category!=ProvedorReal")

etapa "Frontend: lint (ESLint + Prettier), testes (Vitest) e build"
(
  cd "$raiz/web"
  [ -d node_modules ] || npm ci --no-audit --no-fund
  npm run lint
  npm test
  npm run build
)

if $completo; then
  export COMPOSE_PROJECT_NAME="${COMPOSE_PROJECT_NAME:-helpdesk-testes}"
  export WEB_PORTA_HOST="${WEB_PORTA_HOST:-8089}" API_PORTA_HOST="${API_PORTA_HOST:-5081}" DB_PORTA_HOST="${DB_PORTA_HOST:-55433}"
  # Variáveis do ambiente valem mais que o .env: o ambiente de teste é sempre o fake, com a IA ligada.
  export LLM_PROVIDER=fake LLM_FAKE_MODO=normal IA_TRIAGEM_HABILITADA=true IA_COPILOTO_HABILITADO=true

  derrubar() { (cd "$raiz" && docker compose down -v --remove-orphans >/dev/null 2>&1) || true; }
  trap derrubar EXIT

  etapa "Ambiente isolado: docker compose up ($COMPOSE_PROJECT_NAME, web em http://localhost:$WEB_PORTA_HOST)"
  (cd "$raiz" && docker compose up --build --detach --wait --wait-timeout 300)

  etapa "Smoke: critérios de aceite contra o ambiente de pé"
  (cd "$raiz" && bash scripts/smoke-compose.sh)

  etapa "E2E (Playwright): triagem, copiloto e telas em 375 px"
  (
    cd "$raiz/web"
    [ -n "${E2E_NAVEGADOR:-}" ] || npx playwright install chromium
    E2E_BASE_URL="http://localhost:$WEB_PORTA_HOST" npm run e2e
  )
fi

etapa "Todos os testes passaram"
