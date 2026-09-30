# ADR-0015 — Migrations e seed num serviço one-shot (`migrator`)

- **Status:** Aceita
- **Data:** 2026-09-30
- **Fase:** 4 — Walking Skeleton (Sprint 0), decisão de plataforma
- **Requisitos relacionados:** NFR-08, D6; ADD §12 (topologia)

## Contexto

O schema do PostgreSQL evolui por migrations versionadas do EF Core, e o banco precisa de seed: as 5 categorias na Sprint 0; 200 chamados com Bogus e ~25 artigos nas Sprints 1 e 3. Dois processos dependem do schema: a **API** e o **Worker** (ADR-0001). O `docker compose up` a partir de um clone limpo precisa deixar tudo pronto, sem passo manual (NFR-08).

A pergunta é **quem** aplica as migrations e o seed, e **quando**.

## Alternativas consideradas

### A) Aplicar no startup da API
A API chama `Database.MigrateAsync()` e o seed no `Program.cs`, antes de começar a atender.
- ✅ Um serviço a menos no Compose. Um `dotnet run` na API já deixa o banco pronto.
- ✅ Desde o EF Core 9, o `Migrate` usa um lock no banco contra execuções concorrentes, o que reduz o risco clássico de duas réplicas migrando ao mesmo tempo.
- ❌ A API passa a precisar de privilégio de DDL e tem um startup mais lento. Uma migration com falha deixa a API em loop de restart, e o sintoma aparece como "API fora do ar".
- ❌ O Worker também depende do schema. Ou ele espera a API ficar saudável (um acoplamento que não existe em mais nenhum ponto), ou migra também (dois pontos de migração).
- ❌ O seed, que cresce nas próximas sprints, roda dentro do processo que atende requisições.

### B) Serviço one-shot `migrator`
Um host próprio (`HelpDesk.Migrator`) aplica as migrations e o seed idempotente e termina. No Compose, a `api` e o `worker` dependem dele com `condition: service_completed_successfully`.
- ✅ Um único ponto altera o schema. A API e o Worker não têm DDL no caminho de inicialização.
- ✅ Uma falha fica isolada e visível: o `migrator` termina com código ≠ 0 e nada mais sobe. É o mesmo formato de um *init job* ou de uma etapa de pipeline de release.
- ✅ A lógica de migrar e fazer o seed fica na `Infrastructure` e é reaproveitada pela fixture dos testes de integração (Testcontainers), que usa as migrations reais em vez de `EnsureCreated`.
- ❌ É um serviço a mais no Compose. No desenvolvimento fora do Compose, é preciso rodar o `migrator` antes da API.
- ❌ `dotnet ef migrations bundle` seria mais enxuto, mas não executa o seed. Por isso o `migrator` é um projeto console, e não o bundle.

## Decisão

Escolhemos **B: serviço one-shot `migrator`**, como já previsto no ADD §12.

- `HelpDesk.Migrator` usa o Generic Host (`Microsoft.Extensions.Hosting`) só para configuração, logging e DI, executa `MigrarEAplicarSeedAsync` e termina com `0` (sucesso) ou `1` (falha, com log do erro).
- A operação fica na `Infrastructure` (`Persistencia/InicializadorBanco`): `Database.MigrateAsync()` seguido de um seed **idempotente** (insere o que falta e não duplica numa segunda execução).
- A API e o Worker **nunca** chamam `Migrate` nem `EnsureCreated`.

## Trade-offs aceitos

- Um contêiner a mais no Compose, que fica parado (`exited (0)`) depois da subida.
- No desenvolvimento local fora do Compose, a ordem é: `db` → `migrator` → API/Worker. Isso fica documentado no README.

## Consequências

- `docker-compose.yml`: `migrator` com `depends_on: db (service_healthy)`; `api` e `worker` com `depends_on: migrator (service_completed_successfully)`.
- Testes de integração: a fixture sobe o `pgvector/pgvector` e chama o mesmo `InicializadorBanco`, o que também valida as migrations em todo PR.
- Um teste garante a idempotência do seed (executar duas vezes → mesmo resultado).
- **Gatilho de reavaliação:** implantar num orquestrador com mecanismo próprio de migração (por exemplo, um job do Kubernetes ou uma etapa de pipeline de release). Nesse caso, o `migrator` vira essa etapa, sem mudar o código da `Infrastructure`.
