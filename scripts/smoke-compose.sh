#!/usr/bin/env bash
# Smoke test do ambiente completo (ADR-0022): verifica os critérios de aceite das Sprints 0 a 6 contra o
# docker compose já em execução. Usado pelo CI e executável localmente:
#   docker compose up --build -d --wait && bash scripts/smoke-compose.sh
set -euo pipefail

API="http://localhost:${API_PORTA_HOST:-5080}"
WEB="http://localhost:${WEB_PORTA_HOST:-8080}"
falhas=0
SESSAO=""

ok() { echo "✅ $1"; }
falha() { echo "❌ $1"; falhas=$((falhas + 1)); }
verificar() { local descricao="$1"; shift; if "$@"; then ok "$descricao"; else falha "$descricao"; fi; }

status_http() { curl -s -o /dev/null -w '%{http_code}' --max-time 10 "$1"; }

# Sem jq de propósito: o script roda no runner do CI e também no Git Bash do Windows.
saude_e() { curl -s --max-time 10 "$API/health" | grep -q "^{\"status\":\"$1\""; }

migrator_terminou_com_zero() {
  [ "$(docker inspect "$(docker compose ps -aq migrator)" --format '{{.State.ExitCode}}')" = "0" ]
}

login_com_cookie_httponly() {
  # ADR-0026: login com cookie httpOnly e SameSite=Strict; o token não vem no corpo.
  # Preenche SESSAO com o token do cookie para as chamadas seguintes da API via curl.
  local cabecalhos corpo cookie token
  cabecalhos="$(mktemp)"
  corpo="$(curl -fsS --max-time 10 -D "$cabecalhos" -H 'Content-Type: application/json' \
    -d '{"email":"ana.suporte@example.com","senha":"HelpDesk@2026"}' "$WEB/api/auth/login")" || return 1
  echo "$corpo" | grep -q '"perfil":"Atendente"' || return 1
  cookie="$(grep -i '^set-cookie: helpdesk_sessao=' "$cabecalhos" || true)"
  [ -n "$cookie" ] || return 1
  echo "$cookie" | grep -qi 'httponly' || return 1
  echo "$cookie" | grep -qi 'samesite=strict' || return 1
  token="$(echo "$cookie" | sed -e 's/.*helpdesk_sessao=\([^;]*\).*/\1/' | tr -d '\r\n')"
  [ -n "$token" ] || return 1
  SESSAO="Authorization: Bearer $token"
}

sessao_da_ana() {
  # ADR-0026: quem está na sessão; exige autenticação.
  local corpo
  corpo="$(curl -fsS --max-time 10 ${SESSAO:+-H "$SESSAO"} "$WEB/api/auth/eu")" || return 1
  echo "$corpo" | grep -q '"email":"ana.suporte@example.com"' && echo "$corpo" | grep -q '"perfil":"Atendente"'
}

categorias_pelo_nginx() {
  local cabecalhos corpo
  cabecalhos="$(mktemp)"
  corpo="$(curl -fsS --max-time 10 -D "$cabecalhos" ${SESSAO:+-H "$SESSAO"} "$WEB/api/categorias")"
  [ "$(echo "$corpo" | grep -o '"id":' | wc -l)" -eq 5 ] && grep -qi '^x-correlation-id:' "$cabecalhos"
}

shell_do_frontend() { curl -fsS --max-time 10 "$WEB/" | grep -q '<title>HelpDesk Inteligente</title>'; }

logs_da_api_em_json() {
  # Toda linha do log da API deve ser JSON (ADR-0016), e a requisição deve trazer o CorrelationId.
  # ID único por execução: um log de execução anterior não pode fazer esta verificação passar.
  local id logs
  id="smoke-$(date +%s)-$RANDOM"
  curl -s -o /dev/null ${SESSAO:+-H "$SESSAO"} -H "X-Correlation-Id: $id" "$API/api/categorias"
  sleep 1
  logs="$(docker compose logs api --no-log-prefix --no-color)"
  ! echo "$logs" | grep -v '^{' | grep -q . && echo "$logs" | grep -q "\"CorrelationId\":\"$id\""
}

# Primeiro valor de um campo no JSON (suficiente para respostas pequenas e conhecidas).
campo_json() { grep -o "\"$1\":[^,}]*" | head -1 | cut -d: -f2- | tr -d '"'; }

chamados_do_seed() {
  local total
  total="$(curl -fsS --max-time 10 ${SESSAO:+-H "$SESSAO"} "$WEB/api/chamados?tamanhoPagina=1" | campo_json totalItens)"
  [ "${total:-0}" -ge 200 ]
}

busca_sem_acento() {
  # O seed tem "configuração" em vários textos; a busca vai sem acento (ADR-0008).
  local total
  total="$(curl -fsS --max-time 10 ${SESSAO:+-H "$SESSAO"} "$WEB/api/chamados?q=configuracao" | campo_json totalItens)"
  [ "${total:-0}" -ge 1 ]
}

patch_status() {
  curl -s -o /dev/null -w '%{http_code}' --max-time 10 -X PATCH ${SESSAO:+-H "$SESSAO"} \
    -H 'Content-Type: application/json' -H "If-Match: $3" \
    -d "{\"status\":\"$2\",\"alteradoPor\":\"Smoke (suporte)\"}" "$WEB/api/chamados/$1/status"
}

EMAIL_SMOKE="smoke.$(date +%s).$RANDOM@example.com"

ciclo_do_chamado() {
  # Cria (201) → muda status com o ETag atual (200) → repete com o ETag antigo (412 versao_desatualizada).
  # --compressed pede gzip, como o navegador: se o Nginx comprimir a resposta da API, o ETag vira fraco (W/"...")
  # e o If-Match deixa de casar. Sem isso, o smoke não via o 412 que todo navegador recebia.
  local cabecalhos corpo id etag
  cabecalhos="$(mktemp)"
  corpo="$(curl -fsS --compressed --max-time 10 -D "$cabecalhos" ${SESSAO:+-H "$SESSAO"} -H 'Content-Type: application/json' \
    -d "{\"titulo\":\"Smoke test do compose\",\"descricao\":\"Chamado criado pelo smoke test. CPF 123.456.789-09.\",\"solicitanteNome\":\"Pessoa Smoke\",\"solicitanteEmail\":\"$EMAIL_SMOKE\"}" \
    "$WEB/api/chamados")" || return 1
  grep -qi '^HTTP/[0-9.]* 201' "$cabecalhos" || return 1
  id="$(echo "$corpo" | campo_json id)"
  [ -n "$id" ] || return 1
  # O ETag vem do detalhe (200), como na tela: o Nginx só comprime respostas 200, não o 201 da criação.
  curl -fsS --compressed --max-time 10 -D "$cabecalhos" ${SESSAO:+-H "$SESSAO"} -o /dev/null "$WEB/api/chamados/$id" || return 1
  etag="$(grep -i '^etag:' "$cabecalhos" | cut -d' ' -f2- | tr -d '\r')"
  [ -n "$etag" ] || return 1
  [ "$(patch_status "$id" EmAndamento "$etag")" = "200" ] || return 1
  [ "$(patch_status "$id" Resolvido "$etag")" = "412" ]
}

config_ia() {
  local corpo
  corpo="$(curl -fsS --max-time 10 ${SESSAO:+-H "$SESSAO"} "$WEB/api/config/ia")"
  echo "$corpo" | grep -q '"triagem":true' && echo "$corpo" | grep -q '"copiloto":true'
}

saude_com_fila_de_triagem() {
  curl -s --max-time 10 "$API/health" | grep -q '"filaTriagem":{"status":"'
}

ID_TRIAGEM=""

texto_acentuado_na_triagem() {
  # ADR-0025: as imagens .NET rodam com ICU. Em globalização invariante (o padrão das imagens Alpine), a remoção de
  # acentos não funciona: o fake não reconhece "Não consigo" (prioridade Alta), e o mascarador de nomes e o
  # validador da saída da IA falham do mesmo jeito. O corpo vai pelo stdin para o UTF-8 chegar intacto.
  local corpo id
  corpo="$(printf '%s' "{\"titulo\":\"Não consigo acessar o relatório\",\"descricao\":\"Desde ontem não consigo abrir o relatório mensal.\",\"solicitanteNome\":\"Pessoa Smoke\",\"solicitanteEmail\":\"$EMAIL_SMOKE\"}" |
    curl -fsS --max-time 10 ${SESSAO:+-H "$SESSAO"} -H 'Content-Type: application/json' --data-binary @- "$WEB/api/chamados")" || return 1
  id="$(echo "$corpo" | campo_json id)"
  [ -n "$id" ] || return 1
  for _ in $(seq 1 30); do
    corpo="$(curl -fsS --max-time 10 ${SESSAO:+-H "$SESSAO"} "$WEB/api/chamados/$id")"
    if echo "$corpo" | grep -q '"triagem":{"id":"[^"]*","status":"Concluida"'; then
      echo "$corpo" | grep -q '"prioridadeSugerida":"Alta"'
      return
    fi
    sleep 1
  done
  return 1
}

triagem_concluida_pelo_worker() {
  # Critério da Sprint 2: com o fake, a triagem fica Concluida em segundos (o Worker consome a fila).
  local corpo
  corpo="$(curl -fsS --max-time 10 ${SESSAO:+-H "$SESSAO"} -H 'Content-Type: application/json' \
    -d "{\"titulo\":\"Nao consigo emitir o boleto\",\"descricao\":\"O boleto do mes nao sai. Meu CPF e 123.456.789-09 e o telefone (11) 98765-4321.\",\"solicitanteNome\":\"Pessoa Smoke\",\"solicitanteEmail\":\"$EMAIL_SMOKE\"}" \
    "$WEB/api/chamados")" || return 1
  echo "$corpo" | grep -q '"triagem":{"id":"[^"]*","status":"Pendente"' || return 1
  ID_TRIAGEM="$(echo "$corpo" | campo_json id)"
  for _ in $(seq 1 30); do
    curl -fsS --max-time 10 ${SESSAO:+-H "$SESSAO"} "$WEB/api/chamados/$ID_TRIAGEM" \
      | grep -q '"triagem":{"id":"[^"]*","status":"Concluida"' && return 0
    sleep 1
  done
  return 1
}

aceitar_triagem() {
  [ -n "$ID_TRIAGEM" ] || return 1
  curl -fsS --max-time 10 ${SESSAO:+-H "$SESSAO"} -H 'Content-Type: application/json' -d '{"decididaPor":"Smoke (suporte)"}' \
    "$WEB/api/chamados/$ID_TRIAGEM/triagem/aceitar" | grep -q '"status":"Aceita"'
}

copiloto_via_sse_pelo_nginx() {
  # Critério da Sprint 4: o copiloto responde via SSE pelo Nginx (sem buffering),
  # emitindo eventos de ferramenta, delta, fontes e fim, citando chamados parecidos retornados pelo fake.
  # Pergunta sobre um chamado do seed com semelhantes conhecidos ("erro 403"): o chamado criado pelo smoke tem
  # um título curto que, com o embedding fake, não alcança o limiar de similaridade e não teria o que citar.
  local id cabecalhos corpo
  id="$(curl -fsS --max-time 10 ${SESSAO:+-H "$SESSAO"} "$WEB/api/chamados?q=403&tamanhoPagina=1" | campo_json id)"
  [ -n "$id" ] || return 1
  cabecalhos="$(mktemp)"
  # O corpo vai pela entrada padrão: como argumento, o curl nativo do Windows recodifica o "á" na página de
  # código ANSI, e a API recusa o JSON (400). Pelo stdin, os bytes UTF-8 chegam intactos em qualquer sistema.
  corpo="$(printf '%s' '{"mensagens":[{"papel":"usuario","conteudo":"Já tivemos casos parecidos?"}]}' |
    curl -fsS --max-time 15 -D "$cabecalhos" ${SESSAO:+-H "$SESSAO"} -H 'Content-Type: application/json' --data-binary @- \
      "$WEB/api/chamados/$id/copiloto")" || return 1
  grep -qi 'content-type: text/event-stream' "$cabecalhos" || return 1
  echo "$corpo" | grep -q 'event: ferramenta' || return 1
  echo "$corpo" | grep -q 'event: delta' || return 1
  echo "$corpo" | grep -q 'event: fim' || return 1
  # A resposta cita chamados, e as citações foram verificadas: o evento fontes traz pelo menos um chamado.
  echo "$corpo" | grep -Eq '#[0-9]+' || return 1
  echo "$corpo" | grep -A1 'event: fontes' | grep -q '"numero":[0-9]'
}

dados_pessoais_fora_dos_logs() {
  # DoD: o e-mail, o nome, o CPF e o telefone dos chamados criados acima não podem aparecer em nenhuma linha de
  # log da API nem do Worker (que é quem processa o texto para a IA).
  local logs
  logs="$(docker compose logs api worker --no-log-prefix --no-color)"
  ! echo "$logs" | grep -q -e "$EMAIL_SMOKE" -e "Pessoa Smoke" -e "123.456.789-09" -e "98765-4321"
}

dashboard_bate_com_a_listagem() {
  # Critério da Sprint 3: o dashboard agrega no banco; o total dele é o mesmo da listagem, e a IA tem decisões
  # (o seed traz triagens aceitas e rejeitadas).
  local resumo total_lista
  resumo="$(curl -fsS --max-time 10 ${SESSAO:+-H "$SESSAO"} "$WEB/api/dashboard/resumo")" || return 1
  total_lista="$(curl -fsS --max-time 10 ${SESSAO:+-H "$SESSAO"} "$WEB/api/chamados?tamanhoPagina=1" | campo_json totalItens)"
  [ "$(echo "$resumo" | campo_json totalChamados)" = "$total_lista" ] \
    && echo "$resumo" | grep -q '"porStatus":\[{"status":"Aberto"' \
    && ! echo "$resumo" | grep -q '"taxaAceitacao":null'
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
verificar "POST /api/auth/login → sessão com cookie httpOnly" login_com_cookie_httponly
verificar "GET /api/auth/eu com sessão → Ana (Atendente)" sessao_da_ana
verificar "GET /api/categorias pelo Nginx → 5 categorias + X-Correlation-Id" categorias_pelo_nginx
verificar "Swagger UI em /swagger → 200" test "$(status_http "$API/swagger/index.html")" = "200"
verificar "GET /api/chamados pelo Nginx → seed com 200+ chamados" chamados_do_seed
verificar "busca 'configuracao' (sem acento) encontra chamados" busca_sem_acento
verificar "criar (201) → mudar status com If-Match (200) → ETag antigo (412)" ciclo_do_chamado
verificar "GET /api/config/ia → triagem e copiloto ativos" config_ia
verificar "GET /health traz o check filaTriagem" saude_com_fila_de_triagem
verificar "criar chamado → Worker conclui a triagem (fake) em até 30 s" triagem_concluida_pelo_worker
verificar "aceitar a triagem pelo Nginx → Aceita" aceitar_triagem
verificar "texto acentuado: o Worker remove acentos (ICU na imagem, ADR-0025)" texto_acentuado_na_triagem
verificar "resolvidos e artigos do seed indexados no RAG (sem ação manual)" indice_rag_completo
# Depois do índice completo: as buscas do copiloto dependem dele (sem isso, a ordem dependeria da velocidade da máquina).
verificar "POST /copiloto via SSE pelo Nginx → stream com ferramenta, delta, fim e fontes verificadas" copiloto_via_sse_pelo_nginx
verificar "GET /api/dashboard/resumo pelo Nginx → total igual ao da listagem" dashboard_bate_com_a_listagem
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
