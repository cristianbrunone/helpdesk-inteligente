# ADR-0017 — Mantine como biblioteca de UI do frontend

- **Status:** Aceita
- **Data:** 2026-09-30
- **Fase:** 4 — Walking Skeleton (Sprint 0), decisão de plataforma
- **Requisitos relacionados:** enunciado §5 (frontend) e §8 ("indique quais bibliotecas e por quê"); RF-40..43 (dashboard); NFR de UX (estados e 375 px)

## Contexto

O enunciado deixa a biblioteca de UI livre, mas avalia a **experiência de uso**: estados de carregamento, erro e validação, e layout responsivo que funcione no celular. O plano (`05-sprints.md`) acrescenta:

- **Sprint 1:** lista com filtros, badges e paginação; formulário com React Hook Form + Zod; aviso amigável de conflito (412).
- **Sprint 3:** dashboard com cartões e gráficos.
- **Sprint 4:** painel de chat do copiloto.
- **Sprint 5:** acessibilidade básica (labels, foco, contraste).

Todo componente de tela precisa tratar carregando, vazio e erro e funcionar em 375 px (`CLAUDE.md`). O projeto tem um dev e 7 dias; o diferencial avaliado está na IA e nos testes, não num design autoral.

## Alternativas consideradas

### A) Tailwind CSS v4 + shadcn/ui (componentes copiados para o repositório, sobre Radix)
- ✅ O código dos componentes fica no repositório: é totalmente customizável e não há API de terceiros para acompanhar.
- ✅ Os primitivos Radix são acessíveis (foco, teclado, ARIA) em diálogos, menus e selects.
- ✅ É muito popular e resulta num visual próprio.
- ✅ Os gráficos teriam o componente `chart` do shadcn (sobre Recharts).
- ❌ São várias dependências pequenas: `tailwindcss`, `@tailwindcss/vite`, um pacote Radix por componente, `class-variance-authority`, `clsx`, `tailwind-merge`, `lucide-react`.
- ❌ O layout responsivo (casca da aplicação, menu mobile) e os estados visuais são montados por nós, o que consome tempo do prazo.
- ❌ Dezenas de arquivos de componentes copiados passam a fazer parte do código revisado.

### B) Mantine (biblioteca de componentes completa)
- ✅ Cobre o plano inteiro com uma família só: `AppShell` responsivo com menu hambúrguer (375 px); `Table`, `Badge`, `Pagination`, `Select`, `Skeleton`, `Loader` e `Alert` para os estados; `Notifications` para o 412; `@mantine/charts` (sobre Recharts) para o dashboard.
- ✅ Acessível por padrão (labels, foco, ARIA), com tema e modo escuro.
- ✅ Os inputs funcionam com React Hook Form + Zod (`register`/`Controller`); o `@mantine/form` não é necessário.
- ✅ Poucos pacotes na Sprint 0: `@mantine/core`, `@mantine/hooks` e, como dev, `postcss` + `postcss-preset-mantine`.
- ❌ O visual tem "cara de Mantine" sem customização de tema.
- ❌ O bundle é maior do que com componentes próprios.
- ❌ Nos testes com jsdom, os componentes precisam do `MantineProvider` e de polyfills de `matchMedia` e `ResizeObserver` (configuração única no setup do Vitest).
- ❌ Há acoplamento à API da biblioteca: trocar depois é caro.

## Decisão

Escolhemos **B: Mantine** (versão 9, a estável atual), porque ela entrega pronto o que o enunciado avalia (estados, responsividade e acessibilidade) e libera o prazo para os diferenciais de IA e para os testes.

- **Pacotes por sprint:** `@mantine/core` e `@mantine/hooks` (Sprint 0); `@mantine/notifications` (Sprint 1); `@mantine/charts` + `recharts` (Sprint 3). Dev: `postcss`, `postcss-preset-mantine` e `postcss-simple-vars` (configuração recomendada pela Mantine).
- **Sem `@mantine/form`:** os formulários seguem com React Hook Form + Zod, como já definido na stack.
- **Testes:** um `renderComProviders` (MantineProvider + QueryClient + Router) e os polyfills ficam no setup do Vitest, para que cada teste renderize como a aplicação real.
- Os componentes de tela continuam sem `fetch`: todo acesso passa por `web/src/api/` + hooks do TanStack Query.

## Trade-offs aceitos

- Visual padrão da Mantine, com ajustes só pelo tema (cor primária, raio de borda, fontes).
- Bundle maior, aceitável para um app interno de help desk.
- Dependência de uma biblioteca de terceiros com ciclo próprio de versões maiores.

## Consequências

- O commit do frontend da Sprint 0 cria o `MantineProvider` com um tema do projeto e o `AppShell` com as categorias carregadas pela camada `api/`.
- A decisão de biblioteca de gráficos da Sprint 3 fica resolvida: `@mantine/charts`.
- O `CLAUDE.md` passa a listar os pacotes da Mantine como aprovados.
- **Gatilho de reavaliação:** necessidade de identidade visual própria ou de aderir a um design system da empresa. Nesse caso, a alternativa A (Tailwind + shadcn/ui) entra tela por tela, sem mudar a camada `api/` nem os hooks.
