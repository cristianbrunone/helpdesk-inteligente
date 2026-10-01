#!/usr/bin/env bash
# Smoke test do ambiente completo (ADR-0022): verifica os critérios de aceite das Sprints 0 a 3 contra o
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

# Primeiro valor de um campo no JSON (suficiente para respostas pequenas e conhecidas).
campo_json() { grep -o "\"$1\":[^,}]*" | head -1 | cut -d: -f2- | tr -d '"'; }

chamados_do_seed() {
  local total
  total="$(curl -fsS --max-time 10 "$WEB/api/chamados?tamanhoPagina=1" | campo_json totalItens)"
  [ "${total:-0}" -ge 200 ]
}

busca_sem_acento() {
  # O seed tem "configuração" em vários textos; a busca vai sem acento (ADR-0008).
  local total
  total="$(curl -fsS --max-time 10 "$WEB/api/chamados?q=configuracao" | campo_json totalItens)"
  [ "${total:-0}" -ge 1 ]
}

patch_status() {
  curl -s -o /dev/null -w '%{http_code}' --max-time 10 -X PATCH \
    -H 'Content-Type: application/json' -H "If-Match: $3" \
    -d "{\"status\":\"$2\",\"alteradoPor\":\"Smoke (suporte)\"}" "$WEB/api/chamados/$1/status"
}

EMAIL_SMOKE="smoke.$(date +%s).$RANDOM@example.com"

ciclo_do_chamado() {
  # Cria (201) → muda status com o ETag atual (200) → repete com o ETag antigo (412 versao_desatualizada).
  local cabecalhos corpo id etag
  cabecalhos="$(mktemp)"
  corpo="$(curl -fsS --max-time 10 -D "$cabecalhos" -H 'Content-Type: application/json' \
    -d "{\"titulo\":\"Smoke test do compose\",\"descricao\":\"Chamado criado pelo smoke test. CPF 123.456.789-09.\",\"solicitanteNome\":\"Pessoa Smoke\",\"solicitanteEmail\":\"$EMAIL_SMOKE\"}" \
    "$WEB/api/chamados")" || return 1
  grep -qi '^HTTP/[0-9.]* 201' "$cabecalhos" || return 1
  id="$(echo "$corpo" | campo_json id)"
  etag="$(grep -i '^etag:' "$cabecalhos" | cut -d' ' -f2- | tr -d '\r')"
  [ -n "$id" ] && [ -n "$etag" ] || return 1
  [ "$(patch_status "$id" EmAndamento "$etag")" = "200" ] || return 1
  [ "$(patch_status "$id" Resolvido "$etag")" = "412" ]
}

config_ia() {
  curl -fsS --max-time 10 "$WEB/api/config/ia" | grep -q '"triagem":true'
}

saude_com_fila_de_triagem() {
  curl -s --max-time 10 "$API/health" | grep -q '"filaTriagem":{"status":"'
}

ID_TRIAGEM=""

triagem_concluida_pelo_worker() {
  # Critério da Sprint 2: com o fake, a triagem fica Concluida em segundos (o Worker consome a fila).
  local corpo
  corpo="$(curl -fsS --max-time 10 -H 'Content-Type: application/json' \
    -d "{\"titulo\":\"Nao consigo emitir o boleto\",\"descricao\":\"O boleto do mes nao sai. Meu CPF e 123.456.789-09 e o telefone (11) 98765-4321.\",\"solicitanteNome\":\"Pessoa Smoke\",\"solicitanteEmail\":\"$EMAIL_SMOKE\"}" \
    "$WEB/api/chamados")" || return 1
  echo "$corpo" | grep -q '"triagem":{"id":"[^"]*","status":"Pendente"' || return 1
  ID_TRIAGEM="$(echo "$corpo" | campo_json id)"
  for _ in $(seq 1 30); do
    curl -fsS --max-time 10 "$WEB/api/chamados/$ID_TRIAGEM" \
      | grep -q '"triagem":{"id":"[^"]*","status":"Concluida"' && return 0
    sleep 1
  done
  return 1
}

aceitar_triagem() {
  [ -n "$ID_TRIAGEM" ] || return 1
  curl -fsS --max-time 10 -H 'Content-Type: application/json' -d '{"decididaPor":"Smoke (suporte)"}' \
    "$WEB/api/chamados/$ID_TRIAGEM/triagem/aceitar" | grep -q '"status":"Aceita"'
}

dados_pessoais_fora_dos_logs() {
  # DoD: o e-mail, o nome, o CPF e o telefone dos chamados criados acima não podem aparecer em nenhuma linha de
  # log da API nem do Worker (que é quem processa o texto para a IA).
  local logs
  logs="$(docker compose logs api worker --no-log-prefix --no-color)"
  ! echo "$logs" | grep -q -e "$EMAIL_SMOKE" -e "Pessoa Smoke" -e "123.456.789-09" -e "98765-4321"
}

# Consulta no banco do compose (dentro do contêiner, com as variáveis dele): -t sem cabeçalho, -A sem alinhamento.
sql() { docker compose exec -T db sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tAc "$1"' _ "$1"; }

indice_rag_completo() {
  # Critério da Sprint 3: após a subida, todos os resolvidos e artigos do seed estão indexados, sem ação manual.
  # O reconciliador roda logo na subida; com o fake, leva poucos segundos. Espera até ~60 s.
  local faltando
  for _ in $(seq 1 30); do
    faltando="$(sql "SELECT (SELECT count(*) FROM chamados c WHERE c.status IN ('resolvido', 'fechado')
                       AND NOT EXISTS (SELECT 1 FROM documentos_rag d
                                       WHERE d.chamado_id = c.id AND d.embedding IS NOT NULL))
                    + (SELECT count(*) FROM artigos_conhecimento a WHERE a.ativo
                       AND NOT EXISTS (SELECT 1 FROM documentos_rag d
                                       WHERE d.artigo_id = a.id AND d.embedding IS NOT NULL))
                    + (SELECT count(*) FROM documentos_rag WHERE embedding IS NULL)")"
    [ "$faltando" = "0" ] && [ "$(sql "SELECT count(*) > 0 FROM documentos_rag")" = "t" ] && return 0
    sleep 2
  done
  return 1
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
verificar "GET /api/chamados pelo Nginx → seed com 200+ chamados" chamados_do_seed
verificar "busca 'configuracao' (sem acento) encontra chamados" busca_sem_acento
verificar "criar (201) → mudar status com If-Match (200) → ETag antigo (412)" ciclo_do_chamado
verificar "GET /api/config/ia → triagem ativa" config_ia
verificar "GET /health traz o check filaTriagem" saude_com_fila_de_triagem
verificar "criar chamado → Worker conclui a triagem (fake) em até 30 s" triagem_concluida_pelo_worker
verificar "aceitar a triagem pelo Nginx → Aceita" aceitar_triagem
verificar "resolvidos e artigos do seed indexados no RAG (sem ação manual)" indice_rag_completo
verificar "logs da API em JSON, com CorrelationId" logs_da_api_em_json
verificar "nome, e-mail, CPF e telefone fora dos logs da API e do Worker" dados_pessoais_fora_dos_logs

docker compose stop db > /dev/null 2>&1
verificar "banco parado → GET /health → 503" health_responde 503
docker compose start db > /dev/null 2>&1
verificar "banco de volta → GET /health → 200" health_responde 200

if [ "$falhas" -gt 0 ]; then
  echo "== $falhas verificação(ões) falharam"
  exit 1
fi
echo "== Todas as verificações passaram"
