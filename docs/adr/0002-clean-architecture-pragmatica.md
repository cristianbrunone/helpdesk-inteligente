# ADR-0002 — Clean Architecture pragmática

- **Status:** Aceita
- **Data:** 2026-09-30
- **Fase:** 2 — Estilo arquitetural
- **Requisitos relacionados:** NFR-09, NFR-10, NFR-15, D4

## Contexto

O enunciado pede "separação clara de responsabilidades (rotas/controllers, regras de negócio, acesso a dados, integrações)", e isso está entre os critérios de avaliação. O domínio tem uma regra central (a máquina de estados), que precisa ser testada isoladamente, e integrações externas trocáveis (LLM, embeddings) que precisam de fake.

## Alternativas consideradas

### A) Vertical Slice Architecture pura
Cada feature (por exemplo, `CriarChamado`, `MudarStatus`) é uma pasta autocontida com endpoint, handler e acesso a dados.
- ✅ Alta coesão por feature e pouca cerimônia.
- ✅ Adicionar uma feature toca uma pasta só.
- ❌ As regras de domínio tendem a se espalhar pelos handlers. A máquina de estados e o mascaramento são **transversais** e precisam de um lar único.
- ❌ A integração com o LLM, que é usada pela triagem e pelo copiloto, acabaria duplicada ou precisaria de uma camada compartilhada, o que é, na prática, reinventar camadas.
- ❌ A separação "regra de negócio × dados × integrações" que o enunciado pede fica menos evidente para o avaliador.

### B) Clean Architecture pragmática (camadas + organização por feature dentro delas)
Temos os projetos `Domain`, `Application`, `Infrastructure` e os hosts (`Api`, `Worker`). Dentro da `Application`, a organização é **por feature** (`Chamados/`, `Triagem/`, `Copiloto/`...).
- ✅ O domínio é puro: a máquina de estados, as invariantes e os value objects são testáveis sem banco nem DI.
- ✅ As portas (`IChamadoRepository`, `IFilaTriagem`, `IBuscaSemantica`...) ficam na `Application`, e as implementações na `Infrastructure`. Trocar o provedor ou fazer o fake é natural.
- ✅ A separação pedida pelo enunciado fica explícita na estrutura do repositório.
- ✅ Organizar por feature dentro das camadas recupera boa parte da coesão da alternativa A.
- ❌ Há mais cerimônia (interfaces e mapeamentos) do que no Vertical Slice.
- ❌ Existe o risco de abstrair demais (repositório genérico, mediator, AutoMapper...).

## Decisão

Escolhemos **B: Clean Architecture pragmática**, com estas regras explícitas contra a cerimônia excessiva:

1. **Sem mediator e sem AutoMapper.** Os casos de uso são classes de serviço simples injetadas; o mapeamento é manual e explícito.
2. **Sem repositório genérico.** As portas são específicas do caso de uso.
3. **O lado de leitura pode pular o domínio.** A listagem e o dashboard usam consultas de projeção direta (EF `Select` ou SQL), sem hidratar entidades. É um CQRS leve, sem dois bancos.
4. **O domínio rico só onde há regra:** o `Chamado` (transições) e a `TriagemIA` (ciclo de vida). O resto pode ser anêmico sem culpa.

## Trade-offs aceitos

- Algumas interfaces com uma única implementação real (aceitável porque o fake é a segunda).
- Adicionar uma feature toca mais de um projeto.

## Consequências

```
src/
  HelpDesk.Domain/          entidades, enums, máquina de estados, erros de domínio
  HelpDesk.Application/     casos de uso por feature, portas, pipeline de triagem, mascaramento, prompts
  HelpDesk.Infrastructure/  EF Core, migrations, pgvector, fila, provedores LLM (Fake / OpenAI-compatível)
  HelpDesk.Api/             endpoints (Minimal APIs), ProblemDetails, OpenAPI, health
  HelpDesk.Worker/          BackgroundServices (triagem, indexação)
tests/
  HelpDesk.UnitTests/
  HelpDesk.IntegrationTests/  (Testcontainers)
  HelpDesk.ArchitectureTests/
```

- Os erros de domínio (transição inválida, estado final) são mapeados para ProblemDetails **409** num único ponto da API.
- A escolha entre Minimal APIs e Controllers será registrada na Fase 3, junto com os contratos.
- **Gatilho de reavaliação:** se a maioria das features se mostrar CRUD sem regra, migrar para slices onde fizer sentido.
