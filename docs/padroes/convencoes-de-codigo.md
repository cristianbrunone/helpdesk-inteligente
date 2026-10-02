# Convenções de código

> A formatação é do `.editorconfig` (verificada pelo `dotnet format`) e do Prettier: não se discute em revisão. Este guia cobre o que a ferramenta não decide.

## Idioma

- Domínio e negócio em **português**: `Chamado`, `MudarStatus`, `TriagemIA`, `TransicoesPermitidas`.
- Termos técnicos e de framework como são: `Repository`, `Handler`, `Endpoint`, `DbContext`.
- UI, documentação, mensagens de erro e commits em **pt-BR**.

## Backend (C# / .NET 10)

- **Camadas:** Domain ← Application ← Infrastructure; os hosts (Api, Worker, Migrator) só compõem via DI. O Domain não referencia nada; a Application não conhece EF Core nem SDKs de provedor (verificado por teste de arquitetura).
- **Sem mediator, sem AutoMapper, sem repositório genérico** (ADR-0002): casos de uso são classes simples, mapeamento é manual, portas são específicas (`IConsultasCopiloto`, não `IRepository<T>`).
- **Endpoints finos:** binding → caso de uso → `TypedResults`, em ~10 linhas. Nenhuma regra de negócio no endpoint.
- **Erros:** o domínio lança erros tipados (`DominioException` e derivados); um único `IExceptionHandler` os converte em ProblemDetails, conforme o catálogo do contrato (400, 404, 409, 412, 422).
- **Estilo:** `sealed` por padrão, `record` para DTOs, `async` até o fim com `CancellationToken` propagado, `Nullable` habilitado e avisos como erro.
- **Datas:** `TimeProvider` injetado, `timestamptz` em UTC. Nunca `DateTime.Now`.
- **IDs:** UUID v7 (`Guid.CreateVersion7()`).
- **Comentários:** explicam o **porquê** (a decisão, o bug evitado, o ADR), não o quê.

## Dados e SQL

- Escrita e detalhe com EF Core; listagem com projeção (`AsNoTracking` + `Select`); dashboard e consultas analíticas em **SQL explícito** (`Infrastructure/Consultas/Sql/*.sql`, ADR-0009).
- Toda mudança de schema é uma **migration nova**; uma migration já commitada nunca é editada.
- Índice novo vem com justificativa no `03-modelo-de-dados.md` (qual consulta ele atende) e, se possível, um teste com `EXPLAIN`.
- Filas são o estado das entidades (ADR-0003, ADR-0010), com `FOR UPDATE SKIP LOCKED`. Nada de tabela genérica de jobs.

## Frontend (React + TypeScript)

- TypeScript `strict`, **sem `any`**.
- Componentes não fazem `fetch`: todo acesso à API passa por `web/src/api/` e hooks do TanStack Query.
- Filtros da lista vivem na **URL** (recarregar e compartilhar reproduzem a consulta).
- O front **não replica regra de negócio**: mostra só as `transicoesPermitidas` que a API devolve.
- Toda tela trata **carregando, vazio e erro**, funciona em **375 px** e mantém contraste WCAG AA (os ajustes de cor ficam no `tema.ts`).
- Formulários com React Hook Form + Zod; os erros 422 da API voltam para os campos.

## IA (inegociáveis)

- **`TextoMascarado`, nunca `string`**, para os clientes de LLM e de embedding. Só o `MascaradorDadosPessoais` o constrói. Nome e e-mail do solicitante nunca vão ao provedor.
- **A saída do LLM é entrada não confiável:** parse tolerante → schema → validação de domínio. Falha vira `Falhou` com motivo; nenhuma exceção sai do pipeline.
- **Prompts versionados** em `prompts/*.md`; mudou o texto, é versão nova (e passa pelo harness de evals antes de virar padrão, ADR-0018).
- **Logs e spans sem conteúdo:** provedor, modelo, latência, tokens, resultado; nunca o prompt, a resposta ou o texto do chamado.
- **Ferramentas do copiloto só leem.** Uma ferramenta de escrita exigiria um ADR novo.

## Bibliotecas

Toda biblioteca nova precisa de justificativa (o enunciado pede a lista com o porquê) e entra com a versão fixada (`Directory.Packages.props` ou `package.json` com `--save-exact`). Prefira o que a plataforma já oferece: o projeto usa o SSE, o rate limiter e o logging nativos do .NET em vez de pacotes.

## Commits

Conventional Commits em pt-BR, um commit por mudança coesa que compila e passa nos testes. Tipos, escopos e exemplos no [fluxo Git](fluxo-git.md#commits).
