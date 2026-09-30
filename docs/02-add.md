# ADD — Architecture Design Document

> **Fase do checklist:** 2. Estilo arquitetural e 3. Modelagem de dados e comunicação
> **Versão:** 0.2, que acrescenta modelo de dados, contratos de API e topologia (seções 10 a 12) e os ADRs 0007 a 0013.
> **Próxima atualização:** Fase 4 (validação no Walking Skeleton: CI, observabilidade, segurança base).

---

## 1. Contexto

O HelpDesk Inteligente registra chamados de suporte e usa IA para **sugerir** triagem (categoria, prioridade, resumo e resposta inicial). A sugestão é fundamentada em casos semelhantes já resolvidos e numa base de conhecimento (RAG). O atendente conta ainda com um **copiloto conversacional** que consulta os dados do sistema via *tool calling*. A decisão final é sempre humana.

Requisitos completos: [`01-requisitos.md`](01-requisitos.md).

## 2. Direcionadores arquiteturais (ASRs)

Estes são os requisitos que mais influenciam a arquitetura. Os demais são atendidos "naturalmente" por qualquer desenho razoável.

| # | Direcionador | Origem | Impacto no desenho |
|---|---|---|---|
| D1 | Criar chamado **não depende** do LLM | RF-02, NFR-01 | A triagem é assíncrona, fora do ciclo da requisição HTTP. |
| D2 | O LLM é **não confiável** (lento, fora do ar, com saída inválida, com rate limit) | NFR-04, NFR-05 | Timeout, retry, validação em camadas e estados explícitos (`pendente`/`falhou`). |
| D3 | **Nenhum dado pessoal** sai do sistema nem vai para o índice vetorial | RN-10, RN-11, NFR-06 | O mascaramento é um passo obrigatório e único antes de qualquer LLM ou embedding. |
| D4 | O provedor de IA é **trocável** e há um **fake por padrão** | NFR-08, NFR-09, NFR-10 | Abstração de provedor. O fake fica no nível mais baixo, para que parsing e validação rodem sempre. |
| D5 | RAG e tool calling como diferenciais de **IA conversacional** | RF-15..22 | Busca vetorial no mesmo banco, com ferramentas somente leitura. |
| D6 | Subir tudo com **um comando** e ser **avaliável em minutos** | NFR-08 | Pouca infraestrutura: só o necessário no Compose. |
| D7 | Prazo curto (7 dias) com dev solo | Restrição | Simplicidade operacional acima de "escala de papel". |

## 3. Objetivos técnicos

1. **Correção das regras de negócio.** A máquina de estados fica no domínio, é testável isoladamente e é a única fonte de verdade para a API e para o frontend.
2. **IA segura por construção.** Não é possível chamar o LLM sem passar pelo mascaramento, e não é possível persistir uma sugestão sem passar pela validação.
3. **Degradação graciosa.** Qualquer falha de IA vira um estado visível na UI, nunca um erro 500.
4. **Rastreabilidade.** Toda chamada de IA registra modelo, latência, tokens, resultado e as fontes do RAG.
5. **Simplicidade.** Cada peça de infraestrutura precisa justificar sua existência (ver ADR-0001 e ADR-0003).

---

## 4. C4 — Nível 1: Contexto

```mermaid
flowchart TB
    solicitante["👤 Solicitante<br/><i>Abre chamados</i>"]
    atendente["👤 Atendente<br/><i>Trata chamados, decide sobre a triagem,<br/>conversa com o copiloto</i>"]

    subgraph sistema [" "]
        helpdesk["<b>HelpDesk Inteligente</b><br/>Gestão de chamados com triagem<br/>assistida por IA (RAG) e copiloto"]
    end

    llm["☁️ Provedor de LLM<br/><i>Gemini (padrão real) / OpenAI / Ollama...<br/>ou Fake (padrão local)</i>"]

    solicitante -- "Abre e acompanha chamados<br/>[HTTPS]" --> helpdesk
    atendente -- "Trata, aceita/rejeita sugestões,<br/>usa o copiloto [HTTPS]" --> helpdesk
    helpdesk -- "Envia texto MASCARADO para<br/>completion e embeddings [HTTPS]" --> llm

    classDef person fill:#08427b,color:#fff,stroke:#052e56
    classDef system fill:#1168bd,color:#fff,stroke:#0b4884
    classDef external fill:#999,color:#fff,stroke:#6b6b6b
    class solicitante,atendente person
    class helpdesk system
    class llm external
    style sistema fill:none,stroke:none
```

**Fronteira de confiança:** tudo que atravessa a seta para o provedor de LLM está mascarado. O que volta é tratado como entrada não confiável.

---

## 5. C4 — Nível 2: Contêineres

```mermaid
flowchart TB
    atendente["👤 Solicitante / Atendente"]

    subgraph compose ["docker compose"]
        web["<b>Web App</b><br/>React + TypeScript (Vite)<br/>servido por Nginx<br/><i>Telas, filtros na URL, polling da triagem,<br/>chat do copiloto</i>"]
        api["<b>API</b><br/>ASP.NET Core (.NET 10)<br/><i>REST + OpenAPI, regras de negócio,<br/>copiloto (tool calling), ProblemDetails</i>"]
        worker["<b>Worker de IA</b><br/>.NET Worker Service<br/><i>Consome triagens pendentes,<br/>executa pipeline RAG,<br/>indexa embeddings</i>"]
        db[("<b>Banco</b><br/>PostgreSQL + pgvector<br/><i>Dados transacionais, fila de trabalho,<br/>vetores, métricas de IA</i>")]
    end

    llm["☁️ Provedor de LLM"]

    atendente -- "HTTPS" --> web
    web -- "JSON/HTTP" --> api
    api -- "SQL (EF Core)" --> db
    worker -- "SQL: SKIP LOCKED na fila,<br/>busca vetorial" --> db
    worker -- "Triagem + embeddings<br/>(texto mascarado)" --> llm
    api -- "Copiloto + embedding da pergunta<br/>(texto mascarado)" --> llm

    classDef person fill:#08427b,color:#fff,stroke:#052e56
    classDef container fill:#438dd5,color:#fff,stroke:#2e6295
    classDef external fill:#999,color:#fff,stroke:#6b6b6b
    class atendente person
    class web,api,worker,db container
    class llm external
```

| Contêiner | Responsabilidade | Por que existe separado |
|---|---|---|
| **Web App** | UI e acesso à API isolado em uma camada de serviço/hooks. | Exigência do enunciado (SPA React). |
| **API** | Casos de uso síncronos e o copiloto (interação em tempo real com o atendente). | Ponto de entrada HTTP. |
| **Worker** | Trabalho lento e falível: triagem e indexação. | Isola a latência e as falhas do LLM da API, e escala de forma independente (ADR-0001, ADR-0003). |
| **PostgreSQL + pgvector** | Fonte única de verdade: dados, fila e vetores. | Uma peça de infraestrutura a menos para operar (ADR-0003; o vetor será detalhado na Fase 3). |

> A API e o Worker são **dois processos do mesmo código-fonte** (mesma solução, mesmas camadas de domínio e aplicação). Isso é um monólito modular com dois *hosts*, não microsserviços (ver ADR-0001).

---

## 6. Visão lógica — módulos e camadas

### Módulos (bounded contexts leves)

| Módulo | Responsabilidade |
|---|---|
| **Chamados** | Chamado, Comentário, HistoricoStatus e a máquina de estados. |
| **Triagem** | TriagemIA, pipeline RAG, validação da saída e aceite/rejeição. |
| **Conhecimento** | Artigos, embeddings, indexação e busca semântica. |
| **Copiloto** | Sessão de chat e ferramentas somente leitura. |
| **Dashboard** | Consultas de agregação (lado de leitura). |
| **Compartilhado (IA)** | Abstração de provedor, mascaramento LGPD, prompts versionados e telemetria de IA. |

### Camadas (ADR-0002)

```mermaid
flowchart LR
    subgraph hosts ["Hosts"]
        apiH["Api<br/><i>endpoints, ProblemDetails,<br/>OpenAPI</i>"]
        wH["Worker<br/><i>BackgroundServices</i>"]
    end
    app["Application<br/><i>casos de uso, portas,<br/>pipeline de triagem,<br/>ferramentas do copiloto</i>"]
    dom["Domain<br/><i>entidades, máquina de estados,<br/>invariantes, value objects</i>"]
    infra["Infrastructure<br/><i>EF Core, pgvector, provedores LLM<br/>(Fake / OpenAI-compatível),<br/>fila em tabela</i>"]

    apiH --> app
    wH --> app
    app --> dom
    infra --> app
    apiH -. "composição (DI)" .-> infra
    wH -. "composição (DI)" .-> infra
```

**Regra de dependência:** o `Domain` não depende de nada. A `Application` depende do `Domain` e de abstrações. A `Infrastructure` implementa as portas da `Application`.

---

## 7. Cenários principais (visão de runtime)

### Criação do chamado + triagem assíncrona com RAG

```mermaid
sequenceDiagram
    autonumber
    actor S as Solicitante
    participant W as Web App
    participant A as API
    participant DB as PostgreSQL
    participant K as Worker
    participant L as LLM

    S->>W: Preenche formulário
    W->>A: POST /api/chamados
    A->>DB: BEGIN: INSERT chamado + INSERT triagem(pendente)
    A-->>W: 201 Created (triagem: pendente)
    Note over A,L: A resposta HTTP não espera o LLM (D1)

    loop polling curto
        K->>DB: SELECT ... FOR UPDATE SKIP LOCKED
    end
    K->>K: Mascara título/descrição (LGPD)
    K->>L: Embedding do texto mascarado
    K->>DB: Top-k chamados resolvidos + artigos (pgvector)
    K->>L: Prompt versionado + contexto + categorias válidas
    L-->>K: JSON estruturado
    K->>K: Parse → schema → validação de domínio
    alt válido
        K->>DB: triagem = concluida (modelo, tokens, latência, fontes)
    else inválido / timeout / erros após retries
        K->>DB: triagem = falhou (motivo)
    end
    W->>A: GET /api/chamados/{id} (polling enquanto pendente)
```

### Copiloto com tool calling

```mermaid
sequenceDiagram
    autonumber
    actor At as Atendente
    participant A as API (Copiloto)
    participant L as LLM
    participant DB as PostgreSQL

    At->>A: "Já tivemos casos assim? Como resolvemos?"
    A->>A: Mascara a mensagem + injeta contexto do chamado
    A->>L: Mensagens + definição das ferramentas (somente leitura)
    L-->>A: tool_call: buscar_chamados_similares(...)
    A->>DB: Executa a ferramenta (consulta parametrizada)
    A->>L: Resultado da ferramenta
    L-->>A: Resposta final citando as fontes
    A-->>At: Resposta (marcada como gerada por IA)
```

---

## 8. Decisões registradas

| ADR | Decisão | Status |
|---|---|---|
| [ADR-0001](adr/0001-monolito-modular.md) | Monólito modular com dois hosts (API + Worker) em vez de microsserviços | Aceita |
| [ADR-0002](adr/0002-clean-architecture-pragmatica.md) | Clean Architecture pragmática em vez de Vertical Slice pura | Aceita |
| [ADR-0003](adr/0003-fila-em-tabela-postgres.md) | Fila de trabalho em tabela PostgreSQL (SKIP LOCKED) em vez de broker externo | Aceita |
| [ADR-0004](adr/0004-triagem-pipeline-rag-deterministico.md) | Triagem como pipeline RAG determinístico; tool calling só no copiloto | Aceita |
| [ADR-0005](adr/0005-abstracao-provedor-llm.md) | Microsoft.Extensions.AI + adaptador OpenAI-compatível em vez de SDKs nativos por provedor | Aceita (validar na PoC) |
| [ADR-0006](adr/0006-gemini-free-tier-e-lgpd.md) | Gemini free tier como provedor real de demonstração, com mascaramento obrigatório | Aceita |
| [ADR-0007](adr/0007-pgvector-no-postgres.md) | pgvector no próprio PostgreSQL em vez de banco vetorial dedicado | Aceita |
| [ADR-0008](adr/0008-busca-textual-pg-trgm.md) | Busca textual com `pg_trgm` (substring, sem acento) em vez de full-text search | Aceita |
| [ADR-0009](adr/0009-sql-explicito-no-dashboard.md) | SQL explícito nas leituras analíticas; EF Core nas escritas | Aceita |
| [ADR-0010](adr/0010-filas-derivadas-do-estado.md) | Filas derivadas do estado das entidades em vez de tabela genérica de jobs | Aceita |
| [ADR-0011](adr/0011-estrategia-de-embeddings.md) | Tabela única de documentos RAG, dimensão fixa (768) e reconciliação por modelo | Aceita (validar na PoC) |
| [ADR-0012](adr/0012-copiloto-com-streaming-sse.md) | Copiloto com streaming SSE em vez de resposta completa | Aceita |
| [ADR-0013](adr/0013-minimal-apis.md) | Minimal APIs com route groups em vez de Controllers | Aceita |
| [ADR-0014](adr/0014-fluxo-git-trunk-based.md) | Fluxo Git trunk-based com uma branch por sprint em vez de GitFlow | Aceita |
| [ADR-0015](adr/0015-migrations-em-servico-one-shot.md) | Migrations e seed num serviço one-shot em vez do startup da API | Aceita |
| [ADR-0016](adr/0016-logs-estruturados-nativos.md) | Logging nativo do .NET em JSON em vez de Serilog | Aceita |
| [ADR-0017](adr/0017-ui-kit-mantine.md) | Mantine como biblioteca de UI em vez de Tailwind + shadcn/ui | Aceita |
| [ADR-0018](adr/0018-evals-offline-da-ia.md) | Evals offline com conjunto rotulado e harness próprio | Aceita (revisão 30/09) |
| [ADR-0019](adr/0019-tracing-opentelemetry.md) | Tracing com OpenTelemetry e Aspire Dashboard opcional | Aceita (revisão 30/09) |
| [ADR-0020](adr/0020-guardrail-de-saida-do-copiloto.md) | Guardrail de saída do copiloto (PII + citações verificadas) | Aceita (revisão 30/09) |
| [ADR-0021](adr/0021-kill-switch-e-orcamentos-de-ia.md) | Kill switches por funcionalidade e orçamentos de tokens | Aceita (revisão 30/09) |

As decisões de plataforma da Sprint 0 usam os ADRs 0015 a 0017 (migrations, logs e UI kit do frontend) e 0022 a 0023 (estratégia de CI e gestão de segredos). A revisão de 30/09 está registrada em [`revisoes/2026-09-30-padroes-agenticos.md`](revisoes/2026-09-30-padroes-agenticos.md).

---

## 9. Riscos e mitigações

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| O endpoint OpenAI-compatível do Gemini não suportar bem `json_schema` ou tools em algum modelo | Média | Alto | Validar na PoC (Fase 4). O fallback é `json_object` + validação própria, ou um adaptador nativo (ADR-0005). |
| Rate limit do free tier durante a demonstração | Alta | Médio | O fake é o padrão. Retry com backoff para 429. O estado `falhou` é visível e há o botão "Refazer". |
| Escopo (RAG + copiloto) estourar o prazo | Média | Alto | Ordem de entrega: obrigatório → RAG na triagem → copiloto. O copiloto pode cair para Could. |
| Embeddings de modelos diferentes misturados no índice | Média | Médio | A busca filtra por `embedding_modelo` e o reconciliador reindexa ao trocar de modelo (ADR-0010, ADR-0011). |
| Endpoint compatível não aceitar `dimensions` para embeddings do Gemini | Média | Médio | Validar na PoC. O plano B é truncar e renormalizar no adaptador (ADR-0011). |
| Prompt injection via descrição do chamado | Média | Médio | Conteúdo delimitado no prompt, saída validada contra o domínio, e ferramentas do copiloto somente leitura. |

---

## 10. Modelo de dados

Detalhe completo em [`03-modelo-de-dados.md`](03-modelo-de-dados.md): ER, constraints, 14 índices justificados (e os deliberadamente não criados), SQL do dashboard e estratégia de seed.

Pontos arquiteturalmente relevantes:

- **Tudo num PostgreSQL:** dados transacionais, filas (ADR-0010) e vetores (ADR-0007). Uma transação cobre chamado, histórico e triagem pendente.
- **Regras de negócio duplicadas no banco onde são verificáveis** (`CHECK` de `resolvido_em` coerente, Crítica nunca cancelada, uma triagem pendente por chamado). A máquina de estados, por sua vez, vive só no domínio.
- **Rastreabilidade de IA no modelo:** `prompt_versao`, `modelo`, `fontes` e a tabela `uso_llm` como livro-razão de consumo.

## 11. Contratos de comunicação

Detalhe completo em [`04-contratos-api.md`](04-contratos-api.md).

| Interação | Protocolo | Decisão |
|---|---|---|
| Web → API (CRUD, listagem, dashboard) | REST + JSON, erros em ProblemDetails | ADR-0013 |
| Web → API (copiloto) | `POST` + Server-Sent Events | ADR-0012 |
| Web ← triagem assíncrona | Polling do detalhe enquanto `Pendente` (a cada 2 s, com backoff até 10 s) | Simples. Push via SignalR fica como evolução (ADR-0012). |
| API/Worker → banco | EF Core (escritas, listagem) e SQL explícito (dashboard) | ADR-0009 |
| API/Worker → Worker | Nenhuma comunicação direta: coordenação **pelo estado no banco** | ADR-0003, ADR-0010 |
| API/Worker → LLM | HTTPS OpenAI-compatível, com texto mascarado | ADR-0005, ADR-0006 |

## 12. Topologia de implantação (Docker Compose)

```mermaid
flowchart LR
    browser["🌐 Navegador"]

    subgraph net ["rede: helpdesk"]
        web["web<br/>Nginx + SPA<br/>:8080 → host"]
        api["api<br/>ASP.NET Core<br/>:8080 (interno)<br/>:5080 → host (Swagger)"]
        worker["worker<br/>.NET Worker<br/>(sem porta)"]
        migrator["migrator<br/>(one-shot)<br/>migrations + seed"]
        db[("db<br/>pgvector/pgvector<br/>:5432 → host (opcional)<br/>volume: pgdata")]
    end

    llm["☁️ LLM (opcional)"]

    browser -- ":8080" --> web
    web -- "/api/* (proxy, sem CORS)" --> api
    api --> db
    worker --> db
    migrator --> db
    api -. "se LLM_PROVIDER ≠ fake" .-> llm
    worker -. "se LLM_PROVIDER ≠ fake" .-> llm
```

| Serviço | Depende de | Healthcheck |
|---|---|---|
| `db` | — | `pg_isready` |
| `migrator` | `db` saudável | Termina com código 0 (`service_completed_successfully`). |
| `api` | `migrator` concluído | `GET /health` |
| `worker` | `migrator` concluído | — (a saúde aparece no `/health` da API via idade da fila) |
| `web` | `api` saudável | — |

- **O Nginx faz o proxy de `/api`**, então o frontend e a API ficam na mesma origem: sem CORS e sem URL de API compilada no bundle.
- **Migrations num serviço one-shot** (e não no startup da API) evitam duas réplicas migrando ao mesmo tempo. A decisão formal será tomada no ADR de plataforma da Fase 4.

Variáveis de ambiente (o `.env.example` completo sai na Sprint 0):

| Variável | Padrão | Uso |
|---|---|---|
| `POSTGRES_USER` / `POSTGRES_PASSWORD` / `POSTGRES_DB` | `helpdesk` / *(dev)* / `helpdesk` | Banco local. |
| `ConnectionStrings__Default` | montada no Compose | API, Worker e migrator. |
| `LLM_PROVIDER` | `fake` | `fake` \| `openai-compatible` |
| `LLM_BASE_URL` / `LLM_API_KEY` | — | Provedor real (ADR-0005). |
| `LLM_CHAT_MODEL` / `LLM_EMBEDDING_MODEL` | — | Modelos configuráveis. |
| `EMBEDDING_DIMENSIONS` | `768` | ADR-0011. |
| `LLM_TIMEOUT_SECONDS` / `LLM_MAX_RETRIES` | `15` / `2` | NFR-04. |
| `RAG_TOP_K` / `RAG_MIN_SIMILARITY` | `3` / `0.35` | ADR-0011. |
| `WORKER_POLL_INTERVAL_MS` / `WORKER_BATCH_SIZE` / `WORKER_RECONCILE_INTERVAL_SECONDS` | `1500` / `5` / `30` | ADR-0003, ADR-0010. |
| `COPILOTO_RATE_LIMIT_POR_MINUTO` | `10` | Proteção de cota. |
