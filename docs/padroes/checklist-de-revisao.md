# Checklist de revisão

Para revisar um PR, ou o próprio código antes de abrir um. Nem todo item se aplica a toda mudança; um "não" sem justificativa é motivo para conversar.

## Escopo e decisões

- [ ] A mudança está no escopo da sprint (`docs/05-sprints.md`)? Ideias de fora viraram nota de "próxima versão", não código?
- [ ] Contraria algum ADR? Se sim, há um ADR novo com duas alternativas, e o `DECISOES.md` foi atualizado ([fluxo de ADR](fluxo-de-adr.md))?
- [ ] Biblioteca nova tem justificativa e versão fixada?

## Correção

- [ ] Os casos de erro foram pensados (404, 409, 412, 422), e não só o caminho feliz?
- [ ] Concorrência: duas requisições ou dois Workers ao mesmo tempo quebram algo (`If-Match`, `xmin`, `SKIP LOCKED`, índice único)?
- [ ] O `CancellationToken` chega até o I/O?
- [ ] Texto com acento, vazio, muito longo ou com caracteres especiais funciona?

## Arquitetura

- [ ] A regra de dependência entre camadas foi respeitada?
- [ ] O endpoint continua fino, e a regra está no domínio ou no caso de uso?
- [ ] A máquina de estados continua só no `Chamado` (o front não decide transições)?
- [ ] Mudança de schema veio como migration nova, com as constraints do modelo?

## IA e dados pessoais

- [ ] Todo texto que vai ao LLM ou ao embedding passa como `TextoMascarado`?
- [ ] A saída do modelo é validada antes de ser usada ou exibida?
- [ ] Logs e spans continuam sem conteúdo (prompt, resposta, texto do chamado)?
- [ ] Prompt alterado virou versão nova, com eval antes de virar padrão?
- [ ] Kill switches e orçamentos (`IA_*_HABILITADO`, `*_MAX_TOKENS_SAIDA`) foram respeitados?

## Testes

- [ ] Há teste para o comportamento novo, na camada certa ([guia de testes](guia-de-testes.md))?
- [ ] O teste foi quebrado de propósito uma vez e falhou?
- [ ] Nada de `UtcNow`, `Sleep` fixo, banco em memória ou provedor real fora da categoria `ProvedorReal`?
- [ ] `bash scripts/testes.sh` passa localmente, e o CI está verde (backend, frontend, smoke e E2E)?

## Frontend

- [ ] Estados carregando, vazio e erro, e layout em 375 px?
- [ ] Acesso à API só por `web/src/api/`, filtros na URL, sem `any`?
- [ ] Labels nos campos, foco visível e contraste AA (cores pelo tema)?

## Configuração e operação

- [ ] Variável de ambiente nova está no `.env.example` (com comentário) e no `docker-compose.yml` **de todos os serviços que a usam**?
- [ ] O `docker compose up` de um clone limpo, sem `.env`, continua subindo tudo?
- [ ] Nenhum segredo no repositório?

## Documentação

- [ ] README, contratos (`04-contratos-api.md`) e `CLAUDE.md` refletem a mudança?
- [ ] Ao fim da sprint: `JORNADA.md` com o que foi feito e aprendido?
