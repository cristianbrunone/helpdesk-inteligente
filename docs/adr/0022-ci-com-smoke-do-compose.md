# ADR-0022 — CI no GitHub Actions com build, testes e smoke test do Compose

- **Status:** Aceita
- **Data:** 2026-09-30
- **Fase:** 4 — Walking Skeleton (Sprint 0), decisão de plataforma
- **Requisitos relacionados:** enunciado §8 (CI rodando build e testes é "diferencial forte"; `docker compose up` deve funcionar); NFR-08; ADR-0014 (PR para a `main` só com CI verde)

## Contexto

O repositório está no GitHub e a `main` só recebe PR com CI verde (ADR-0014), então a plataforma é o **GitHub Actions**. A questão é **o que** a pipeline verifica.

O primeiro item da Definition of Done é "`docker compose up` a partir de um clone limpo sobe tudo, sem chave de API", e o plano o coloca entre o que "nunca se corta". Na própria Sprint 0 apareceram falhas que só esse cenário revela: um arquivo necessário ao Compose estava sendo ignorado pelo `.gitignore`, e a imagem Alpine se comportava diferente da máquina local (biblioteca nativa ausente, bundle de CAs sem ferramenta de atualização).

## Alternativas consideradas

### A) Só build e testes (backend e frontend em paralelo)
- ✅ Cobre exatamente o que o enunciado pede ("build e testes") e roda em uns 4 a 6 minutos.
- ✅ É simples e pouco sujeita a falhas intermitentes.
- ❌ O item 1 da DoD não é verificado automaticamente: um Dockerfile quebrado, um arquivo fora do git ou um healthcheck errado só aparecem na validação manual.

### B) Build e testes + smoke test do Compose
Um terceiro job, em paralelo, sobe o ambiente completo **sem `.env`** num clone limpo e verifica os endpoints.
- ✅ Automatiza o item mais importante da DoD em todo push: o runner é exatamente o cenário de quem avalia o projeto.
- ✅ Detecta a classe de erro que só aparece no ambiente conteinerizado (arquivo ignorado, diferença de imagem, ordem de subida).
- ✅ Reduz a validação manual na nuvem a uma confirmação.
- ❌ Uns 4 a 6 minutos a mais por execução (build das imagens), em paralelo com os outros jobs.
- ❌ Mais sujeito a falhas intermitentes (download de imagens, tempo de subida).

## Decisão

Escolhemos **B**, num único workflow (`.github/workflows/ci.yml`) com três jobs paralelos:

| Job | Passos |
|---|---|
| **backend** | .NET pelo `global.json` → `dotnet format --verify-no-changes` → `dotnet build` → `dotnet test --filter "Category!=ProvedorReal"` (Testcontainers usa o Docker do runner) |
| **frontend** | Node pelo `web/.nvmrc` → `npm ci` → `npm run lint` → `npm test` → `npm run build` |
| **compose** | `docker compose up --build --wait` sem `.env` → `/health` = `Healthy`, `/api/categorias` **pelo Nginx** (`:8080`), `/swagger` = 200, migrator com saída 0 → logs de todos os serviços se falhar → `docker compose down -v` |

Regras:

- **Gatilhos:** push em qualquer branch e PR para a `main`. Execuções antigas da mesma branch são canceladas (`concurrency`).
- **Nenhum segredo no CI:** a IA fica no fake (padrão) e os testes `ProvedorReal` são excluídos. O workflow tem `permissions: contents: read`.
- **Actions fixadas em versão exata** (e não só na major), para que uma atualização de terceiros não mude o CI sem um commit nosso.
- **Cache** de pacotes NuGet e npm, e timeout por job, para que uma falha travada não consuma minutos.

## Trade-offs aceitos

- O CI fica mais lento que o mínimo, embora com os jobs em paralelo o tempo total seja o do mais lento.
- Uma falha intermitente no smoke exige reexecução manual. Os logs dos serviços no fim do job tornam o diagnóstico direto.

## Consequências

- O PR de cada sprint mostra três checks. A proteção da `main` (ADR-0014) passa a exigir os três.
- O smoke do Compose cresce com as sprints: na Sprint 1 ganha uma chamada a `/api/chamados`, e na Sprint 2 a confirmação de que a triagem fake fica `Concluida`.
- **Gatilho de reavaliação:** CI acima de ~10 minutos ou falhas intermitentes frequentes no smoke. Nesse caso, o job `compose` passa a rodar só em PR para a `main`.
