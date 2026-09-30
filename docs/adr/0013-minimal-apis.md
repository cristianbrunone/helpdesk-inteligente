# ADR-0013 — Minimal APIs com route groups em vez de Controllers

- **Status:** Aceita
- **Data:** 2026-09-30
- **Fase:** 3 — Estratégia de comunicação
- **Requisitos relacionados:** RF-01..44, NFR-15; requisitos de API do enunciado (ProblemDetails, OpenAPI)

## Contexto

A API tem cerca de 12 endpoints, agrupados em 4 recursos (chamados, triagem, copiloto, dashboard), além de categorias e health. O enunciado exige ProblemDetails, OpenAPI acessível e separação clara entre rotas, regras, dados e integrações. O backend usa .NET 10 (P-01).

## Alternativas consideradas

### A) Controllers (MVC)
- ✅ É o modelo mais conhecido, com filtros, model binding e validação automática via `[ApiController]`.
- ✅ A organização por classe é familiar para qualquer dev .NET.
- ❌ Traz mais cerimônia (herança, atributos, action results) para uma API pequena.
- ❌ Controllers tendem a acumular lógica, e a disciplina de "controller fino" fica por conta da revisão.
- ❌ Não é mais o padrão dos templates e da documentação recente do ASP.NET Core para APIs novas.

### B) Minimal APIs com `MapGroup` e `TypedResults`
- ✅ Os endpoints ficam finos por construção: recebem o request, chamam o caso de uso e mapeiam o resultado. Não há onde "esconder" regra de negócio.
- ✅ `TypedResults` torna os tipos de resposta explícitos na assinatura, o que melhora o OpenAPI gerado e os testes.
- ✅ O .NET 10 tem **validação nativa** para Minimal APIs (`AddValidation()` + DataAnnotations), com resposta em ProblemDetails.
- ✅ Os route groups (`/api/chamados`) aplicam filtros, tags OpenAPI e rate limiting por grupo.
- ✅ Tem menos overhead de inicialização e de execução (irrelevante aqui, mas correto).
- ❌ A organização de arquivos é responsabilidade nossa (não existe a convenção "uma classe por controller").
- ❌ Alguns recursos avançados de MVC (por exemplo, model binders customizados complexos) exigem alternativas.

## Decisão

Escolhemos **B: Minimal APIs**, organizadas por recurso:

```
HelpDesk.Api/
  Endpoints/
    ChamadosEndpoints.cs      // MapChamados(this IEndpointRouteBuilder) → MapGroup("/api/chamados")
    TriagemEndpoints.cs
    CopilotoEndpoints.cs
    DashboardEndpoints.cs
    CategoriasEndpoints.cs
  Erros/
    DominioExceptionHandler.cs   // IExceptionHandler: erro de domínio → ProblemDetails (409/404)
  Program.cs
```

Regras:

1. **Um endpoint = até ~10 linhas**: binding → caso de uso da `Application` → `TypedResults`.
2. **Validação em duas camadas.** A de formato fica nas DataAnnotations do request (→ 422). As regras de negócio ficam no domínio (→ 409).
3. **Os erros de domínio viram ProblemDetails num único lugar** (`IExceptionHandler` + `AddProblemDetails`). Nenhum endpoint monta erro à mão.
4. **OpenAPI nativo** (`Microsoft.AspNetCore.OpenApi`), com Swagger UI apontando para `/openapi/v1.json` em `/swagger`.

## Trade-offs aceitos

- O padrão de organização é uma convenção nossa, documentada no guia de contribuição (Fase 5).
- A validação por DataAnnotations cobre bem a validação de formato. Regras condicionais complexas, se aparecerem, irão para o domínio, e não para uma biblioteca de validação extra.

## Consequências

- O teste de contrato compara o `/openapi/v1.json` gerado com os endpoints e os códigos de resposta documentados em [`04-contratos-api.md`](../04-contratos-api.md).
- Os testes de integração usam `WebApplicationFactory` + Testcontainers para exercitar a API real.
- **Gatilho de reavaliação:** crescimento para dezenas de recursos com muitas convenções compartilhadas. Nesse caso, controllers podem voltar a compensar.
