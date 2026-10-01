# Fase 1 — Entendimento do negócio e requisitos

> **Fase do checklist:** 1. Entendimento do negócio e requisitos não funcionais (NFRs)
> **Fonte:** [`00-enunciado.md`](00-enunciado.md)
> **Status:** validado em 2026-09-30

Este documento traduz o enunciado em requisitos rastreáveis. Cada requisito tem um ID (`RF`, `RN`, `NFR`, `P`) para ser referenciado nos ADRs, nas sprints e nos testes.

Legenda de prioridade (MoSCoW):

- **M** (Must): exigido pelo enunciado.
- **S** (Should): diferencial escolhido como estratégico para a vaga de IA Engineer conversacional.
- **C** (Could): diferencial que entra se sobrar tempo.
- **W** (Won't): fora do escopo desta versão.

---

## 1. Contexto do negócio

Uma equipe de suporte recebe chamados de usuários. O gargalo é a **triagem**: ler, classificar, priorizar e escrever a primeira resposta. A IA reduz esse trabalho sugerindo classificação, resumo e resposta. **Quem decide é sempre o atendente** (human-in-the-loop).

A vaga é de **IA Engineer conversacional**. Por isso, além do mínimo pedido, o produto se diferencia em dois pontos:

1. **Triagem com RAG:** a sugestão é fundamentada em chamados semelhantes já resolvidos e numa base de conhecimento, não só no texto do chamado.
2. **Copiloto do atendente:** um chat contextual no detalhe do chamado, com *tool calling* sobre os dados do sistema.

### Atores

| Ator | Descrição |
|---|---|
| **Solicitante** | Abre o chamado e informa nome, e-mail, título e descrição. |
| **Atendente** | Trata o chamado: muda status, comenta, aceita ou rejeita a triagem e usa o copiloto. |
| **Provedor de LLM** | Sistema externo (Gemini, OpenAI, Ollama...) ou fake. É tratado como **não confiável** e **não disponível garantidamente**. |
| **Worker de IA** | Processo em background que executa triagens e indexação de embeddings. |

---

## 2. Escopo

| Item | Prioridade |
|---|---|
| CRUD de chamados, comentários e máquina de status | M |
| Triagem por IA assíncrona, validada, com mascaramento LGPD | M |
| Provedor de LLM plugável (Fake padrão + Gemini + extensível) | M |
| Dashboard com agregações no banco | M |
| Frontend React com as 5 telas | M |
| Testes unitários, de integração (banco real) e de frontend | M |
| Docker Compose com um comando, README, DECISOES.md | M |
| **RAG** na triagem (pgvector + chamados resolvidos + base de conhecimento) | S |
| **Copiloto do atendente** com tool calling | S |
| Registro de tokens consumidos (controle de custo) | S |
| Métrica aceitas x rejeitadas por categoria | S |
| Pipeline de CI (build + testes) | S |
| Correlation id + logs estruturados | M (logs) / S (correlation id) |
| E2E com Playwright | C |
| Autenticação JWT com perfis | C |
| OpenTelemetry (tracing) | ~~C~~ → **S** *(revisão 30/09, ADR-0019)* |
| Evals offline da IA (conjunto rotulado + harness) | **S** *(revisão 30/09, ADR-0018)* |
| Guardrail de saída do copiloto | **S** *(revisão 30/09, ADR-0020)* |
| Kill switches e orçamentos de IA | **S** *(revisão 30/09, ADR-0021)* |
| App Flutter, deploy em nuvem | W |

---

## 3. Requisitos funcionais

### Chamados

| ID | Requisito | Prior. |
|---|---|---|
| RF-01 | Criar chamado com título, descrição, nome e e-mail do solicitante. Validar obrigatórios e formato do e-mail. | M |
| RF-02 | A criação dispara a triagem por IA **sem aguardá-la**: a resposta HTTP retorna com a triagem `pendente`. | M |
| RF-03 | Listar chamados com filtros por status, prioridade, categoria, texto (título/descrição) e período de criação. | M |
| RF-04 | Paginar a listagem e ordenar por data de criação ou prioridade (asc/desc). | M |
| RF-05 | Obter o detalhe com comentários, histórico de status e a triagem vigente. | M |
| RF-06 | Mudar status respeitando a máquina de estados (RN-01 a RN-06). | M |
| RF-07 | Adicionar comentário (bloqueado em estados finais). | M |

### Triagem por IA

| ID | Requisito | Prior. |
|---|---|---|
| RF-10 | Gerar sugestão estruturada: categoria, prioridade, resumo (≤ 200 caracteres), resposta sugerida e confiança (0–1). | M |
| RF-11 | Validar a saída do modelo antes de salvar; se inválida, marcar a triagem como `falhou` e registrar o motivo. | M |
| RF-12 | Refazer a triagem manualmente. | M |
| RF-13 | Aceitar a triagem, aplicando categoria e prioridade sugeridas ao chamado. | M |
| RF-14 | Rejeitar a triagem sem alterar o chamado. | M |
| RF-15 | Recuperar chamados resolvidos semelhantes e artigos da base de conhecimento para enriquecer o prompt (RAG). | S |
| RF-16 | Exibir no painel da IA as fontes usadas pelo RAG (chamados/artigos semelhantes). | S |
| RF-17 | Registrar tokens de entrada/saída e modelo usado em cada chamada ao LLM. | S |
| RF-18 | *(revisão 30/09)* A triagem por IA pode ser desativada por configuração (kill switch). Desativada, o chamado é criado sem triagem e o sistema continua funcionando (ADR-0021). | S |

### Copiloto do atendente

| ID | Requisito | Prior. |
|---|---|---|
| RF-20 | Chat no detalhe do chamado, com o contexto do chamado atual. | S |
| RF-21 | O LLM pode chamar **ferramentas somente leitura**: buscar chamados semelhantes, buscar artigos, obter histórico do chamado e obter métricas da categoria. | S |
| RF-22 | O copiloto **não executa ações de escrita** (mudar status, aceitar triagem). Ele pode sugerir, mas quem executa é o atendente, pela UI. | S |
| RF-23 | *(revisão 30/09)* A resposta do copiloto passa por um guardrail de saída: dados pessoais são mascarados e as citações de chamados são verificadas contra os resultados das ferramentas. Uma citação sem fonte é sinalizada ao atendente (ADR-0020). | S |
| RF-24 | *(revisão 30/09)* O copiloto pode ser desativado por configuração (kill switch), e cada resposta tem um limite de tokens (ADR-0021). | S |

### Base de conhecimento e indexação

| ID | Requisito | Prior. |
|---|---|---|
| RF-30 | Manter artigos de base de conhecimento (título, conteúdo, categoria). Na v1, são carregados via seed. | S |
| RF-31 | Gerar embeddings de artigos e de chamados resolvidos em background: na subida do sistema e quando um chamado entra em Resolvido. | S |

### Dashboard e operação

| ID | Requisito | Prior. |
|---|---|---|
| RF-40 | Totais por status e por prioridade. | M |
| RF-41 | Tempo médio de resolução em horas, por categoria. | M |
| RF-42 | Taxa de aceitação das sugestões da IA. | M |
| RF-43 | Aceitas x rejeitadas por categoria. | S |
| RF-44 | Health check da API e do banco (`GET /health`). | M |

---

## 4. Regras de negócio

| ID | Regra |
|---|---|
| RN-01 | As únicas transições permitidas são: Aberto→EmAndamento, Aberto→Cancelado, EmAndamento→Resolvido, Resolvido→Fechado e Resolvido→EmAndamento (reabrir). Qualquer outra transição → **409**. |
| RN-02 | Toda mudança de status gera um registro em `HistoricoStatus` **na mesma transação**. |
| RN-03 | Ao entrar em Resolvido, preencher `resolvidoEm`. Ao reabrir, limpar `resolvidoEm`. |
| RN-04 | Fechado e Cancelado são finais: não aceitam comentário nem mudança de status (**409**). |
| RN-05 | Chamado com prioridade Crítica não pode ser cancelado (**409**). |
| RN-06 | A mudança de status para o mesmo status atual é rejeitada (não é uma transição). |
| RN-07 | Só é possível aceitar ou rejeitar uma triagem com status `concluida`. Caso contrário → **409**. |
| RN-08 | Não é possível aceitar triagem de chamado em estado final (**409**). |
| RN-09 | A categoria sugerida deve existir no banco e a prioridade deve pertencer ao enum. Caso contrário, a triagem fica `falhou`. |
| RN-10 | Antes de qualquer envio ao LLM, e-mails, telefones e CPFs no texto são mascarados. Nome e e-mail do solicitante **nunca** são enviados. |
| RN-11 | Só é indexado para RAG o texto **já mascarado**. O vetor store nunca contém dado pessoal. |
| RN-12 | A taxa de aceitação é calculada como `aceitas / (aceitas + rejeitadas)`. Triagens pendentes, falhas ou não decididas ficam fora do denominador. |
| RN-13 | O tempo de resolução é `resolvidoEm − criadoEm`, considerando chamados com `resolvidoEm` preenchido (Resolvido ou Fechado). |

---

## 5. Casos de uso-chave

### UC-01 — Abrir chamado

1. O solicitante preenche o formulário; o cliente valida os campos.
2. A API valida (**400/422** com ProblemDetails se inválido).
3. A API grava o chamado (status Aberto) e a triagem (`pendente`) na mesma transação.
4. A API retorna **201** imediatamente.
5. O worker processa a triagem de forma assíncrona (UC-02).

**Cenário de exceção:** se o LLM estiver fora do ar, o chamado continua criado e a triagem vai para `falhou` após os retries.

### UC-02 — Triagem assíncrona com RAG

1. O worker obtém a triagem pendente.
2. Mascara o título e a descrição (RN-10).
3. Gera o embedding da consulta e busca os top-k chamados resolvidos e artigos semelhantes.
4. Monta o prompt versionado com as categorias válidas, o contexto recuperado e o texto mascarado.
5. Chama o LLM com timeout e retry, pedindo saída estruturada.
6. Valida a saída (schema + RN-09). Grava como `concluida` ou `falhou`, com modelo, latência, tokens e fontes.
7. Registra log estruturado **sem dados pessoais**.

### UC-03 — Decidir sobre a triagem

1. O atendente vê o painel da IA (com o selo de conteúdo gerado por IA) e as fontes usadas.
2. Aceita (o chamado recebe a categoria e a prioridade sugeridas), rejeita ou refaz a triagem.

### UC-04 — Tratar o chamado

O atendente muda o status, e a UI mostra apenas as transições válidas. Também comenta e acompanha o histórico.

### UC-05 — Conversar com o copiloto

1. O atendente pergunta, por exemplo: "já tivemos casos assim? como resolvemos?"
2. O LLM decide chamar ferramentas (por exemplo, `buscar_chamados_similares`).
3. O backend executa a ferramenta e devolve o resultado ao LLM.
4. O LLM responde citando as fontes.
5. O conteúdo enviado ao LLM é mascarado (RN-10).

### UC-06 — Acompanhar indicadores

O gestor ou atendente abre o dashboard, com cartões e gráficos calculados por agregação SQL.

---

## 6. Requisitos não funcionais (atributos de qualidade)

Os alvos abaixo servem como referência para decisões de design. Eles valem para o ambiente local (Docker, seed de ~200 chamados) e não são SLAs de produção.

| ID | Atributo | Requisito / alvo |
|---|---|---|
| NFR-01 | **Latência — criação** | `POST /api/chamados` com p95 < 300 ms, **independente** do provedor de LLM (a triagem é assíncrona). |
| NFR-02 | **Latência — leitura** | Listagem e dashboard com p95 < 200 ms. Filtros e ordenações apoiados por índices. O dashboard nunca carrega registros em memória. |
| NFR-03 | **Latência — triagem** | Triagem concluída em < 30 s quando o provedor está saudável. O frontend faz polling enquanto a triagem está `pendente`. |
| NFR-04 | **Resiliência** | Timeout (padrão 15 s) e retries (padrão 2, com backoff exponencial + jitter) configuráveis por variável de ambiente. HTTP 429 (rate limit do free tier) é tratado como erro transitório. Uma falha do LLM nunca derruba a API. |
| NFR-05 | **Robustez da saída** | Toda saída do LLM é tratada como entrada não confiável: parse tolerante + validação de schema + validação de domínio. |
| NFR-06 | **Privacidade (LGPD)** | Mascaramento antes do envio. Nenhum dado pessoal em logs, prompts ou embeddings. O free tier do Gemini pode usar dados para melhoria do produto; a mitigação é o mascaramento (ver ADR específico). |
| NFR-07 | **Segurança** | Nenhum segredo no repositório (`.env.example`). O copiloto só tem ferramentas de leitura. Há mitigação básica de prompt injection: delimitação do conteúdo do usuário e validação de saída. |
| NFR-08 | **Portabilidade** | `docker compose up` sobe banco, API, worker e frontend, com migrations, seed e **IA fake por padrão**. Não exige chave de API. |
| NFR-09 | **Extensibilidade de IA** | Trocar de provedor (Fake / Gemini / outros) exige só variável de ambiente e uma nova implementação da interface, sem alterar a regra de negócio. |
| NFR-10 | **Testabilidade** | Todos os testes rodam sem chave de API. Os testes de integração usam PostgreSQL real (Testcontainers). O embedding fake é determinístico. |
| NFR-11 | **Observabilidade** | Logs estruturados (JSON) com correlation id. Chamadas ao LLM registram provedor, modelo, latência, sucesso/falha e tokens, sem conteúdo pessoal. |
| NFR-12 | **Custo** | Consumo de tokens registrado por chamada. Top-k e tamanho de contexto do RAG limitados e configuráveis. |
| NFR-13 | **Escalabilidade** | O worker de IA é separável da API (escala independente). Não é alvo desta versão escalar horizontalmente, mas o desenho não pode impedir isso. |
| NFR-14 | **Usabilidade** | Estados de loading, vazio e erro em todas as telas. Layout responsivo. Conteúdo de IA claramente sinalizado. |
| NFR-15 | **Manutenibilidade** | Separação em camadas (API / aplicação / domínio / infraestrutura). Prompts versionados em arquivo. Decisões registradas em ADRs. |
| NFR-16 | **Avaliação da IA** *(revisão 30/09)* | Um conjunto rotulado de ~30 casos (incluindo injeção e PII) e um harness que mede acurácia de categoria e de prioridade, pass^k, taxa de saída válida, segurança, latência p95 e custo por triagem bem-sucedida. Toda nova versão de prompt passa pelo harness antes de virar a padrão (ADR-0018). |
| NFR-17 | **Rastreabilidade de execução** *(revisão 30/09)* | Traces OpenTelemetry com um span por etapa do pipeline de triagem e por rodada de ferramenta do copiloto, sem nenhum conteúdo de usuário nos atributos (ADR-0019). |

---

## 7. Restrições

| Tipo | Restrição |
|---|---|
| Técnica | Backend em C# (.NET 8+), frontend React + TypeScript, PostgreSQL, Docker Compose e migrations versionadas. |
| Prazo | 7 dias corridos. Esforço de referência: 10–14 h. O escopo S excede esse valor, de forma consciente. |
| Custo | LLM real com free tier (Gemini), portanto sujeito a rate limit e cotas. |
| Legal | LGPD. Seed apenas com dados fictícios. |
| Processo | Histórico de commits incremental. README e DECISOES.md obrigatórios. |

---

## 8. Premissas (a registrar no DECISOES.md)

| ID | Premissa | Justificativa |
|---|---|---|
| P-01 | **Backend em C# / .NET** (versão LTS mais recente disponível). | É a stack principal da vaga e está declarada como diferencial. |
| P-02 | **`categoriaId` e `prioridade` são opcionais na criação.** A prioridade padrão é Média e a categoria fica nula até o aceite ou uma edição. | O solicitante normalmente não sabe classificar, e é exatamente isso que a IA sugere. Se fossem obrigatórias, a triagem perderia o sentido. |
| P-03 | **Sem autenticação na v1.** O atendente é informado em campo livre (`alteradoPor`, `autor`). | A autenticação é diferencial. Ficou registrada como próxima versão. |
| P-04 | **Refazer triagem cria um novo registro** de `TriagemIA`. A vigente é a mais recente, e o histórico é preservado. | Permite auditoria e métricas de qualidade. |
| P-05 | O LLM responde em **português do Brasil**. | Público do produto. |
| P-06 | É permitido comentar em chamados Resolvidos (só os finais bloqueiam). | O enunciado só bloqueia Fechado e Cancelado. |
| P-07 | A ordenação por prioridade usa a ordem de negócio (Crítica > Alta > Média > Baixa), não a ordem alfabética. | Corresponde à expectativa do usuário. |
| P-08 | O histórico do copiloto é **efêmero** (por sessão na UI, sem persistência na v1). | Reduz escopo e evita armazenar conversas com possíveis dados pessoais. |
| P-09 | A mudança de status aceita um **comentário opcional**, gravado na mesma transação. | Ao resolver, esse comentário descreve a solução e é o principal insumo do RAG. |
| P-10 | A **criação** do chamado gera um registro de histórico `null → Aberto` (autor "sistema"). | A linha do tempo fica completa desde o início, e o tempo de cada fase é calculável. |
| P-11 | Não é possível refazer triagem nem aceitar sugestão em chamados Fechados ou Cancelados. | Estende a RN-04: estados finais são imutáveis. |

---

## 9. Questões resolvidas

- [x] P-02 confirmada: `categoriaId` e `prioridade` são opcionais na criação.
- [x] O copiloto (RF-20 a RF-22) é Should. Se o prazo apertar, ele é o primeiro a cair para Could.
- [x] Volume do seed: ~200 chamados e ~25 artigos de base de conhecimento.
