# Como construímos o HelpDesk Inteligente

Este documento conta a história do projeto, fase a fase, seguindo o processo de arquitetura adotado. Cada fase lista os artefatos produzidos e as decisões tomadas, com links para os documentos detalhados.

## Processo adotado

1. Entendimento do negócio e requisitos não funcionais.
2. Estilo arquitetural e trade-offs (início do ADD, C4 de Contexto/Contêiner, primeiros ADRs).
3. Modelagem de dados e estratégia de comunicação (ER, contratos de API, novos ADRs), fechando com o **planejamento das sprints**.
4. Walking Skeleton, que corresponde à Sprint 0: CI, observabilidade, segurança base e subida real do ambiente.
5. Padrões de engenharia: `/docs/adr`, guias de teste, linters e revisão.

Regra para os ADRs: toda decisão relevante compara **pelo menos duas alternativas** e registra a escolha, os trade-offs e as consequências.

---

## Fase 1 — Entendimento do negócio e requisitos

**Artefato:** [`01-requisitos.md`](01-requisitos.md)

O que fizemos:

- Convertemos o enunciado em requisitos rastreáveis (RF, RN, NFR), classificados por MoSCoW.
- Definimos o posicionamento do produto para uma vaga de IA Engineer conversacional: **triagem com RAG** e **copiloto com tool calling**, além do mínimo exigido.
- Quantificamos os atributos de qualidade. O mais importante: a criação do chamado **não depende** do LLM.
- Registramos as premissas onde o enunciado era ambíguo. Exemplo: a categoria e a prioridade são opcionais na criação, porque é justamente isso que a IA sugere.

Decisão de escopo relevante: RAG e tool calling **não** são aplicados no mesmo lugar. A triagem usa um pipeline RAG determinístico; o tool calling fica no copiloto conversacional. A justificativa completa virá em ADR na Fase 2.

---

## Fase 2 — Estilo arquitetural e trade-offs

**Artefatos:** [`02-add.md`](02-add.md) (ADD v0.1) e [`adr/0001`](adr/0001-monolito-modular.md) a [`adr/0006`](adr/0006-gemini-free-tier-e-lgpd.md)

O que fizemos:

- Extraímos dos requisitos os **7 direcionadores arquiteturais**. Os decisivos são: a criação não depende do LLM, o LLM é tratado como não confiável, nenhum dado pessoal sai do sistema e o provedor é trocável com fake por padrão.
- Desenhamos o **C4 de Contexto e de Contêiner**. A fronteira de confiança fica explícita: só texto mascarado atravessa para o provedor de LLM.
- Tomamos as decisões de estilo, cada uma comparando duas alternativas:

| ADR | Rejeitamos | Escolhemos | Argumento central |
|---|---|---|---|
| 0001 | Microsserviços | Monólito modular, API + Worker | Uma transação local garante chamado + triagem sem dual-write; o Worker isola o LLM. |
| 0002 | Vertical Slice pura | Clean Architecture pragmática | Máquina de estados e mascaramento são transversais e precisam de um lar único; sem mediator nem AutoMapper. |
| 0003 | Broker externo (RabbitMQ) | Tabela como fila com `SKIP LOCKED` | O broker *também* exigiria outbox; a tabela já é a outbox, com zero infraestrutura extra. |
| 0004 | Agente na triagem | Pipeline RAG determinístico (e agente só no copiloto) | Usar agente só onde a sequência de passos não é conhecida de antemão. |
| 0005 | SDK nativo por provedor | Microsoft.Extensions.AI + endpoint OpenAI-compatível | Um adaptador atende Gemini, OpenAI e Ollama; o fake no nível mais baixo exercita a validação real. |
| 0006 | Só Ollama ou tier pago | Gemini free tier opt-in + mascaramento tipado | O free tier pode usar os dados, então o mascaramento vira garantia de compilação (`TextoMascarado`). |

Pesquisa feita nesta fase: confirmamos na documentação oficial que o Gemini oferece endpoint compatível com OpenAI (tools, saída estruturada, embeddings) e que o free tier pode usar os dados para melhoria de produto. Os dois fatos mudaram decisões (ADR-0005 e ADR-0006).

Riscos levados para a Fase 4 (PoC): as lacunas de compatibilidade do Gemini com `json_schema` e tools, e o rate limit do free tier.

---

## Fase 3 — Modelagem de dados e comunicação

**Artefatos:** [`03-modelo-de-dados.md`](03-modelo-de-dados.md), [`04-contratos-api.md`](04-contratos-api.md), ADD v0.2 (seções 10–12), [`adr/0007`](adr/0007-pgvector-no-postgres.md) a [`adr/0013`](adr/0013-minimal-apis.md) e [`05-sprints.md`](05-sprints.md)

O que fizemos:

- **Modelamos os dados pensando no que o banco deve garantir sozinho.**
  - As regras verificáveis viraram `CHECK`: `resolvido_em` coerente com o status, Crítico nunca cancelado e uma única triagem pendente por chamado.
  - A máquina de estados ficou só no domínio, porque duplicá-la num trigger seria manter a mesma regra em duas linguagens.
  - Enums nativos cuja ordem de declaração é a ordem de negócio, o que faz o `ORDER BY prioridade` funcionar sem `CASE`.
- **Justificamos cada índice** a partir dos filtros e ordenações da listagem. Também registramos os índices que decidimos **não** criar, e por quê.
- **Escrevemos o SQL do dashboard antes do código**, com `FILTER`, `ROLLUP` e `NULLIF`, porque a qualidade das consultas é um critério de avaliação.
- **Definimos os contratos da API**:
  - catálogo de erros com semântica clara entre 400, 409, 412 e 422;
  - `transicoesPermitidas` calculadas pelo domínio e entregues ao frontend;
  - contrato de eventos SSE do copiloto.
- **Desenhamos a topologia**: o Nginx faz o proxy de `/api` (sem CORS), e as migrations rodam num serviço one-shot.

Decisões desta fase:

| ADR | Rejeitamos | Escolhemos | Argumento central |
|---|---|---|---|
| 0007 | Qdrant | pgvector | Vetor e dado de negócio na mesma transação e na mesma consulta; FK com cascade. |
| 0008 | Full-text search | `pg_trgm` + `unaccent` | O atendente busca códigos e trechos (`ERR-5`), não radicais linguísticos. |
| 0009 | LINQ no dashboard | SQL explícito | O SQL é o artefato avaliado, então precisa estar visível. |
| 0010 | Tabela genérica de jobs | Filas derivadas do estado | Nenhum evento pode se perder; trocar o modelo de embedding reindexa sozinho. |
| 0011 | Embedding em coluna | Tabela `documentos_rag` com 768 dimensões | Busca unificada, chunking, e fake e real compatíveis. |
| 0012 | JSON completo | SSE | Numa vaga de IA conversacional, mostrar "Consultando chamados semelhantes…" é parte do produto. |
| 0013 | Controllers | Minimal APIs | Endpoints finos por construção, com validação nativa do .NET 10. |

**Planejamento:** fechamos a fase com 6 sprints em fatias verticais (Sprint 0 = Walking Skeleton + PoC de IA). Cada sprint termina demonstrável, e há uma linha de corte explícita caso o prazo aperte.

Aprendizado: a pergunta "onde fica a fila?", deixada em aberto no ADR-0003, só foi bem respondida com o modelo de dados na mesa. A resposta (estado da entidade = fila) eliminou uma tabela inteira e um problema de sincronização.

---

## Preparação para a Fase 4: fundação do repositório

**Artefatos:** [`adr/0014`](adr/0014-fluxo-git-trunk-based.md), [`padroes/fluxo-git.md`](padroes/fluxo-git.md), `.github/pull_request_template.md`, `CLAUDE.md`, `README.md`, `.gitignore`, `.gitattributes`, `.editorconfig`

Antes de escrever a primeira linha de código, preparamos o repositório para que o processo fosse **visível** e **reprodutível**:

- **A documentação foi o primeiro commit.** Os requisitos, o ADD, os ADRs e o plano de sprints entraram na `main` antes de qualquer código, então o histórico mostra que o planejamento veio primeiro.
- **O fluxo Git foi decidido antes da primeira branch** (ADR-0014): trunk-based com uma branch por sprint, PR com CI verde, merge commit (para preservar os commits pequenos) e uma tag por sprint. O GitFlow clássico foi descartado como cerimônia excessiva para um dev só em 7 dias. Um template de PR padroniza cada entrega, com os critérios de aceite e a Definition of Done.
- **O README é vivo.** Ele nasce no primeiro commit e ganha uma seção por sprint. Isso virou item da Definition of Done.
- **Aprendizado de ambiente.** O primeiro `git add` no Windows avisou sobre a conversão LF → CRLF. Como tudo roda em contêineres Linux, isso quebraria scripts dentro do Docker. O `.gitattributes` passou a forçar LF no repositório, e os arquivos já commitados foram renormalizados (`git add --renormalize .`).

### Como a IA é usada no desenvolvimento

O uso de assistentes de IA é **governado e documentado**, e não improvisado:

| Ferramenta | Papel | Limites |
|---|---|---|
| **Claude (chat)** | Parceiro de arquitetura nas Fases 1 a 3: requisitos, ADD, ADRs, modelo de dados, contratos e plano | Toda decisão foi revisada e aprovada pelo desenvolvedor; cada ADR compara pelo menos duas alternativas |
| **Claude Code (local, VS Code)** | Implementação, sprint a sprint, seguindo o `CLAUDE.md` | Não faz commit nem push sem pedido explícito; não contraria ADR em silêncio; não adiciona biblioteca sem justificativa |
| **Claude Code (sessões na nuvem)** | Validação em **clone limpo** (a Definition of Done diz "`docker compose up` a partir de um clone limpo"), suíte completa de testes com Docker, auto-fix de CI nos PRs e tarefas paralelas bem delimitadas | 4 modos explícitos no `CLAUDE.md`. Nos modos de validação, não edita nada, só reporta. Nunca envia para a `main`, nunca faz merge. Usa sempre a IA fake: a chave do provedor real **nunca** sai da máquina do desenvolvedor |

O `CLAUDE.md` funciona como o **contrato** entre o desenvolvedor e os assistentes. As mesmas regras de arquitetura, de IA (mascaramento, saída não confiável) e de Git valem para qualquer código, seja escrito à mão ou com assistente.

---

## Revisão de arquitetura por pares: padrões agênticos (30/09)

**Artefatos:** [`revisoes/2026-09-30-padroes-agenticos.md`](revisoes/2026-09-30-padroes-agenticos.md), [`adr/0018`](adr/0018-evals-offline-da-ia.md) a [`adr/0021`](adr/0021-kill-switch-e-orcamentos-de-ia.md), plano de sprints v1.1

Com a Sprint 0 em andamento, um AI Engineer externo compartilhou um catálogo de 25 padrões de design de agentes com um checklist de produção. Em vez de "adicionar o que parecia interessante", o desenho foi **confrontado** com a referência, padrão por padrão:

- **A maior parte já estava coberta.** O princípio central do catálogo ("comece pelo padrão mais simples: workflow antes de agente") é exatamente o ADR-0004. A triagem é um *prompt chain* com gate de validação, e o copiloto é um agente ReAct.
- **9 padrões foram rejeitados com justificativa.** Por exemplo, multiagente consome cerca de 15× os tokens de um chat, sem subtarefas separáveis que o justifiquem.
- **4 lacunas foram adotadas**, cada uma com ADR:
  - evals offline (ADR-0018);
  - tracing com OpenTelemetry (ADR-0019);
  - guardrail de saída do copiloto (ADR-0020);
  - kill switches e orçamentos de tokens (ADR-0021).
- **1 item ficou opcional:** uma nova tentativa corretiva na triagem.

**Por que agora, e por que não virou uma sprint extra no final:** nenhuma das mudanças afeta código já escrito (a Sprint 0 não mudou), então o custo de adotar era zero. Cada item foi encaixado na sprint em que o código relacionado nasce: tracing na 2, evals na 3, guardrail na 4. Uma sprint no final seria a primeira a ser cortada. Os evals passaram a ficar **acima** do copiloto na linha de corte.

**Aprendizado:** a lacuna mais importante não era de funcionalidade, e sim de **medição**. Sem evals, o RAG seria "uma funcionalidade implementada". Com evals comparando sem RAG × com RAG, ele vira "uma funcionalidade cujo efeito foi medido".

---

## Fase 4 — Walking Skeleton (Sprint 0)

**Artefatos:** código em `src/`, `tests/` e `web/`; `Dockerfile`, `docker-compose.yml`, `.github/workflows/ci.yml`, `scripts/smoke-compose.sh`; [`adr/0015`](adr/0015-migrations-em-servico-one-shot.md), [`adr/0016`](adr/0016-logs-estruturados-nativos.md), [`adr/0017`](adr/0017-ui-kit-mantine.md), [`adr/0022`](adr/0022-ci-com-smoke-do-compose.md) e [`adr/0023`](adr/0023-segredos-em-env-local.md); resultado da PoC nos ADRs [0005](adr/0005-abstracao-provedor-llm.md) e [0011](adr/0011-estrategia-de-embeddings.md).

**O que foi entregue:** a arquitetura completa funcionando de ponta a ponta com o mínimo de funcionalidade.

- O `docker compose up`, num clone limpo e sem chave, sobe os 5 serviços na ordem certa.
- `/health` verifica o banco e devolve 503 quando ele cai.
- Logs JSON com correlation id e ProblemDetails no formato do contrato.
- Casca web responsiva carregando as categorias pela API.
- CI com três jobs (backend, frontend e smoke do Compose).
- 35 testes de backend e 9 de frontend.

**Como foi feito:** 15 commits pequenos, um por vez, cada um com build e testes rodados antes da mensagem de commit. Toda decisão de plataforma teve duas alternativas apresentadas **antes** do ADR ser escrito. Todo pacote fora da lista aprovada foi justificado antes de entrar.

### O que mudou em relação ao plano

- **Um Dockerfile com três alvos** (api, worker e migrator) em vez de três Dockerfiles quase iguais. Restore e compilação acontecem uma vez só.
- **Dois ADRs de plataforma a mais** que o previsto (CI e segredos), numerados 0022 e 0023, porque os números 0018 a 0021 já tinham sido usados pela revisão de 30/09.
- **"CA extra" opcional no build**, que não estava no plano. A máquina de desenvolvimento fica atrás de um DLP corporativo (Acronis DeviceLock) que reassina o HTTPS, e os contêineres de build não confiam nessa CA. A solução é um *build secret* opt-in: sem configuração, nada muda, e o certificado não entra nas imagens finais.
- **O CI virou a fonte oficial da validação em clone limpo.** A sessão do Claude Code na nuvem, prevista para isso, roda atrás de um proxy que também inspeciona TLS e bloqueia o download do .NET, então não consegue compilar o backend. O job de smoke do CI faz exatamente o papel dela: clone limpo, sem `.env`, a cada push.
- **Modelo de IA padrão:** `gemini-3.5-flash-lite`, escolhido pelas cotas medidas, e não o Flash "maior". Ver a PoC abaixo.

### PoC de IA: o risco que justificou a Sprint 0

O plano dizia "melhor descobrir no dia 2 do que no dia 6", e foi exatamente o que aconteceu.

- **Saída estruturada e embeddings funcionaram de primeira.** Um detalhe: o vetor reduzido para 768 dimensões **não vem normalizado** (norma ≈ 0,59), o que confirmou que a normalização do ADR-0011 é obrigatória.
- **O tool calling falhou.** Os modelos Gemini 3 devolvem a chamada de ferramenta com uma *thought signature* e exigem recebê-la de volta. O SDK da OpenAI descarta esse campo, e a segunda rodada dá HTTP 400. O diagnóstico foi feito à mão, via REST (sem a assinatura → 400; com a assinatura → OK). O plano B escolhido mantém a arquitetura: uma `PipelinePolicy` no adaptador guarda e reinjeta a assinatura. O protótipo passou na PoC; a versão definitiva entra na Sprint 4.
- **A cota do free tier mudou a escolha do modelo.** Os modelos Flash têm **20 requisições por dia**, e a cota acabou durante a própria PoC. Os Flash Lite têm 500. A latência variou de 5 a 21 s, com 429 e 503 intermitentes, e o timeout padrão subiu de 15 para 60 s.

### Aprendizados (e bugs que só apareceram rodando de verdade)

- **O Npgsql cria enums em ordem alfabética.** O `MapEnum` sozinho gerava `('alta','baixa','critica','media')`, o que quebraria o `ORDER BY prioridade` (P-07). A correção foi declarar os rótulos explicitamente, e um teste de integração verifica a ordem dos três enums.
- **O CI pegou um bug que o Windows escondia.** O teste de arquitetura lia os `ProjectReference` com `\`, que não é separador no Linux. Na máquina de desenvolvimento ficava verde; no runner, vermelho. A correção foi reproduzida e validada num contêiner Linux antes do push. *(Por descuido, ela entrou no mesmo commit da PoC, e não num `fix:` próprio.)*
- **Um arquivo necessário ao Compose estava sendo ignorado pelo `.gitignore`** (o placeholder vazio da CA, pego pela regra `*.pem`). Funcionaria na máquina local e falharia em qualquer clone limpo. Isso reforçou o valor do job de smoke no CI.
- **As versões mais novas trazem armadilhas.**
  - O TypeScript 7 ainda não é suportado pelo `typescript-eslint` (ficamos no 6).
  - O MSW 3 renomeou `onUnhandledRequest` para `onUnhandledFrame`. Com o nome antigo, o modo estrito era ignorado em silêncio, e só o `tsc` do build denunciou.
  - O jsdom 30 exige Node 22.22+ ou 24, o que levou o projeto para o Node 24 LTS.
- **A imagem Alpine tem diferenças em relação à máquina local.** O Npgsql tenta Kerberos por padrão e a imagem não tem a `libgssapi`, o que gerava um aviso fora do JSON a cada conexão; foi desligado na connection string. O SDK Alpine também não traz o `update-ca-certificates`.
- **Testes que passam pelo motivo errado.** Duas verificações passavam mesmo com o sistema quebrado: o modo estrito do MSW (opção com nome errado) e a checagem de logs do smoke test (ID fixo que existia de uma execução anterior). As duas foram descobertas provocando a falha de propósito. Desde então, cada teste novo é "quebrado" uma vez para provar que detecta o problema.
