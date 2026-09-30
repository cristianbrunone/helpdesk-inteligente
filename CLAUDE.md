# CLAUDE.md

Diretrizes para assistentes de IA (Claude Code) que trabalham neste repositório. Leia este arquivo inteiro antes de qualquer tarefa.

## Contexto

**HelpDesk Inteligente:** gestão de chamados de suporte com triagem assistida por IA (RAG) e um copiloto conversacional para o atendente. É um teste técnico para uma vaga de **IA Engineer conversacional (sênior)**. O código é avaliado por arquitetura, qualidade de SQL, testes, uso responsável de IA e UX.

O projeto foi **planejado antes de ser codificado**. As decisões já tomadas estão documentadas e **devem ser seguidas**:

| Documento | Para quê |
|---|---|
| `docs/00-enunciado.md` | Enunciado original. É a fonte da verdade dos requisitos. |
| `docs/01-requisitos.md` | RF, RN, NFR e premissas (P-xx), com IDs. |
| `docs/02-add.md` | Arquitetura: C4, módulos, camadas, fluxos, topologia. |
| `docs/03-modelo-de-dados.md` | ER, constraints, índices justificados, SQL do dashboard, seed. |
| `docs/04-contratos-api.md` | Endpoints, payloads, catálogo de erros, eventos SSE. |
| `docs/05-sprints.md` | Escopo, critérios de aceite e testes de cada sprint. |
| `docs/adr/` | Decisões (ADRs). **Antes de implementar algo, leia o ADR relacionado.** |
| `docs/JORNADA.md` | Narrativa do projeto por fase. |
| `DECISOES.md` | Índice curto das decisões e premissas (entregável do enunciado). |

## Regras de ouro

1. **Não contrarie um ADR em silêncio.** Se a implementação pedir algo diferente do que um ADR decidiu, **pare e proponha** um novo ADR (modelo em `docs/adr/0000-template.md`, sempre com **duas alternativas**, decisão, trade-offs e gatilho de reavaliação). Depois atualize o `DECISOES.md`.
2. **Escopo da sprint atual.** Implemente só o que está no escopo da sprint em andamento (`docs/05-sprints.md`). Ideias fora do escopo viram nota em "próxima versão", não código.
3. **Obrigatório antes de diferencial.** Em caso de conflito de tempo, siga a linha de corte do `05-sprints.md`.
4. **Nenhum segredo no repositório.** Chaves só no `.env` (que está no `.gitignore`). Mantenha o `.env.example` atualizado a cada variável nova.
5. **Nenhum dado pessoal em logs, prompts ou embeddings** (ver as regras de IA abaixo).
6. **Toda biblioteca nova precisa de justificativa**, porque o enunciado exige indicar quais bibliotecas foram usadas e por quê. Prefira as já listadas abaixo. Se precisar de outra, explique antes de adicionar.

## Stack

- **Backend:** C# / .NET 10, ASP.NET Core Minimal APIs (ADR-0013), EF Core + Npgsql, PostgreSQL com `pgvector`, `pg_trgm` e `unaccent`.
- **IA:** `Microsoft.Extensions.AI` (`IChatClient`, `IEmbeddingGenerator`) + adaptador OpenAI-compatível (Gemini, OpenAI, Ollama...). O padrão é o provedor **fake** (ADR-0005, ADR-0006).
- **Frontend:** React + TypeScript + Vite, React Router, TanStack Query, React Hook Form + Zod.
- **Testes:** xUnit, Testcontainers (PostgreSQL real, **nunca** banco em memória), NetArchTest, Vitest + Testing Library, MSW (mock de API no front) e Playwright (E2E, Sprint 5).
- **Infra:** Docker Compose com `db`, `migrator`, `api`, `worker` e `web`. CI com GitHub Actions.

Bibliotecas aprovadas até agora: Npgsql.EntityFrameworkCore.PostgreSQL, Pgvector.EntityFrameworkCore, Microsoft.Extensions.AI, Microsoft.Extensions.AI.OpenAI, Microsoft.AspNetCore.OpenApi, Swashbuckle.AspNetCore.SwaggerUI (só a UI), Bogus (seed), xUnit, Testcontainers.PostgreSql, NetArchTest.Rules e Shouldly (asserções; **não** use FluentAssertions, cuja v8+ tem licença comercial). As decisões pendentes da Sprint 0 (logs, UI kit do front) serão registradas em ADR.

## Estrutura

```
src/
  HelpDesk.Domain/          entidades, enums, máquina de estados, erros de domínio. SEM dependências.
  HelpDesk.Application/     casos de uso POR FEATURE (Chamados/, Triagem/, Copiloto/, Dashboard/, Conhecimento/),
                            portas (interfaces), pipeline de triagem, mascaramento, TextoMascarado
  HelpDesk.Infrastructure/  EF Core, migrations, Consultas/*.sql, pgvector, filas, provedores LLM (Fake/, OpenAiCompat/)
  HelpDesk.Api/             Endpoints/ (um arquivo por recurso), Erros/, Program.cs
  HelpDesk.Worker/          BackgroundServices (triagem, reconciliador de indexação)
  HelpDesk.Migrator/        aplica migrations + seed e termina (one-shot)
tests/
  HelpDesk.UnitTests/  HelpDesk.IntegrationTests/  HelpDesk.ArchitectureTests/
web/                        frontend (src/api/ isola todo acesso HTTP)
prompts/                    prompts versionados (triagem.v1.md, copiloto.v1.md...)
docs/                       documentação de arquitetura
```

## Regras de arquitetura

- **Regra de dependência:** Domain ← Application ← Infrastructure; os hosts (Api, Worker, Migrator) compõem via DI. O Domain não referencia nada. A Application não referencia EF Core nem SDKs de provedor. Isso é verificado por teste de arquitetura.
- **Sem mediator, sem AutoMapper, sem repositório genérico** (ADR-0002). Os casos de uso são classes simples, o mapeamento é manual e as portas são específicas.
- **A máquina de estados vive só no `Chamado` (Domain).** A API e o front **nunca** replicam as transições: o detalhe devolve `transicoesPermitidas` e `podeComentar`, calculados pelo domínio.
- **Endpoints finos:** binding → caso de uso → `TypedResults`, com no máximo ~10 linhas. Nenhuma regra de negócio no endpoint.
- **Erros:** o domínio lança erros tipados, e um único `IExceptionHandler` os converte em ProblemDetails, conforme o catálogo em `04-contratos-api.md`: 400 requisição malformada · 404 · 409 conflito de estado · 412 `If-Match` · 422 validação.
- **Leitura × escrita:** escritas e detalhe com EF Core; a listagem com projeção (`AsNoTracking` + `Select`); o **dashboard em SQL explícito** em `Infrastructure/Consultas/*.sql` via `Database.SqlQuery<T>` (ADR-0009).
- **Filas = estado das entidades** (ADR-0003, ADR-0010): a triagem pendente é o item da fila (`FOR UPDATE SKIP LOCKED`, lease, backoff). A indexação RAG é feita por um reconciliador. **Não crie** tabela genérica de jobs.
- **Banco:** `snake_case`, UUID v7 (`Guid.CreateVersion7()`), `timestamptz` em UTC, enums nativos do Postgres, constraints conforme `03-modelo-de-dados.md`. Toda mudança de schema é feita por migration versionada. Nunca edite uma migration que já foi commitada.

## Regras de IA (inegociáveis)

- **Mascaramento tipado:** os clientes de LLM e de embedding recebem **`TextoMascarado`**, nunca `string`. Só o `MascaradorDadosPessoais` constrói `TextoMascarado`. O nome e o e-mail do solicitante **nunca** vão para o LLM.
- **A saída do LLM é entrada não confiável:** parse tolerante → schema → validação de domínio (categoria existe, prioridade válida, resumo ≤ 200, confiança 0–1). Se falhar, a triagem fica `Falhou` com motivo. Nunca lance exceção para fora do pipeline.
- **A criação do chamado nunca espera o LLM.** A triagem é sempre assíncrona, no Worker.
- **Triagem = pipeline determinístico** (Mascarar → Recuperar → MontarPrompt → Completar → Validar). **Tool calling só no copiloto** (ADR-0004). As ferramentas do copiloto são **somente leitura**.
- **Prompts ficam em `prompts/*.md`**, com versão (`triagem.v1`). A versão usada é gravada na triagem. Mudou o prompt? Nova versão, não edição.
- **Logs de IA:** registre provedor, modelo, latência, tokens, sucesso/falha e tipo de erro. **Nunca** o conteúdo do prompt ou da resposta.
- **O fake é o padrão** e precisa exercitar o pipeline real (parsing e validação rodam também com o fake). Ele tem modos de falha configuráveis para teste (lento, JSON inválido, categoria inexistente, 429).
- Resiliência: timeout e retry configuráveis por variável de ambiente, com 429/5xx tratados como transitórios.

## Convenções de código

- **Idioma:** o domínio e o negócio em **português** (`Chamado`, `MudarStatus`, `TriagemIA`, `TransicoesPermitidas`). Termos técnicos e de framework ficam como são (`Repository`, `Handler`, `Endpoint`, `DbContext`). A UI, os docs e as mensagens de erro são em **pt-BR**.
- C#: `Nullable` habilitado, `TreatWarningsAsErrors` nos projetos de `src/`, `async` até o fim com `CancellationToken` propagado, `record` para DTOs, classes `sealed` por padrão. Formatação via `dotnet format` (siga o `.editorconfig`).
- TypeScript: `strict`. Nada de `any`. Os componentes não fazem `fetch`: todo acesso passa por `web/src/api/` + hooks do TanStack Query. Os filtros da lista vivem na URL.
- Todo componente de tela trata os estados **carregando / vazio / erro** e funciona em 375 px.

## Testes

- **Todo comportamento novo vem com teste.** Os mínimos por sprint estão em `05-sprints.md`.
- Unitários: sem I/O. Nome no padrão `Metodo_Cenario_ResultadoEsperado`.
- Integração: `WebApplicationFactory` + Testcontainers (imagem `pgvector/pgvector`). **A IA fica sempre no fake.**
- Os testes que usam provedor real levam `[Trait("Category", "ProvedorReal")]` e **não rodam no CI**.
- Antes de dizer "pronto", rode os testes afetados e reporte o resultado real.

## Comandos

```bash
docker compose up --build                  # sobe tudo (IA fake por padrão)
dotnet build                               # build do backend
dotnet test --filter "Category!=ProvedorReal"   # todos os testes do backend (exige Docker)
dotnet format --verify-no-changes          # lint do backend
dotnet ef migrations add <Nome> -p src/HelpDesk.Infrastructure -s src/HelpDesk.Migrator
cd web && npm run lint && npm test && npm run build
```

(Atualize esta seção se os comandos mudarem.)

## Git

Decisão no ADR-0014. O guia completo, com comandos e o ciclo de cada sprint, está em `docs/padroes/fluxo-git.md`.

- **Fluxo:** trunk-based com uma branch curta por sprint (`sprint/0-walking-skeleton`, `sprint/1-chamados`...). Abra PR para `main`, com CI verde obrigatório, e faça **merge commit** (preserva os commits pequenos). Crie uma tag por sprint (`v0.1.0`...).
- **Commits:** Conventional Commits em pt-BR: `tipo(escopo): descrição no imperativo`.
  - Tipos: `feat`, `fix`, `test`, `refactor`, `docs`, `build`, `ci`, `chore`.
  - Escopos: `domain`, `app`, `infra`, `api`, `worker`, `web`, `ia`, `db`, `adr`, `jornada`.
- **Um commit = uma mudança coesa**, que compila e passa nos testes. Não misture refatoração com feature.
- **Não faça commit nem push sem pedido explícito do desenvolvedor.** Ao terminar uma unidade de trabalho, sugira a mensagem de commit.

## Ao terminar uma tarefa

1. Rode o build e os testes afetados.
2. Confira a Definition of Done em `docs/05-sprints.md` §3.
3. Se tomou uma decisão nova, crie o ADR e atualize o `DECISOES.md`.
4. No fim da sprint:
   - atualize o `README.md` com o que a sprint entregou (a seção prevista no escopo da sprint em `docs/05-sprints.md`). O README é documentação viva: cresce a cada sprint e nunca é escrito todo no final;
   - atualize a `docs/JORNADA.md` (o que foi feito, o que foi aprendido, o que mudou em relação ao plano).
