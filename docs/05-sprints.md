# Planejamento das sprints

> **Fase do checklist:** fechamento da 3 (planejamento), que prepara a 4 (Walking Skeleton = Sprint 0)
> **Base:** [`01-requisitos.md`](01-requisitos.md), [`02-add.md`](02-add.md), [`03-modelo-de-dados.md`](03-modelo-de-dados.md), [`04-contratos-api.md`](04-contratos-api.md)
> **Versão do plano:** 1.1 (30/09). As mudanças estão no [histórico de revisões](#5-histórico-de-revisões-do-plano).

## 1. Estratégia

- **Fatias verticais.** Cada sprint entrega banco + API + frontend + testes de uma funcionalidade, e termina **demonstrável** com `docker compose up`. Assim, se o prazo acabar em qualquer ponto, o que existe funciona de ponta a ponta.
- **Risco primeiro.** A Sprint 0 inclui a PoC com o Gemini real, porque as incertezas dos ADR-0005 e ADR-0011 podem mudar o desenho, e é melhor descobrir isso no dia 2 do que no dia 6.
- **Obrigatório antes de diferencial.** As Sprints 0 a 3 cobrem todo o obrigatório do enunciado. RAG e copiloto entram por cima de uma base que já atende a avaliação.
- **Uma sprint ≈ um bloco de trabalho de 2 a 5 h**, com vários commits pequenos (Conventional Commits). Isso gera naturalmente o "histórico de commits real e incremental" exigido.
- **Documentação viva.** O README nasce no primeiro commit e cresce a cada sprint (uma seção por entrega). Nunca é escrito todo no final.

## 2. Visão geral

| Sprint | Tema | Entrega demonstrável | Prioridade | Estimativa |
|---|---|---|---|---|
| **0** | Walking Skeleton + PoC de IA | Compose sobe os 5 serviços, `/health` verde, CI verde, relatório da PoC | M | 3 h |
| **1** | Chamados de ponta a ponta | Criar, listar (filtros na URL), detalhar, mudar status e comentar | M | 4,5 h |
| **2** | Triagem por IA (sem RAG) | Chamado criado → triagem assíncrona → aceitar/rejeitar/refazer no painel, **com trace por etapa** | M | 4,25 h |
| **3** | RAG + Dashboard + Evals | Triagem com fontes + dashboard com gráficos + **relatório de eval sem RAG × com RAG** | M (dashboard) / S (RAG, evals) | 4,5 h |
| **4** | Copiloto conversacional | Chat com tool calling e streaming no detalhe do chamado, **com guardrail de saída** | S | 3,25 h |
| **5** | Hardening e entrega | README completo, E2E, cobertura, revisão final, padrões (Fase 5) | M (docs) / C (E2E) | 2 h |
| | | | **Total** | **~21,5 h** |

O enunciado estima de 10 a 14 h para o **obrigatório**. As Sprints 0, 1, 2 e a parte de dashboard da 3 somam ~13 h. As ~8,5 h restantes são o investimento consciente nos diferenciais de IA conversacional: RAG, evals, copiloto e os controles de produção vindos da [revisão de 30/09](revisoes/2026-09-30-padroes-agenticos.md).

### Calendário sugerido

Considerando o recebimento em 30/09 e a entrega até 07/10:

| Dia | Data | Trabalho |
|---|---|---|
| D1 | Qua 30/09 | Fases 1–3: requisitos, ADD, ADRs, modelo, contratos e este plano ✅ |
| D2 | Qui 01/10 | Sprint 0 (Fase 4 do checklist) |
| D3 | Sex 02/10 | Sprint 1 |
| D4 | Sáb 03/10 | Sprint 2 |
| D5 | Dom 04/10 | Sprint 3 |
| D6 | Seg 05/10 | Sprint 4 |
| D7 | Ter 06/10 | Sprint 5 + entrega |
| — | Qua 07/10 | Folga para imprevistos |

### Linha de corte (se o prazo apertar)

Os cortes acontecem nesta ordem, do primeiro ao último:

1. A nova tentativa corretiva da triagem (opcional, L5 da revisão): só entra se houver folga.
2. E2E com Playwright (Sprint 5).
3. Streaming do copiloto: o contrato já prevê a degradação para um único `delta` (ADR-0012).
4. O copiloto inteiro (Sprint 4), junto com o guardrail de saída dele. O RAG da triagem continua sendo o diferencial de IA.
5. **Os evals (ADR-0018) só são cortados depois do copiloto.** Para uma vaga de IA, medir a qualidade vale mais que uma funcionalidade a mais. Se o tempo for curto, reduzir para os 15 casos claros + os 5 de segurança, em vez de eliminar.
6. **Nunca se corta:** testes obrigatórios, README e DECISOES.md, e o `docker compose up` funcionando.

---

## 3. Definition of Done (vale para todas as sprints)

- [ ] O `docker compose up` a partir de um clone limpo sobe tudo, **sem chave de API**.
- [ ] O CI está verde (build, lint e todos os testes).
- [ ] Os testes novos cobrem os critérios de aceite da sprint.
- [ ] Nenhum dado pessoal aparece em logs (verificado nos logs da execução).
- [ ] O frontend novo tem estados de carregamento, vazio e erro, e funciona em 375 px de largura.
- [ ] Toda decisão nova relevante tem ADR (duas alternativas) e aparece no `DECISOES.md`.
- [ ] A `JORNADA.md` foi atualizada com o que foi feito e o que foi aprendido.
- [ ] O `README.md` foi atualizado com o que a sprint entregou: quem clonar o repositório ao fim da sprint consegue rodar e entender o que existe.
- [ ] Os commits são pequenos e usam o padrão `tipo(escopo): descrição`.

---

## Sprint 0: Walking Skeleton + PoC de IA

> Corresponde à **Fase 4 do checklist**: pipeline de CI, observabilidade, segurança base e subida real do ambiente.

**Objetivo:** provar que a arquitetura "anda" de ponta a ponta com o mínimo de funcionalidade, e eliminar as incertezas técnicas sobre o provedor de IA.

**Escopo:**

- Estrutura do repositório: `src/` (5 projetos, ADR-0002), `tests/` (unit, integration, architecture), `web/`, `docs/`.
- Migration inicial: extensões (`vector`, `pg_trgm`, `unaccent`), enums, `categorias` + seed das 5 categorias.
- Compose com `db`, `migrator`, `api`, `worker` e `web`, com healthchecks e ordem de subida (ADD §12).
- API: `/health` (com o check do banco), `GET /api/categorias`, OpenAPI + Swagger UI, ProblemDetails global, correlation id, logs estruturados em JSON.
- Worker: um `BackgroundService` "vazio" que registra heartbeat nos logs.
- Web: Vite + React + TypeScript + React Router + TanStack Query. O layout base responsivo carrega as categorias pela camada `api/`.
- CI (GitHub Actions): build .NET, testes (com Testcontainers), lint e build do frontend e testes do frontend.
- Qualidade: `.editorconfig`, `dotnet format`, ESLint e Prettier, e `.env.example`.
- **README:** pré-requisitos, como rodar (`docker compose up`), URLs (app, Swagger, health), como rodar os testes, diagrama de arquitetura (do ADD) e a stack, com as bibliotecas usadas e o porquê de cada uma. O README inicial (nome, descrição e links para `docs/`) já existe desde os commits de fundação.
- **PoC de IA** (teste marcado `Category=ProvedorReal`, que não roda no CI):
  1. saída estruturada com `json_schema` no Gemini, pelo endpoint compatível;
  2. uma rodada de tool calling via `UseFunctionInvocation`;
  3. embeddings com `dimensions = 768`.

  O resultado é registrado no ADR-0005 e no ADR-0011 (status "Aceita (validada)" ou o plano B aplicado).

**Critérios de aceite:**

- [ ] O `docker compose up` sobe os 5 serviços. `http://localhost:8080` mostra o shell com as categorias. `http://localhost:5080/swagger` abre.
- [ ] `GET /health` → `Healthy`. Com o banco parado → `503`.
- [ ] Cada requisição gera um log JSON com `CorrelationId`, e o header volta na resposta.
- [ ] O CI roda em cada push e está verde.
- [ ] O relatório da PoC foi registrado. Se algum item falhou, o plano B foi escolhido e documentado.

**Testes:** teste de arquitetura (regra de dependência entre camadas); teste de integração do `/health` e de `/api/categorias` com Testcontainers.

**ADRs de plataforma previstos (Fase 4):** migrations em serviço one-shot × no startup; biblioteca de logs (Serilog × logging nativo em JSON); estratégia de CI; gestão de segredos (`.env` local × secret store).

---

## Sprint 1: Chamados de ponta a ponta

**Objetivo:** o ciclo completo de um chamado sem IA, com a máquina de estados blindada.

**Requisitos:** RF-01, RF-03..07, RN-01..06, P-02, P-06, P-07, P-09, P-10.

**Escopo:**

- Domínio: `Chamado` com a máquina de estados (`MudarStatus`, `TransicoesPermitidas`, `PodeComentar`) e erros de domínio tipados.
- Migration: `chamados`, `comentarios` e `historico_status`, com todas as constraints e os índices 1 a 7 (modelo §4 e §5).
- Seed: 200 chamados, comentários e histórico coerente (Bogus, semente fixa).
- API: `POST/GET /api/chamados`, `GET /{id}` (com `ETag`), `PATCH /{id}/status` (com `If-Match`) e `POST /{id}/comentarios`, com os códigos de erro do contrato.
- Web:
  - **Lista:** filtros sincronizados com a URL, busca com debounce, paginação, ordenação e badges de status/prioridade.
  - **Novo chamado:** validação no cliente (React Hook Form + Zod) e exibição dos erros 422 da API por campo.
  - **Detalhe:** dados, comentários, histórico e botões **só** das `transicoesPermitidas`.
- **README:** justificativa dos índices (resumo, com link para `03-modelo-de-dados.md`) e as regras de status.

**Critérios de aceite:**

- [ ] Todas as 5 transições permitidas funcionam. Transições proibidas → 409 com `transicoesPermitidas`.
- [ ] Resolver preenche `resolvidoEm`; reabrir limpa. Cada mudança gera histórico na mesma transação.
- [ ] Um chamado Crítico não pode ser cancelado. Chamados Fechados ou Cancelados não aceitam comentário nem mudança de status.
- [ ] Recarregar a lista mantém os filtros. A busca `configuracao` encontra `configuração`.
- [ ] Uma alteração concorrente → 412 e mensagem amigável na UI.

**Testes:**

- **Unitários:** todas as transições permitidas; pelo menos 6 proibidas (Aberto→Resolvido, Aberto→Fechado, EmAndamento→Fechado, Fechado→qualquer, Cancelado→qualquer, mesmo status); Crítico→Cancelado; `resolvidoEm` na resolução e na reabertura.
- **Integração** (Testcontainers): criar (201/422), listar com cada filtro e ordenação, paginação, busca sem acento (e com `EXPLAIN` usando o índice), status (200/409/412) e comentário em chamado finalizado (409).
- **Frontend** (Vitest + Testing Library): validação do formulário; botões de status exibindo só as transições permitidas; filtros refletidos na URL.

---

## Sprint 2: Triagem por IA (sem RAG)

**Objetivo:** cumprir **todo** o requisito obrigatório de IA: assíncrona, resiliente, validada e com LGPD.

**Requisitos:** RF-02, RF-10..14, RF-17, RF-18, RN-07..10, NFR-04..06, NFR-09..11, NFR-17.

**Escopo:**

- `TextoMascarado` + `MascaradorDadosPessoais` (e-mail, telefone BR com e sem DDD/+55, CPF com e sem pontuação), conforme o ADR-0006.
- Migration: `triagens_ia` (colunas de fila, constraints, índices 8 a 10) e `uso_llm`.
- Prompt versionado em `prompts/triagem.v1.md`: papel, categorias válidas injetadas, formato JSON, regras de prioridade, instrução anti-injection e texto do usuário delimitado.
- Pipeline: `Mascarar → MontarPrompt → Completar → Validar`. O validador faz: parse tolerante (remove cercas ```` ```json ````), schema, categoria existente, prioridade válida (normaliza "Média"/"media"), resumo com até 200 caracteres e confiança entre 0 e 1.
- Provedores: `FakeChatClient` (determinístico, por palavras-chave; com modos de falha configuráveis para teste) e o adaptador OpenAI-compatível. Resiliência com timeout, retry com backoff e jitter, e 429/5xx tratados como transitórios.
- Middleware de telemetria de IA → `uso_llm` + log estruturado (sem conteúdo).
- Worker: fila com `SKIP LOCKED`, lease e backoff (ADR-0003, ADR-0010).
- API: refazer (202), aceitar e rejeitar (200/404/409).
- Web: painel da IA com selo "Gerado por IA", estados Pendente (polling com backoff), Concluída, Falhou (mensagem + Refazer) e Aceita/Rejeitada.
- **Tracing** (ADR-0019): OpenTelemetry na API e no Worker; spans do pipeline (`mascarar`, `completar`, `validar`...) e o middleware `UseOpenTelemetry()` no `IChatClient`; link entre a criação e o processamento via `traceparent` gravado na triagem; serviço `aspire-dashboard` no profile `observabilidade` do Compose. **Nenhum conteúdo nos atributos.**
- **Kill switch e orçamento da triagem** (ADR-0021): `IA_TRIAGEM_HABILITADA`, `TRIAGEM_MAX_TOKENS_SAIDA` e `GET /api/config/ia`. Com a triagem desativada, o chamado nasce sem triagem e a UI mostra "Triagem por IA desativada".
- **README:** como ativar um provedor real (Gemini, OpenAI, Ollama), como o prompt foi construído (estrutura, decisões, versionamento), como os dados pessoais são protegidos, **como ver os traces** e o **procedimento de emergência** (qual flag desligar).

**Critérios de aceite:**

- [ ] Criar um chamado responde em menos de 300 ms, mesmo com o provedor configurado para atrasar 30 s (teste com fake lento).
- [ ] Com o fake, a triagem fica `Concluida` em segundos. Com o fake em modo "JSON inválido", fica `Falhou` com motivo, e a API segue saudável.
- [ ] Nenhum CPF, telefone ou e-mail chega ao provedor (teste com um spy no `IChatClient`). O nome e o e-mail do solicitante nunca são enviados.
- [ ] Aceitar aplica a categoria e a prioridade. Rejeitar não altera o chamado. Refazer com uma triagem pendente → 409.
- [ ] `LLM_PROVIDER=openai-compatible` + chave do Gemini no `.env` → triagem real funcionando.
- [ ] Com o profile `observabilidade`, o trace de uma triagem mostra as etapas do pipeline e as tentativas ao provedor, sem nenhum texto do chamado.
- [ ] `IA_TRIAGEM_HABILITADA=false` → o chamado é criado sem triagem, "Refazer" devolve 503 e o Worker não consome a fila.

**Testes:**

- **Unitários:** mascaramento (mais de 10 casos, positivos e negativos); parsing e validação (resposta válida, JSON inválido, JSON em cerca Markdown, categoria inexistente, prioridade inválida, resumo longo, confiança fora da faixa).
- **Integração:** criação → worker processa → `Concluida`; timeout → retries → `Falhou`; dois workers concorrentes não duplicam; aceitar/rejeitar/refazer com os códigos do contrato; os spans esperados são emitidos (`ActivityListener`) e um CPF injetado **não** aparece em nenhum atributo; comportamento com a triagem desativada.
- **Frontend:** painel nos estados Concluída e Falhou; botões Aceitar e Rejeitar chamam a API.

---

## Sprint 3: RAG + Dashboard + Evals

**Objetivo:** fundamentar a triagem em conhecimento existente, entregar o dashboard obrigatório e **medir** o efeito do RAG.

**Requisitos:** RF-15, RF-16, RF-30, RF-31, RF-40..43, RN-11..13, NFR-16.

**Escopo:**

- Migration: `artigos_conhecimento` e `documentos_rag` (índices 11 a 13), com o seed de ~25 artigos.
- `FakeEmbeddingGenerator` (feature hashing, 768 dimensões, normalizado) e embeddings reais pelo adaptador.
- Reconciliador de indexação no Worker (ADR-0010): indexa resolvidos e artigos, remove reabertos e reindexa quando o modelo muda.
- `IBuscaSemantica` com pgvector (filtro por `embedding_modelo`, top-k e limiar).
- A etapa `Recuperar` entra no pipeline. O prompt passa a ser `triagem.v2`, com o contexto recuperado, e as `fontes` são gravadas.
- Painel da IA: lista de fontes, com link para o chamado semelhante.
- Dashboard: as 4 consultas SQL (modelo §6), o endpoint e a tela com cartões, gráfico por status, gráfico por prioridade, tempo médio por categoria, e aceitas × rejeitadas por categoria.
- **Evals offline** (ADR-0018):
  - conjunto `evals/triagem/casos.jsonl` com ~30 casos: 15 claros, 6 ambíguos, 4 de prioridade, 3 de injeção e 2 de PII, sendo 10 marcados como held-out;
  - harness `tools/HelpDesk.Evals`, que executa o pipeline real N vezes por caso e gera um relatório em `docs/evals/`;
  - smoke do harness com o fake no CI.
  - **Primeira medição:** `triagem.v1` (sem RAG) × `triagem.v2` (com RAG), no Gemini.
- *(Opcional, L5 da revisão)* Uma nova tentativa corretiva quando a validação falhar: reenviar ao modelo o erro de validação, no máximo 1 vez. Só com folga, e com ADR próprio.
- **README:** como funciona o RAG (o que é indexado, quando, e como trocar o modelo de embedding), as consultas do dashboard e o **resultado dos evals** (tabela sem RAG × com RAG e como rodar o harness).

**Critérios de aceite:**

- [ ] Após a subida, todos os resolvidos e artigos do seed estão indexados, sem ação manual.
- [ ] Um chamado sobre "erro 403 em boletos" recupera chamados e artigos financeiros semelhantes (com o fake).
- [ ] Reabrir um chamado o remove do índice. Trocar `LLM_EMBEDDING_MODEL` reindexa tudo.
- [ ] Os números do dashboard batem com consultas de conferência sobre dados controlados.
- [ ] O relatório de evals comparando sem RAG × com RAG está em `docs/evals/`, com acurácia de categoria e de prioridade, pass^k, taxa de JSON válido, aprovação nos casos de segurança, latência p95 e custo por triagem bem-sucedida.
- [ ] O harness roda com o fake no CI (smoke).

**Testes:**

- **Integração:** reconciliação (indexar, remover, reindexar); busca semântica com o fake; cada consulta do dashboard com massa controlada.
- **Unitários:** o cálculo das métricas do harness (acurácia, pass^k, custo por sucesso) com resultados simulados.
- **Frontend:** dashboard nos estados de carregamento, vazio e erro.

---

## Sprint 4: Copiloto conversacional

**Objetivo:** o diferencial de IA conversacional: um agente com ferramentas, transparente e seguro.

**Requisitos:** RF-20..24, P-08, ADR-0004, ADR-0012, ADR-0020, ADR-0021.

**Escopo:**

- As 4 ferramentas somente leitura (contrato §copiloto), com parâmetros validados, resultados mascarados e limitados.
- `ICopilotoLlm` com `UseFunctionInvocation`, até 3 rodadas de ferramentas e prompt de sistema versionado (`prompts/copiloto.v1.md`), que exige citar chamados como `#numero`.
- Endpoint SSE (`ferramenta`, `delta`, `fontes`, `aviso`, `fim`, `erro`), com rate limit, cancelamento propagado e telemetria (spans por rodada de ferramenta).
- **Guardrail de saída** (ADR-0020): `FiltroSaidaCopiloto`, com buffer de retenção no stream, mascaramento de PII na saída e verificação das citações contra os resultados das ferramentas. Uma citação sem fonte gera o evento `aviso`.
- **Kill switch e orçamento do copiloto** (ADR-0021): `IA_COPILOTO_HABILITADO` e `COPILOTO_MAX_TOKENS_SAIDA`. Uma resposta truncada gera `aviso`.
- Fake com uma sequência roteirizada: tool call → resultado → resposta em pedaços, e um modo que "vaza" um CPF e cita um chamado inexistente, para testar o guardrail.
- Web: painel de chat no detalhe (histórico em memória), indicador "Consultando…" por ferramenta, fontes clicáveis, selo "Contém referências não verificadas" e botão de parar. O painel fica oculto quando o copiloto está desativado.
- **README:** o copiloto, as ferramentas disponíveis e os limites de segurança: somente leitura, escopo no chamado atual, rate limit, guardrail de saída e o *lethal trifecta* com a perna de comunicação externa removida.

**Critérios de aceite:**

- [ ] "Já tivemos casos parecidos?" dispara `buscar_chamados_similares`, e a resposta cita os números dos chamados.
- [ ] Fechar o painel durante a geração cancela a chamada ao provedor.
- [ ] Mais de 10 requisições por minuto → 429 com mensagem amigável.
- [ ] Nenhuma ferramenta permite escrita. Uma instrução do tipo "mude o status para fechado" é recusada pelo copiloto, que orienta o atendente a usar a UI.
- [ ] Um CPF gerado pelo modelo chega mascarado ao cliente, mesmo dividido entre dois pedaços do stream. Uma citação a um chamado que nenhuma ferramenta retornou gera `aviso` e o selo na UI.
- [ ] `IA_COPILOTO_HABILITADO=false` → o endpoint devolve 503 e a UI esconde o painel.

**Testes:**

- **Integração:** sequência de eventos SSE com o fake; ferramenta executada com o chamado correto; 429; o fake que vaza CPF e cita uma fonte inexistente produz a saída mascarada com `aviso`; copiloto desativado → 503.
- **Unitários:** validação dos parâmetros de ferramenta; mascaramento dos resultados; `FiltroSaidaCopiloto` (PII dividida entre pedaços, texto limpo intacto e em ordem, citação válida × inventada).
- **Frontend:** o parser de SSE, a renderização incremental e o selo de referência não verificada.

---

## Sprint 5: Hardening e entrega

> Inclui a **Fase 5 do checklist**: padrões de engenharia para a equipe.

**Escopo:**

- **README final:** revisão de ponta a ponta das seções construídas nas sprints anteriores, mais cobertura de testes, "o que ficaria para uma próxima versão" e onde e como assistentes de IA foram usados no desenvolvimento. Conferir item a item contra a seção 8 do enunciado.
- E2E com Playwright: criar chamado → ver a triagem → aceitar a sugestão (com o fake).
- Relatório de cobertura (Coverlet + Vitest), com o número informado no README.
- Revisão final de responsividade, acessibilidade básica (labels, foco, contraste) e mensagens de erro.
- Padrões de engenharia (`docs/padroes/`): guia de testes, convenções de código e commits, checklist de revisão e o fluxo de ADR.
- `JORNADA.md` final com as lições aprendidas e o que ficou de fora e por quê.

**Critérios de aceite:**

- [ ] Uma pessoa sem contexto clona, roda `docker compose up`, segue o README e testa tudo em menos de 10 minutos.
- [ ] Um comando único roda todos os testes, documentado no README.
- [ ] O repositório público tem o histórico incremental e o CI verde.

---

## 4. Rastreabilidade: requisitos × sprints

| Requisitos | Sprint |
|---|---|
| RF-44, NFR-08, NFR-11 (base), NFR-15 | 0 |
| RF-01, RF-03..07, RN-01..06, NFR-01, NFR-02 (listagem), NFR-14 | 1 |
| RF-02, RF-10..14, RF-17, RF-18, RN-07..10, NFR-03..07, NFR-09, NFR-10, NFR-17 | 2 |
| RF-15, RF-16, RF-30, RF-31, RF-40..43, RN-11..13, NFR-02 (dashboard), NFR-12, NFR-16 | 3 |
| RF-20..24 | 4 |
| Entrega (README, DECISOES, E2E, cobertura) | 5 |

---

## 5. Histórico de revisões do plano

| Versão | Data | Motivo | Mudanças |
|---|---|---|---|
| 1.0 | 30/09 | Plano inicial (fechamento da Fase 3) | Sprints 0 a 5, ~18,5 h. |
| 1.1 | 30/09 | [Revisão de arquitetura: padrões agênticos](revisoes/2026-09-30-padroes-agenticos.md), feita com a Sprint 0 em andamento e **antes** de qualquer código de IA | **S2:** tracing com OpenTelemetry (ADR-0019), kill switch e orçamento da triagem (ADR-0021). **S3:** evals offline com comparação sem RAG × com RAG (ADR-0018); nova tentativa corretiva como opcional. **S4:** guardrail de saída do copiloto (ADR-0020), kill switch e orçamento do copiloto (ADR-0021). Linha de corte: os evals passam a ficar acima do copiloto e do E2E. Total: ~21,5 h. A Sprint 0 não mudou. |
