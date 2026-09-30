# ADR-0014 — Fluxo Git trunk-based com uma branch por sprint

- **Status:** Aceita
- **Data:** 2026-09-30
- **Fase:** preparação da Fase 4 (definido antes do primeiro código)
- **Requisitos relacionados:** enunciado §8 ("histórico de commits real e incremental", CI como diferencial)

## Contexto

O enunciado avalia o **histórico de commits** (real e incremental, não um commit único) e valoriza CI rodando build e testes. O projeto é desenvolvido por **uma pessoa**, em **7 dias**, em 6 sprints curtas (`05-sprints.md`). Precisamos de um fluxo que:

- mantenha a `main` sempre funcional, porque o prazo pode acabar no meio de uma sprint;
- deixe cada entrega **visível e revisável** para o avaliador;
- preserve os commits pequenos.

## Alternativas consideradas

### A) GitFlow clássico (`main`, `develop`, `feature/*`, `release/*`, `hotfix/*`)
- ✅ É um modelo conhecido, com separação explícita entre o que está em desenvolvimento e o que foi liberado.
- ✅ Suporta várias releases em paralelo e correções de produção isoladas.
- ❌ Foi pensado para **várias pessoas e releases agendadas**. Com um dev só, `develop` e `release/*` viram cópias da `main` com passos manuais a mais.
- ❌ Há mais merges, mais branches de longa duração e mais chance de divergência, sem nenhum benefício correspondente neste contexto.
- ❌ O próprio autor do modelo recomenda alternativas mais simples para software com entrega contínua.

### B) Trunk-based com uma branch curta por sprint, PR e merge commit
- ✅ Existe **uma** branch de vida longa (`main`), sempre verde e sempre entregável.
- ✅ **Um PR por sprint** vira a unidade de entrega: a descrição traz os critérios de aceite marcados, e o CI valida antes do merge.
- ✅ O **merge commit** (em vez de squash) preserva os commits pequenos e ainda agrupa visualmente cada sprint no grafo.
- ✅ As **tags por sprint** (`v0.1.0` … `v1.0.0`) permitem navegar pela evolução do projeto.
- ❌ Sem `develop`, não há área de "integração" separada. Neste projeto, a própria branch da sprint cumpre esse papel.
- ❌ As branches de sprint duram um dia inteiro, o que é mais longo que o trunk-based "puro" (branches de horas).

## Decisão

Escolhemos **B: trunk-based com uma branch por sprint**.

- A `main` é protegida: só recebe PR com CI verde.
- Branches `sprint/<n>-<tema>`: uma por vez, criadas da `main` atualizada e apagadas após o merge.
- Branches `fix/<descricao>` são reservadas para corrigir algo já mergeado enquanto outra sprint está em andamento.
- O merge é feito **sempre** com "Create a merge commit". Squash e rebase-merge estão proibidos.
- Uma tag semântica por sprint mergeada.
- Os commits seguem Conventional Commits em pt-BR.

O guia operacional, com comandos e checklist, está em [`docs/padroes/fluxo-git.md`](../padroes/fluxo-git.md).

## Trade-offs aceitos

- Não há isolamento entre "em desenvolvimento" e "liberado" além da `main`. Isso é aceitável porque não existe ambiente de produção com usuários reais.
- O grafo tem merge commits (não é linear). Isso é intencional: cada merge marca o fim de uma sprint.

## Consequências

- Existe um template de PR (`.github/pull_request_template.md`) com o checklist da Definition of Done.
- A proteção da `main` é configurada no GitHub assim que o CI existir (fim da Sprint 0).
- O `CLAUDE.md` instrui o assistente de IA a nunca fazer commit ou push sem pedido e a sugerir mensagens no padrão.
- **Gatilho de reavaliação:** mais de uma pessoa trabalhando em paralelo → branches por feature em vez de por sprint. Releases com suporte a versões antigas → avaliar branches de release.
