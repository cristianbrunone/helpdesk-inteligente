# ADR-0023 — Segredos em variáveis de ambiente via `.env` local, com proteções contra vazamento

- **Status:** Aceita
- **Data:** 2026-09-30
- **Fase:** 4 — Walking Skeleton (Sprint 0), decisão de plataforma
- **Requisitos relacionados:** enunciado §6 (rodar sem chave de API; provedor selecionado por variável de ambiente); NFR-06; ADR-0005, ADR-0006

## Contexto

O projeto tem um único segredo real: a **`LLM_API_KEY`** do provedor de IA (Gemini no free tier, ADR-0006). Ela é opcional, porque o padrão é o provedor fake e tudo roda sem chave. A senha do PostgreSQL existe só para o ambiente local de desenvolvimento. O CI e as sessões na nuvem **não usam nenhum segredo** (ADR-0022 e `CLAUDE.md`).

O risco relevante neste contexto não é alguém ler o ambiente de um container numa máquina de um único usuário. É o **vazamento da chave**: num commit, num print, num log ou colada numa conversa.

## Alternativas consideradas

### A) Variáveis de ambiente via `.env` local (12-factor), com proteções
O `.env` fica fora do git; o `.env.example` é versionado sem valores; o Compose injeta os valores como variáveis de ambiente.
- ✅ É o que o enunciado espera ("roda sem chave; o provedor real é ativado por variável de ambiente"). Ativar o Gemini é uma linha no `.env`, para qualquer avaliador.
- ✅ Nenhuma infraestrutura ou pacote novo.
- ✅ As proteções contra vazamento são baratas: `.gitignore`, *push protection* do GitHub, nunca logar configuração, CI sem segredos.
- ❌ A chave fica em texto puro no disco local.
- ❌ Variáveis de ambiente aparecem no `docker inspect` e são herdadas por processos filhos.

### B) Segredos em arquivo (secrets do Compose em `/run/secrets`) + `dotnet user-secrets` fora do Docker
- ✅ A chave não aparece no `docker inspect` nem no ambiente dos processos.
- ✅ Fica mais próximo de um orquestrador de produção (Kubernetes Secrets, Key Vault).
- ❌ Pacote novo (`Microsoft.Extensions.Configuration.KeyPerFile`) e dois mecanismos diferentes (arquivo no Docker, user-secrets fora dele).
- ❌ Ativar o provedor real deixa de ser "uma linha no `.env`", o que piora a experiência de quem avalia, com ganho pequeno numa máquina de desenvolvimento.

## Decisão

Escolhemos **A: variáveis de ambiente via `.env` local**, com estas proteções obrigatórias:

1. **Fora do git:** `.env`, `.env.*` (exceto `.env.example`), `*.pem`, `*.key`, `*.pfx` e `secrets.json` estão no `.gitignore`. O `.env.example` só tem nomes e valores não sensíveis.
2. **Push protection:** secret scanning com *push protection* ativado no repositório do GitHub, bloqueando o envio de chaves conhecidas (as do Google incluídas).
3. **Nunca logar configuração:** nenhum log, ProblemDetails ou mensagem de erro inclui valores de configuração. A telemetria de IA registra provedor e modelo, nunca a chave (NFR-11).
4. **CI e nuvem sem segredos:** o fake é o padrão, os testes `ProvedorReal` não rodam no CI, e as sessões na nuvem nunca usam chave (`CLAUDE.md`).
5. **Do AI Studio direto para o `.env`:** a chave é copiada pelo botão do console e colada no `.env`, sem passar por chats, issues ou capturas de tela. Se vazar, é revogada no console e substituída.
6. **Valor vazio = não configurado:** variáveis opcionais vazias no `.env` (por exemplo, `LLM_API_KEY=`) são tratadas como ausentes pela aplicação, e não como erro.

## Trade-offs aceitos

- Chave em texto puro no disco do desenvolvedor, protegida pelo sistema operacional e pelo `.gitignore`.
- Exposição das variáveis no `docker inspect` local.

## Consequências

- O README documenta como ativar o provedor real: copiar a chave para o `.env` e trocar `LLM_PROVIDER`.
- A `CA_EXTRA_PEM` (ADR-0022/compose) segue o mesmo modelo: caminho no `.env`, arquivo fora do repositório.
- **Gatilho de reavaliação:** implantação num ambiente compartilhado ou de produção. Nesse caso, a chave passa a vir do secret store do orquestrador (Key Vault, Kubernetes Secrets) como variável de ambiente ou arquivo, sem mudar o código que lê a configuração.
