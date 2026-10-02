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

---

## Sprint 1 — Chamados de ponta a ponta

**Artefatos:** domínio em `src/HelpDesk.Domain/Chamados/`; migration `Chamados` (tabelas, `CHECK`s, índices 1 a 7 e `f_unaccent`); seed em `src/HelpDesk.Infrastructure/Persistencia/Seed/`; casos de uso em `src/HelpDesk.Application/Chamados/`; endpoints em `src/HelpDesk.Api/Endpoints/ChamadosEndpoints.cs`; telas em `web/src/paginas/`; [`DECISOES.md`](../DECISOES.md#decisões-de-implementação-sprint-1).

**O que foi entregue:** o ciclo completo de um chamado, sem IA.

- Criar, com validação no cliente e no servidor.
- Listar, com filtros na URL, busca sem acento, ordenação e paginação.
- Detalhar, com comentários e histórico.
- Mudar status, só pelas transições permitidas.
- Comentar.
- Tudo com concorrência otimista (`ETag`/`If-Match` → 412).
- 200 chamados de demonstração no seed.
- Testes: de 35 para 203 no backend, de 9 para 31 no frontend, e o smoke do Compose cresceu de 8 para 12 verificações.

**Como foi feito:** 13 commits, na ordem domínio → banco → seed → um endpoint por commit → smoke → uma tela por commit → documentação. Cada commit levou build, testes e lint rodados antes da mensagem. Os únicos pacotes novos foram os já aprovados (Bogus, React Hook Form, Zod e `@mantine/notifications`) mais o `@hookform/resolvers`, justificado e aprovado antes de entrar.

### O que mudou em relação ao plano

- **Uma divergência entre documentos foi pega antes do código.** O modelo de dados dizia que uma escrita concorrente detectada pelo `xmin` gerava **409**, enquanto o contrato e o critério de aceite diziam **412**. A decisão foi um código só, o 412, para os dois caminhos: `If-Match` velho e corrida no `SaveChanges`. O modelo foi alinhado ao contrato.
- **O detalhe entrou junto com a criação, e não no próprio commit.** O contrato manda o 201 devolver o detalhe completo, então a consulta do detalhe nasceu no `POST`, e o commit do `GET /{id}` ficou só com o endpoint, o 404 e os testes.
- **O seed passa pelo domínio.** Em vez de montar linhas "coerentes" à mão, o gerador chama `Abrir`, `MudarStatus` e `Comentar` em ordem cronológica. Se o seed tentasse algo proibido, o próprio domínio recusaria.
- **Os placeholders da Sprint 0 saíram.** A página "Bem-vindo" e o painel de categorias deram lugar à lista de chamados.
- **O campo `triagem` do detalhe e o `triagemStatus` da lista ficaram para a Sprint 2**, junto com a tabela `triagens_ia`. O contrato registra a entrega incremental.

### Aprendizados

- **O `EXPLAIN` com 200 linhas escolhe *Seq Scan*.** É o comportamento certo do planner com tabela pequena, mas deixa o teste do índice sem prova. Com o `seqscan` desligado, o teste roda o `EXPLAIN` da **consulta que o próprio EF gera** (via interceptor), e não de um SQL escrito à mão. O teste também mostrou que, na consulta da página, o planner pode preferir o índice 1, que já entrega a ordem com `LIMIT`. Isso é legítimo; por isso o teste afirma o uso do trigram na consulta de filtro.
- **O mesmo teste achou SQL ruim que ninguém tinha pedido para olhar.** O nome da categoria com `FirstOrDefault` virava uma janela (`WindowAgg`) sobre a tabela inteira de categorias. Uma subconsulta escalar por PK resolveu.
- **O `JsonStringEnumConverter` aceita números por padrão.** `"prioridade": 2` passava em silêncio. Agora dá 400, e há teste para isso.
- **Comentar também muda a versão do chamado.** O `xmin` muda porque `atualizado_em` muda, então o `POST` de comentário devolve o novo `ETag`. Sem isso, comentar e logo depois mudar o status daria um 412 falso.
- **O `If-Match` usa comparação forte (RFC 9110).** Um ETag fraco (`W/"..."`) nunca confere e resulta em 412; um teste cobre o caso.
- **O tamanho do texto é contado em caracteres Unicode, como o `char_length` do PostgreSQL.** Contando em UTF-16, um título de 5 emojis passaria na API e cairia no `CHECK` do banco com erro 500.
- **Ferramentas de teste no front.** O jsdom não tem `document.fonts`, que o `Textarea` com autosize usa, e precisou de polyfill. No MSW, o handler registrado **por último** tem precedência, o que inverteu um cenário de erro até a ordem ser corrigida.
- **O bundle passou de 500 kB minificado** (~160 kB com gzip) depois do Zod. Fica registrado para o *code-splitting* por rota na Sprint 5, e não para agora.

---

## Sprint 2 — Triagem por IA (sem RAG)

**Artefatos:** domínio `src/HelpDesk.Domain/Triagem/`; mascaramento, prompt, validador e pipeline em `src/HelpDesk.Application/Triagem/`; [`prompts/triagem.v1.md`](../prompts/triagem.v1.md); provedores, resiliência e telemetria em `src/HelpDesk.Infrastructure/Ia/`; fila em `src/HelpDesk.Infrastructure/Persistencia/FilaTriagem.cs`; consumidor em `src/HelpDesk.Worker/`; painel em `web/src/componentes/PainelTriagem.tsx`; [`adr/0024`](adr/0024-retry-no-cliente-de-chat.md).

**O que foi entregue:** todo o requisito obrigatório de IA.

- A triagem é **assíncrona**: o chamado nasce com a triagem `Pendente` na mesma transação, e a criação responde em menos de 300 ms mesmo com um provedor de 30 s.
- O processamento é **resiliente**: fila com `SKIP LOCKED` e lease, timeout e retry com backoff, `Retry-After`.
- A saída é **validada**: parse tolerante, schema e domínio; qualquer falha vira `Falhou` com motivo.
- A triagem segue a **LGPD**: o mascaramento é garantido pelo tipo e provado por um *spy* no lugar do provedor.
- A triagem é **observável**: `uso_llm` por tentativa e traces OpenTelemetry sem conteúdo, com o painel opcional.
- A triagem tem **kill switch**: `IA_TRIAGEM_HABILITADA`, `/api/config/ia`, 503 no refazer e `Degraded` no `/health`.
- O painel no front tem o selo "Gerado por IA" e as ações aceitar, rejeitar e refazer.
- Testes: de 203 para 451 no backend, de 31 para 41 no frontend, e o smoke do Compose cresceu de 12 para 16 verificações.

**Como foi feito:** 16 commits, na ordem domínio → mascaramento → banco → criação com a triagem → prompt e validador → provedores → resiliência → telemetria → pipeline → Worker → endpoints → tracing → smoke → front → correção → documentação. Duas decisões foram tomadas antes de codar, com duas alternativas cada: onde ficam as novas tentativas (virou o ADR-0024) e a etapa "Recuperar" vazia até o RAG.

### O que mudou em relação ao plano

- **Onde ficam as novas tentativas virou um ADR.** Os ADRs 0003/0010 (tentativas pela fila) e 0005 (resiliência no adaptador) eram ambíguos juntos, e fazer as duas coisas multiplicaria as chamadas e a cota. Ficou no cliente de chat ([ADR-0024](adr/0024-retry-no-cliente-de-chat.md)).
- **Lacunas do modelo de dados preenchidas:** `motivo_rejeicao` (o contrato aceitava um motivo sem ter onde guardá-lo), `trace_parent` (o ADR-0019 pedia, o modelo não tinha), `provedor`/`modelo`/`prompt_versao` nulos enquanto pendente e o `xmin` na triagem para decisões simultâneas.
- **Os 200 chamados do seed continuam sem triagem.** Pendentes no seed seriam processadas na subida, e com o Gemini isso consumiria a cota diária de uma vez. As triagens do seed, com status variados para o dashboard, entram na Sprint 3.
- **A validação com o Gemini real foi feita com o Worker fora do contêiner.** Nesta máquina, um DLP corporativo intercepta o TLS e as imagens, por decisão da Sprint 0, não carregam a CA corporativa. A limitação ficou documentada no README; num clone limpo, o contêiner fala com o provedor normalmente.

### Validação com o provedor real

Com `LLM_PROVIDER=openai-compatible` e a chave do Gemini no `.env`, um chamado "VPN cai a cada 10 minutos" (com um telefone na descrição) foi triado em 3,2 s: **Infraestrutura / Média**, confiança 0,95, um resumo objetivo e uma resposta cordial, **sem o telefone**. Foram 752 tokens de entrada e 130 de saída, numa chamada registrada em `uso_llm`.

### Aprendizados

- **Um teste de concorrência que passava pelo motivo errado.** Ao tirar o `SKIP LOCKED` de propósito, o teste "duas instâncias não pegam a mesma triagem" continuou verde: no PostgreSQL, a segunda reserva **espera** a primeira e reavalia as linhas, então não duplica, só bloqueia. O que o `SKIP LOCKED` garante é não esperar. O teste certo segura linhas numa transação aberta e exige que a reserva volte na hora com as outras.
- **O primeiro contato com o provedor real achou um defeito.** Dentro do contêiner, o TLS falhava (DLP), e o SDK entregava isso como `ClientResultException` **sem resposta HTTP** (status 0), que eu tratava como falha definitiva, sem retry. Uma queda de rede é exatamente o caso transitório; a correção foi um commit `fix` próprio, e o log de falha passou a trazer o status HTTP.
- **`varchar(200)` escondia o `CHECK` de 200.** O PostgreSQL recusa o texto longo antes de avaliar o `CHECK`, então a regra nomeada do modelo nunca seria exercitada. O resumo virou `text` + `CHECK`.
- **`EnableSensitiveData` desligado foi verificado, e não só configurado.** O teste com `ActivityListener` injeta um CPF e procura em todos os atributos de todos os spans, inclusive o SQL do Npgsql. Ligar a opção de propósito faz o teste falhar. No caminho, um falso positivo do próprio teste: `gen_ai.output.type=json` é metadado, não conteúdo.
- **Uma imagem oficial com defeito.** A `aspire-dashboard:13.6.0` traz o painel como executável nativo, mas o `entrypoint` dela ainda aponta para um `.dll` que não existe; o contêiner reiniciava em loop. A solução foi sobrescrever o `entrypoint` no Compose, com comentário.
- **Mascaramento conservador tem custo, e ele foi documentado.** Um protocolo `2026-0001` vira `[TELEFONE]` e um sobrenome como "Exemplo" é mascarado no texto todo. Cada falso positivo aceito tem teste próprio.
- **O fake também tem bugs.** "fora do ar para todos os usuários" caía em Acesso/Login por causa da palavra "usuario". Como o fake passa pelo validador real nos testes, o erro apareceu na hora.
- **Testes de `/health` precisaram de banco isolado.** Com a fila no health, as pendentes antigas criadas por outros testes deixavam o check `Degraded` no banco compartilhado.

---

## Sprint 3 — RAG, dashboard e evals

**Artefatos:** artigos e documentos do RAG em `src/HelpDesk.Domain/Conhecimento/` e `src/HelpDesk.Infrastructure/Persistencia/` (`IndiceRag`, `DocumentoRag`); montagem dos documentos e reconciliação em `src/HelpDesk.Application/Conhecimento/`; embeddings (fake e real) e `RecuperadorRag` em `src/HelpDesk.Infrastructure/Ia/`; busca e dashboard em `src/HelpDesk.Infrastructure/Consultas/` (as consultas em `Sql/*.sql`); reconciliador em `src/HelpDesk.Worker/`; [`prompts/triagem.v2.md`](../prompts/triagem.v2.md); tela `web/src/paginas/Dashboard.tsx`; harness em `tools/HelpDesk.Evals/`; conjunto em [`evals/triagem/`](../evals/triagem/LEIAME.md); relatórios em [`docs/evals/`](evals/LEIAME.md).

**O que foi entregue:**

- **RAG na triagem.** Os chamados resolvidos e os 25 artigos do seed são indexados sozinhos na subida, por um reconciliador que também remove os reabertos e reindexa quando o modelo de embedding muda. A busca usa cosseno no pgvector com HNSW, e o painel mostra as fontes da sugestão.
- **Dashboard** obrigatório, com as consultas em SQL explícito e testadas uma a uma com massa controlada.
- **Evals:** um conjunto rotulado de 30 casos, o harness e a **primeira medição real**, que decidiu o prompt padrão com números.
- **Seed** com triagens decididas, para o dashboard nascer com dados.
- **Testes:** de 451 para 590 no backend, de 41 para 50 no frontend, e o smoke do Compose passou de 16 para 18 verificações.

**Como foi feito:** 14 commits, na ordem banco → seed → embeddings → montagem dos documentos → reconciliador → busca → prompt v2 e fontes → painel → dashboard (API e tela) → conjunto → harness → medição → documentação. Antes de começar, quatro pontos foram decididos com o desenvolvedor: o harness usa o banco do Compose com RAG; a nova tentativa corretiva (L5) fica de fora; a medição roda sem que a chave seja lida; o reconciliador roda mesmo com a triagem desligada.

### O que mudou em relação ao plano

- **Uma coluna a mais em `documentos_rag`** (`origem_atualizada_em`), para o reconciliador não recalcular o hash de todos os resolvidos a cada passada. Está registrada como ajuste no modelo de dados.
- **A coluna `fontes` entrou com o prompt v2**, e não com as tabelas do RAG: sem quem a preenchesse, seria uma coluna morta por quatro commits.
- **A versão do prompt virou configuração.** O ADR-0018 pede que uma versão nova passe pelo eval antes de virar padrão; para isso, a v2 precisou existir sem ser a padrão, e o eval a promoveu.
- **A v2 virou a padrão mesmo piorando a prioridade.** O eval não deu uma vitória limpa: categoria 100%, prioridade de 91% para 87%. A decisão foi do desenvolvedor, com o trade-off escrito em [`docs/evals/LEIAME.md`](evals/LEIAME.md), e a regra de prioridade virou o alvo da próxima versão.

### Validação com o provedor real

O eval rodou no Gemini: 180 triagens, 90 por versão, sem nenhuma falha que não fosse recuperada. Para a v2, o índice precisou ser refeito com o `gemini-embedding-001`: o Worker rodou no host (por causa da inspeção TLS da máquina) com a triagem desligada, só para reindexar. No meio da reindexação, o Gemini respondeu **429** e a resiliência nova dos embeddings repetiu com backoff até terminar: o primeiro uso real dela.

### Aprendizados

- **Um teste de concorrência achou um deadlock real.** Dois reconciliadores sincronizando o mesmo artigo travavam o índice único em ordens diferentes, porque o EF grava os INSERTs pela chave e Guids v7 gerados no mesmo milissegundo não têm ordem garantida. A trava por origem (`pg_advisory_xact_lock`) resolveu sem perder o paralelismo entre origens diferentes.
- **O fake de chat lia "categorias" demais.** Ele tratava como categoria toda linha `- …` do prompt; a v2 tem listas antes das categorias, e o fake passou a devolver uma "categoria" inexistente quando nenhuma palavra-chave casava (8 falhas em 30 no harness). Sem o harness, o problema só apareceria no ambiente de demonstração.
- **`0,10` virava 10.** O parser do preço por milhão de tokens aceitava a vírgula como separador de milhar: um custo informado no formato brasileiro sairia cem vezes maior. Um teste de argumento inválido pegou.
- **Uma resposta que nunca termina quebra os testes seguintes.** No front, o `delay('infinite')` do MSW deixava o interceptador num estado em que a requisição de outro teste escapava para a rede real ("fetch failed"), de forma intermitente. A prova veio rodando sem o teste suspeito: 0 falhas em 8.
- **O relógio do contêiner não é o da máquina.** Um teste criava a triagem com `DateTimeOffset.UtcNow`, e o banco do Testcontainers estava cerca de 1 s atrás: para a fila, a triagem estava "no futuro". Os testes que envolvem a fila usam data fixa.
- **Comando em segundo plano com `&` sobrevive.** Um smoke disparado assim continuou rodando em paralelo e derrubou outras execuções com o próprio `down -v`. Os smokes passaram a rodar num projeto Compose separado (`helpdesk-smoke`), sem tocar no ambiente do desenvolvedor.
- **Um teste de SQL que passava por acaso.** Ao tirar de propósito o filtro de status da consulta de aceitação, o teste continuou verde: na massa, o `JOIN` já excluía as pendentes. Uma triagem concluída e não decidida na massa tornou a regra observável.
- **Medir mudou a conversa.** "O RAG melhora a triagem?" virou uma tabela: melhora a categoria, piora a prioridade num padrão identificável, dobra os tokens. A próxima versão do prompt já tem alvo e régua.

---

## Sprint 4 — Copiloto conversacional

**Artefatos:** prompt versionado em [`prompts/copiloto.v1.md`](../prompts/copiloto.v1.md); ferramentas, guardrail e caso de uso em `src/HelpDesk.Application/Copiloto/` (`ConversarComCopiloto`, `MontadorPromptCopiloto`, `FiltroSaidaCopiloto`, `FerramentasCopiloto`); adaptador de LLM e fake em `src/HelpDesk.Infrastructure/Ia/` (`CopilotoLlm`, `FakeCopiloto`); endpoint e rate limiting em `src/HelpDesk.Api/Endpoints/CopilotoEndpoints.cs` e `Program.cs`; proxy sem buffer em `web/nginx.conf`; cliente SSE e painel em `web/src/api/copiloto.ts` e `web/src/componentes/PainelCopiloto.tsx`; smoke test atualizado em `scripts/smoke-compose.sh`.

**O que foi entregue:**

- **Agente com ferramentas (tool calling / ReAct):** o modelo decide quando chamar ferramentas para embasar a resposta (até 3 rodadas), ligado ao prompt versionado `copiloto.v1.md`.
- **4 ferramentas somente leitura:** busca de chamados similares (pgvector), busca de artigos na base de conhecimento, métricas da categoria e histórico do chamado. A ferramenta de histórico é *poka-yoke*: não aceita parâmetro de ID, garantindo que o modelo nunca consulte o histórico de outro chamado.
- **Recusa de escrita:** o copiloto ajuda a entender e decidir, mas recusa pedidos de ação ("feche o chamado", "mude a prioridade") e direciona o usuário para a interface.
- **Streaming SSE nativo:** endpoint `POST /api/chamados/{id}/copiloto` usando `TypedResults.ServerSentEvents` do .NET 10, com eventos tipados (`ferramenta`, `delta`, `fontes`, `aviso`, `fim`). O Nginx desativa o buffering para entrega incremental em tempo real.
- **Guardrail de saída (ADR-0020):** buffer de retenção que impede vazamento de dados pessoais mesmo com CPF dividido entre pacotes de rede, e verificação de citações contra os resultados retornados pelas ferramentas. Citação inventada produz evento `aviso` e selo de referência não verificada.
- **Proteção de cota e Kill Switch (ADR-0021):** rate limiter nativo por IP (10 req/min, 429 com `Retry-After`), orçamento `COPILOTO_MAX_TOKENS_SAIDA` (resposta truncada) e kill switch `IA_COPILOTO_HABILITADO` (503 na API, esconde o painel na interface).
- **Interface web interativa:** renderização incremental, indicadores de ferramentas em tempo real ("Consultando…"), fontes verificadas com links diretos, selos visuais de alerta e botão Parar com cancelamento via `AbortController`.
- **Testes:** de 590 para 689 no backend (446 unitários, 237 de integração e 6 de arquitetura), de 50 para 62 no frontend, e o smoke do Compose passou de 18 para 19 verificações.

**Como foi feito:** 10 commits na sequência planejada: ferramentas (unit) → consultas (integração) → guardrail de saída (unit) → telemetria e resiliência (unit) → fake roteirizado (unit) → prompt e caso de uso (unit) → endpoint SSE e rate limiter (integração) → cliente SSE (vitest) → painel no detalhe (vitest) → smoke do compose.

### O que mudou em relação ao plano

- **`TypedResults.ServerSentEvents` nativo do .NET 10 adotado diretamente:** evitou bibliotecas externas de SSE no backend, gerando `SseItem<object>` tipado com overhead zero.
- **Reafirmação do `Activity.Current` no enumerador assíncrono:** em iteradores assíncronos (`yield return`), o contexto de atividade do OpenTelemetry pode se perder após o `MoveNextAsync`. Reafirmar a atividade garantiu que as ferramentas fiquem aninhadas sob o span `copiloto.responder`.
- **Um modo novo no fake:** `vaza_dados`, em que a resposta "vaza" um CPF dividido entre pedaços e cita um chamado inexistente, para testar o guardrail de saída sem provedor real. A recusa de escrita não precisou de modo: é o comportamento normal do fake diante de um pedido de ação.
- **A revisão antes do PR achou três problemas**, todos corrigidos num commit próprio (`965f05f`): a API, que agora roda o copiloto, não recebia as variáveis de LLM no compose (com o provedor real, ela não subiria); o 429 usava um código fora do contrato (`limite_de_requisicoes` em vez de `limite_excedido`); e a verificação do copiloto no smoke falhava no CI (perguntava sobre um chamado sem semelhantes) e no Windows (o `curl` recodificava o acento da pergunta).

### Aprendizados

- **Testes de mutação garantem a efetividade dos testes:** quebrar intencionalmente o código (remover filtro de texto, não repassar o número do chamado no contexto, estourar exceção sem devolver `{ erro }` ou remover `Activity.Current`) fez os testes falharem exatamente nos pontos esperados, comprovando que a suíte não tem testes passando por acaso.
- **Restrição do compilador em iteradores (`yield` dentro de `catch`):** a regra CS1631 proíbe `yield return` diretamente em blocos `catch`. Capturar a exceção e emitir o `event: erro` fora do bloco de tratamento resolve o fluxo de erro de streaming de maneira limpa.
- **Buffering em proxies quebra o streaming:** proxies reversos acumulam pacotes por padrão; a diretiva `proxy_buffering off` e `proxy_cache off` no Nginx é indispensável para que o Server-Sent Events entregue deltas imediatamente ao navegador.


---

## Sprint 5 — Hardening e entrega (Fase 5: padrões de engenharia)

**Artefatos:** E2E em [`web/e2e/`](../web/e2e/) com [`web/playwright.config.ts`](../web/playwright.config.ts); comando único [`scripts/testes.sh`](../scripts/testes.sh); união da cobertura em [`scripts/cobertura.mjs`](../scripts/cobertura.mjs); padrões em [`docs/padroes/`](padroes/LEIAME.md); [ADR-0025](adr/0025-icu-nas-imagens-dotnet.md); tema com contraste AA em `web/src/tema.ts`; rotas com `lazy` em `web/src/rotas.tsx`.

**O que foi entregue:**

- **E2E com Playwright**, no CI, contra o compose de pé: o fluxo do enunciado (criar → ver a triagem → aceitar), o copiloto citando chamados com fontes clicáveis e as quatro telas sem rolagem horizontal em 375 px.
- **Dois bugs de produção corrigidos**, ambos encontrados pelo E2E (detalhes nos aprendizados).
- **Acessibilidade:** auditoria com o axe-core; o contraste de cor, único problema encontrado, foi corrigido no tema, e as telas auditadas ficaram com zero violações do WCAG 2.1 AA.
- **Desempenho do front:** o pacote inicial caiu de 1.112 kB para 429 kB, com o dashboard, o formulário e o detalhe carregados sob demanda.
- **Cobertura medida** no CI: 96,2% das linhas no backend (os três projetos de teste unidos) e 90,9% no frontend.
- **Um comando para todos os testes** (`bash scripts/testes.sh`), com um modo que sobe um compose isolado para o smoke e o E2E.
- **Padrões de engenharia** para o time: guia de testes, convenções de código, checklist de revisão e fluxo de ADR, ao lado do fluxo Git.
- **README fechado** contra a seção 8 do enunciado, com "o que ficaria para uma próxima versão", o uso de assistentes de IA e um mapa de onde cada item está atendido.
- **Testes:** o backend segue com 689; o frontend com 62; o smoke passou de 19 para 20 verificações; e o E2E nasceu com 6.

**Como foi feito:** 12 commits. O plano previa 10; o E2E encontrou dois bugs no caminho, e cada correção virou um commit próprio, com o teste que a reproduzia antes da correção e passava depois.

### O que mudou em relação ao plano

- **Dois `fix` entraram no meio da sprint.** O E2E era para provar o fluxo; acabou sendo a camada que achou os únicos bugs de produção da sprint.
- **Um ADR novo (0025)** para o ICU nas imagens, com duas alternativas medidas: instalar o ICU (+58 MB por imagem) ou reescrever a remoção de acentos sem depender dele.
- **A auditoria de acessibilidade usou o axe-core sem torná-lo dependência.** Rodou de uma pasta temporária, nas telas do compose; mantê-lo no CI ficou como próxima versão.
- **O E2E aceita um navegador instalado** (`E2E_NAVEGADOR=msedge`), porque a máquina de desenvolvimento não consegue baixar o Chromium do Playwright atrás da inspeção TLS; o CI usa o Chromium normalmente.
- **A divisão do bundle**, pendência da Sprint 3, entrou como o primeiro commit de código da sprint.

### Aprendizados

- **O E2E testou o que nenhuma outra camada testava: o caminho do usuário.** Os 689 testes do backend e os 62 do front passavam, e o smoke também; mesmo assim, aceitar a sugestão da IA pelo navegador não funcionava.
- **O Nginx enfraquecia o `ETag`.** Ao comprimir o JSON da API com gzip, ele troca o `ETag` forte por um fraco (`W/"..."`), como manda o RFC; o `If-Match` usa comparação forte, e toda escrita pelo navegador respondia 412. O smoke não via porque o `curl` não pedia gzip, e o front testa com MSW, sem Nginx. Correção: sem gzip nas rotas da API. E uma lição para o smoke: teste com os cabeçalhos que o usuário manda.
- **As imagens Alpine do .NET rodam sem ICU.** Em globalização invariante, `Normalize(FormD)` não remove acentos e `IgnoreNonSpace` não ignora: em produção, "Não consigo" não era reconhecido pelo fake, e o mascarador não casaria "João" com "Joao". Os testes rodam no Windows e no runner do CI, que têm ICU, então nunca veriam. A reprodução numa imagem Alpine com o modo invariante isolou a causa em minutos. Lição: o ambiente de teste precisa se parecer com o de produção onde importa, ou um teste precisa rodar no ambiente de produção.
- **Contraste não se vê a olho.** O cinza das legendas e o indigo da marca pareciam legíveis e estavam abaixo de 4,5:1. A correção foi no tema, num lugar só, com os contrastes calculados antes de escolher os tons.
- **Unir relatórios exige a mesma chave.** A primeira versão do script de cobertura contava cada arquivo duas vezes, porque cada relatório usava uma raiz diferente, e dava um total menor que o de uma suíte sozinha. O sinal estava no próprio número: a união não pode ser menor que uma das partes.
- **A paleta de cores padrão não é acessível por padrão.** Os tons 6 da Mantine, usados em quase tudo, não passam de 4,5:1 sobre branco na maioria das cores; `autoContrast` e tons mais escuros resolvem.

---

## Sprint 6 — Autenticação JWT e Perfis (ADR-0026)

**Artefatos:** [ADR-0026](adr/0026-autenticacao-jwt-com-usuarios-do-seed.md); entidade `Usuario` e enum `PerfilUsuario` em `src/HelpDesk.Domain/Usuarios/`; caso de uso `EntrarNoSistema` e porta `IHashSenha` em `src/HelpDesk.Application/Autenticacao/`; hash PBKDF2 em `src/HelpDesk.Infrastructure/Seguranca/HashSenhaPbkdf2.cs`; emissão do JWT e políticas (`PoliticaAtendente` e a política de fallback "autenticado") em `src/HelpDesk.Api/Autenticacao/` (`EmissorToken`, `ConfiguracaoAutenticacao`); endpoints `POST /api/auth/login`, `GET /api/auth/eu` e `POST /api/auth/sair` em `src/HelpDesk.Api/Endpoints/AutenticacaoEndpoints.cs`; migration `Usuarios` e seed em `src/HelpDesk.Infrastructure/Persistencia/Seed/GeradorSeedUsuarios.cs`; filtro pelo e-mail do solicitante em `ConsultaChamados`; tela de login `web/src/paginas/Entrar.tsx`, rotas protegidas em `web/src/componentes/RotaProtegida.tsx` e sessão em `web/src/api/autenticacao.ts`; fluxo E2E do solicitante em `web/e2e/solicitante.spec.ts`.

**O que foi entregue:**

- **Autenticação robusta por Cookie HttpOnly:** JWT assinado com HMAC-SHA256, encapsulado num cookie com `HttpOnly`, `Secure` e `SameSite=Strict`, válido por 8 horas. Imune a ataques de roubo de token via XSS no frontend e compatível com clientes sem navegador via cabeçalho `Authorization: Bearer`.
- **Perfis de usuário estritos (`Atendente` e `Solicitante`):**
  - **Atendente:** acesso completo à triagem por IA, copiloto conversacional, transições de status da máquina de estados, dashboard executivo e abertura de chamado em nome de solicitantes.
  - **Solicitante:** visão restrita e personalizada. Na API e na interface, visualiza e consulta apenas os chamados cujo e-mail do solicitante é o da sua conta (sem diferenciar maiúsculas); o chamado de outra pessoa responde 404, e não 403, para não revelar que existe. O formulário de abertura de chamado omite campos de contato (vinculados diretamente à sessão ativa), o menu omite o dashboard, e o detalhe do chamado oculta painéis de IA e botões de alteração de status, mantendo acesso a leitura e envio de comentários.
- **Identidade confiável e inviolável:** remoção total de campos de identidade do corpo das requisições (`alteradoPor`, `autor`, `decididaPor`). O backend extrai a identidade unicamente do `ClaimsPrincipal` assinado, impedindo qualquer falsificação de autoria de comentários, alterações de status ou decisões de triagem.
- **Segurança de credenciais:** senhas com salt criptográfico de 16 bytes e hash PBKDF2 com HMAC-SHA256 em 600.000 iterações, comparadas em tempo constante; um e-mail inexistente também calcula um hash, para o tempo de resposta não revelar quais contas existem.
- **Testes:** a suíte do backend cresceu de 689 para 780 testes (482 unitários, 292 de integração e 6 de arquitetura); o frontend passou de 62 para 70 testes no Vitest; os testes E2E com Playwright aumentaram de 6 para 8 cenários, cobrindo o fluxo do solicitante e autenticação; e o script de smoke do compose foi atualizado para operar autenticado.

**Como foi feito:** 15 commits, do planejamento (`docs: planeja a Sprint 6 e decide a autenticação (ADR-0026)`) às correções finais. Os principais:
1. `feat(db): adiciona os usuários com perfis e o seed de demonstração`
2. `feat(api): adiciona o login com JWT em cookie httpOnly`
3. `feat(web): adiciona o login, a sessão e o menu do usuário + smoke e E2E fazendo login`
4. `feat(api): exige autenticação e aplica os perfis`
5. `feat(app): restringe o solicitante aos próprios chamados e documenta usuários do seed`
6. `refactor(api): tira a identidade do corpo das requisições + o front sem o campo 'Seu nome (atendente)' e o formulário por perfil`
7. `test(e2e): cobre o fluxo do solicitante`
8. `fix(infra): lista os chamados do solicitante sem diferenciar maiúsculas no e-mail`, achado na revisão antes do PR: o detalhe e o comentário já ignoravam maiúsculas, mas a lista não, e um chamado aberto pelo atendente com "Marina.Costa@..." sumia da lista dela.

### O que mudou em relação ao plano

- **Inversão planejada entre frontend e backend:** para preservar o princípio inegociável de que *cada commit compila e passa 100% nos testes*, a interface e os testes de integração/E2E com login foram integrados antes de a API fechar os endpoints com 401. Se a API exigisse autenticação antes do frontend ter tela de login, o CI quebraria no meio da branch.
- **Ajustes no script de smoke:** o smoke em bash precisou ser atualizado para realizar login via `curl`, armazenando o cookie em jar temporário para consumir os endpoints protegidos, e as chamadas de transição/triagem tiveram seus payloads ajustados para não mais enviar identidade manual.

### Aprendizados

- **Ordem de introdução de segurança:** introduzir autenticação em projetos com CI estrito exige criar as chaves e mecanismos de autenticação do cliente antes de bloquear os endpoints da API.
- **Segurança defensiva contra falsificação:** confiar na identidade enviada no payload JSON (ex.: `"alteradoPor": "Ana"`) é uma vulnerabilidade clássica. Ao amarrar a autoria ao token JWT assinado, eliminou-se uma superfície inteira de ataque sem adicionar complexidade ao domínio.
- **Cookie HttpOnly vs LocalStorage:** armazenar JWT no `localStorage` expõe a aplicação a vazamento por XSS. O uso de cookie HttpOnly gerenciado pelo navegador, com suporte opcional a `Bearer` para testes de API e scripts, combinou segurança máxima na UI e flexibilidade em integrações.

## Sprint 7 — Design e experiência

**Artefatos:** [análise de experiência](06-analise-de-experiencia.md); layout do detalhe em `web/src/paginas/DetalheChamado.module.css`; hooks `useCelular` e `useTituloDaPagina` em `web/src/hooks/`; `notificarSucesso` e `SemPermissao` em `web/src/componentes/`; ajustes de contraste e largura do conteúdo em `web/src/tema.ts` (com `tema.test.ts`); favicon em `web/public/favicon.svg`; E2E de consistência em `web/e2e/layout.spec.ts`.

**O que foi entregue:**

- **Análise antes do código.** As telas foram percorridas com o Playwright nos dois perfis, em 1366 e 375 px, e o resultado virou um documento com 12 problemas priorizados. Cada commit da sprint resolve um item da lista, e o documento registra o que ficou de fora.
- **Prioridade alta (4):** ações e triagem antes do conteúdo no detalhe em telas pequenas; filtros recolhíveis no celular; notificação de sucesso nas ações cujo resultado não aparece onde o usuário está olhando; "Meus chamados" e um estado vazio próprio para o solicitante.
- **Prioridade média (5):** menu ativo nas sub-rotas; nome e perfil no menu do celular; dashboard com 403 explicado, sem alerta de erro; gráficos e consumo de IA legíveis em 375 px; título da aba por página.
- **Consistência visual,** pedida durante a sprint: a mesma largura de conteúdo na lista, no detalhe e no dashboard; ícones no menu e no cabeçalho (`@tabler/icons-react`, justificada no CLAUDE.md); cantos arredondados no item ativo; favicon.
- **Acessibilidade:** nova auditoria com o axe-core, agora em 38 telas e estados, contra um compose isolado com o banco limpo. Achou dois problemas de contraste que a auditoria da Sprint 5 não viu, porque ela não visitou esses estados: o vermelho dos campos com erro (3,28:1) e o hover da variante "light" dos botões (indigo 4,13:1, red 3,76:1). Ambos foram corrigidos no tema, com zero violações no fim.
- **Testes:** o frontend passou de 70 para 92 testes no Vitest, e o E2E de 8 para 12 cenários. O backend não mudou (sem mudança de contrato).

**Como foi feito:** um commit por item da análise, cada um com os testes de componente afetados, o E2E e uma quebra proposital para provar que o teste detecta o problema.

### O que mudou em relação ao plano

- **O README ficou sem capturas de tela.** O plano pedia "o que mudou na experiência, com as telas"; por decisão do desenvolvedor, a seção descreve as mudanças em texto.
- **Entraram itens fora da análise:** a largura única, os ícones, o favicon e os cantos do menu, pedidos ao ver as telas durante a sprint. Do grupo de baixa prioridade, só o B1 (ícones) foi feito; o cartão inteiro clicável (B2) e a padronização dos botões (B3) ficaram registrados.
- **Um teste instável foi corrigido no caminho:** o primeiro teste de alguns arquivos passava às vezes dos 5 s, porque carregava a página sob demanda a frio. Antes da correção, 2 de 3 rodadas falhavam; depois, 5 de 5 passaram.

### Aprendizados

- **Uma auditoria vale pelos estados que ela visita.** A da Sprint 5 deu zero violações e estava certa para as telas paradas. A da Sprint 7 enviou formulários vazios e passou o mouse nos botões, e encontrou contraste abaixo do mínimo em componentes que existiam desde a Sprint 1.
- **A quebra proposital precisa compilar.** Duas vezes a quebra derrubou o build da imagem, e o E2E rodou contra o contêiner antigo e "passou". Conferir o código de saída do build virou parte da rotina.
- **Ordem do HTML é decisão de UX.** Mover as ações para cima no celular com áreas de CSS grid, e não duplicando o bloco, também mudou a ordem do teclado e do leitor de tela, e deu um teste simples: a ordem no DOM.
- **Dados de teste criados à mão podem quebrar o E2E.** Os dois chamados abertos para a análise tinham "403" no texto e passaram a ser os escolhidos pelo teste do copiloto, sem casos parecidos indexados. O ambiente isolado (`testes.sh --completo`) é a referência; o banco de desenvolvimento, não.

---

## Sprint 8 — Deploy de demonstração

**Artefatos:** [ADR-0027](adr/0027-deploy-de-demonstracao-na-vps.md) (decisão de deploy na VPS com Gemini real e HTTPS); [guia operacional de deploy](deploy-vps.md); tela de login sem credenciais em `web/src/paginas/Entrar.tsx`; suporte a certificados remotos e tolerância à inferência do modelo real em `web/playwright.config.ts` e `web/e2e/triagem.spec.ts`.

**O que foi entregue:**

- **Diferencial §9 atendido na nuvem:** publicação do HelpDesk Inteligente em VPS com Ubuntu 24.04, sob subdomínio próprio e terminação TLS com certificado válido Let's Encrypt (Certbot).
- **IA real ativa (Google Gemini):** o ambiente na nuvem roda com o Gemini 2.5 Flash Lite para chat/triagem e `gemini-embedding-001` para indexação vetorial (RAG) no pgvector, ambos no plano gratuito do Google AI Studio. A triagem processa chamados em ~1,2 s exibindo o modelo real, e o copiloto responde em streaming via SSE citando fontes verificadas.
- **Isolamento de portas e segurança dos segredos:** as portas internas do Docker Compose foram presas estritamente ao `127.0.0.1` (`8085`, `5080` e `55432`), sem exposição pública na internet além do Nginx reverso nas portas 80 e 443. Os segredos (`LLM_API_KEY`, `JWT_CHAVE` e senha do banco) residem exclusivamente no `.env` da VPS com permissão 600, sem transitar pelo Git ou CI (ADR-0023).
- **Invariância do ambiente local:** o repositório principal continua subindo com um único comando (`docker compose up --build`) com IA fake sem `.env`, mantendo o CI 100% verde e determinístico.
- **Validação E2E remota:** a suíte completa de testes Playwright foi executada com sucesso contra o ambiente publicado em HTTPS, validando o fluxo de login, criação de chamado com triagem real pelo Gemini, copiloto e responsividade em 375 px.

### O que mudou em relação ao plano

- **URL privada para avaliadores:** por decisão de segurança e para proteger a cota da IA e a infraestrutura contra tráfego automatizado de robôs, a URL pública não foi fixada no GitHub; o acesso é fornecido diretamente aos avaliadores no processo seletivo.

### Aprendizados

- **Coexistência de projetos em VPS:** usar proxy reverso com redes Docker nomeadas (`docker network connect`) permite que múltiplos sistemas compartilhem a porta 443 com certificados SSL independentes, com custo de infraestrutura zero.
- **Comportamento da IA real nos testes E2E:** enquanto o provedor fake devolve respostas estáticas previsíveis, o modelo real avalia semanticamente o chamado. Testes de ponta a ponta em produção devem validar o contrato de negócio (ex.: categoria atribuída e remoção do estado "Sem categoria") sem engessar a resposta exata da IA.

---

## Fechamento do projeto

### Lições que valem para o próximo

- **Planejar antes de codificar pagou.** Os requisitos, os ADRs, o modelo de dados e os contratos existiam antes da primeira linha de código. Nas sprints, quase toda discussão foi "como", e não "o quê" ou "por quê"; as mudanças de rumo viraram ADRs novos, e não surpresas.
- **Cada camada de teste pegou algo que as outras não pegavam:** o teste de concorrência achou um deadlock (Sprint 3); o harness de evals achou um fake lendo o prompt errado (Sprint 3); a revisão achou variáveis faltando no compose (Sprint 4); o E2E achou o ETag e o ICU (Sprint 5); os testes de autenticação e perfis pegaram chamados de outros solicitantes vazando na busca (Sprint 6); a auditoria de acessibilidade nos estados de erro e de hover achou contraste abaixo do mínimo (Sprint 7); a execução remota na VPS validou o suporte a certificados TLS e a inferência real com o Gemini (Sprint 8).
- **Quebrar o teste de propósito é barato e revelador.** Ao longo do projeto, essa regra encontrou testes que passavam por acaso (o filtro de status do dashboard, na Sprint 3) e quebras que não compilavam e por isso "passavam" com o binário antigo.
- **IA tratada como componente não confiável funciona.** Tipos que impedem mandar texto cru ao provedor, validação de toda saída, fake determinístico que passa pelo mesmo pipeline, evals antes de trocar o prompt, guardrail no stream e kill switches. Nenhuma dessas peças é sofisticada; juntas, deixam a IA previsível o bastante para produção.
- **Medir mudou decisões.** "O RAG melhora a triagem?" virou uma tabela, e a tabela mostrou o que melhorou, o que piorou e quanto custou.

### O que ficou de fora e por quê

Ficaram de fora, por escolha: a `triagem.v3` (com o critério de adoção fixado antes de medir), os evals do copiloto, o *grounding* por LLM-as-judge, um DLP com NER, o rate limit por atendente autenticado, notificações em tempo real no lugar do polling, feature flags dinâmicas e a auditoria de acessibilidade no CI. Os dois grandes diferenciais previstos inicialmente como futuros — autenticação com perfis (Sprint 6) e deploy de demonstração em nuvem com IA real e HTTPS (Sprint 8) — foram integralmente implementados e entregues. Cada item restante, com o motivo e o gatilho para voltar a ele, está no [README](../README.md#o-que-ficaria-para-uma-próxima-versão). A regra que decidiu os cortes foi a do plano: o obrigatório bem feito primeiro, e cada diferencial só com testes e documentação.
