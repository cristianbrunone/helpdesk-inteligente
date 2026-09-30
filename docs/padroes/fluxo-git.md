# Guia do fluxo Git

> **Decisão:** [ADR-0014](../adr/0014-fluxo-git-trunk-based.md). Trunk-based com uma branch por sprint, PR e merge commit.
> Este guia é **operacional**: o que fazer, em que ordem e com quais comandos.

## Visão geral

```
main ●──────────────●──────────────●──── …   (protegida, sempre verde)
      \            / \            /
       ●──●──●──●─●   ●──●──●──●─●
       sprint/0         sprint/1
          │ PR + CI        │ PR + CI
          └ merge commit   └ merge commit
            tag v0.1.0       tag v0.2.0
```

| Branch | Tema | Tag no merge |
|---|---|---|
| `sprint/0-walking-skeleton` | Esqueleto + PoC de IA | `v0.1.0` |
| `sprint/1-chamados` | Chamados de ponta a ponta | `v0.2.0` |
| `sprint/2-triagem-ia` | Triagem por IA | `v0.3.0` |
| `sprint/3-rag-dashboard` | RAG + Dashboard | `v0.4.0` |
| `sprint/4-copiloto` | Copiloto conversacional | `v0.5.0` |
| `sprint/5-entrega` | Hardening e entrega | `v1.0.0` |

**Regras:**

1. Existe **uma** branch de sprint por vez, sempre criada a partir da `main` atualizada.
2. Nada é commitado direto na `main`, com uma exceção: os commits de fundação, feitos antes de a proteção existir.
3. Todo merge para a `main` é feito via **PR com CI verde** e **merge commit**. Squash e rebase-merge não são usados.
4. Depois do merge: criar a tag e apagar a branch (local e remota).
5. Um bug em algo já mergeado, encontrado durante outra sprint, vai para uma branch `fix/<descricao>` criada da `main`, com PR próprio. Ela não é misturada à sprint em andamento.

## Ciclo de uma sprint

### 1. Começar

```bash
git checkout main
git pull
git checkout -b sprint/1-chamados
```

### 2. Trabalhar

- Um commit por mudança coesa, que compila e passa nos testes.
- Envie com frequência. O CI roda em cada push, então você descobre cedo se algo quebrou.

```bash
git push -u origin sprint/1-chamados   # primeiro push
git push                               # pushes seguintes
```

### 3. Abrir o PR

No GitHub: **Compare & pull request**, de `sprint/1-chamados` para `main`. O template já vem preenchido, e é preciso completar:

- os critérios de aceite da sprint (copiados de `docs/05-sprints.md`), marcados;
- os ADRs criados ou alterados;
- o que mudou em relação ao plano.

### 4. Fazer o merge

- Espere o CI ficar verde.
- Revise o próprio diff no PR. É a última chance de pegar um `console.log` ou um TODO esquecido.
- Clique em **Create a merge commit** e depois em **Delete branch**.

### 5. Criar a tag e limpar

```bash
git checkout main
git pull
git tag -a v0.2.0 -m "Sprint 1: chamados de ponta a ponta"
git push origin v0.2.0
git branch -d sprint/1-chamados
```

## Commits

**Formato:** Conventional Commits em pt-BR, com a descrição no imperativo e em minúsculas.

```
tipo(escopo): descrição curta no imperativo

Corpo opcional explicando o PORQUÊ (não o quê).
Refs: RF-06, ADR-0010
```

| Tipo | Uso |
|---|---|
| `feat` | Funcionalidade nova |
| `fix` | Correção de bug |
| `test` | Testes (sem mudar código de produção) |
| `refactor` | Mudança interna sem alterar comportamento |
| `docs` | Documentação, ADRs, README, JORNADA |
| `build` | Dockerfiles, Compose, dependências |
| `ci` | Pipeline do GitHub Actions |
| `chore` | Configuração e manutenção |

**Escopos:** `domain`, `app`, `infra`, `api`, `worker`, `web`, `ia`, `db`, `adr`, `jornada`.

**Exemplos:**

```
feat(domain): implementa máquina de estados do chamado
test(domain): cobre transições permitidas e proibidas
feat(api): adiciona PATCH /api/chamados/{id}/status com If-Match
fix(web): mantém filtros na URL ao trocar de página
docs(adr): ADR-0015 migrations em serviço one-shot
```

**Boas práticas:**

- Não misture refatoração com feature no mesmo commit.
- Não faça commit de código que não compila, nem de testes quebrados.
- O `.env` **nunca** é commitado. Só o `.env.example` vai para o repositório.
- Commits feitos com ajuda de assistente de IA mantêm o trailer `Co-Authored-By`, por transparência (o README descreve onde e como a IA foi usada).

## Proteção da `main` (configurar no fim da Sprint 0)

Em GitHub → **Settings → Branches → Add branch ruleset** (ou *branch protection rule*) para `main`:

- [x] Exigir pull request antes do merge
- [x] Exigir que os status checks passem (o job de CI)
- [x] Exigir que a branch esteja atualizada antes do merge
- [x] Bloquear force push
- [x] Permitir apenas **merge commits** (em Settings → General → Pull Requests, desmarque squash e rebase)

O status check só aparece na lista depois que o CI rodar pela primeira vez.

## Situações comuns

**A `main` avançou (por exemplo, com um `fix/`) enquanto a sprint estava aberta:**

```bash
git checkout sprint/2-triagem-ia
git merge main
```

Use merge, não rebase, porque a branch já foi enviada ao remoto.

**Commitei algo errado e ainda não enviei:**

```bash
git commit --amend            # corrige o último commit
git reset --soft HEAD~1       # desfaz o último commit mantendo as mudanças
```

**Commitei um segredo por engano:** considere a chave **comprometida**. Revogue-a no provedor e gere outra antes de qualquer coisa. Remover do histórico vem depois, e não substitui a revogação.
