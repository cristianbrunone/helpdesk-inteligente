# ADR-0026 — Autenticação com JWT próprio e perfis, em cookie httpOnly

- **Status:** Aceita
- **Data:** 2026-10-02
- **Fase:** Sprint 6 (login e perfis, condicional)
- **Requisitos relacionados:** "Autenticação JWT com perfis" (MoSCoW **C**, enunciado §9), NFR-06, RN-10
- **Decisões relacionadas:** substitui a premissa **P-03** (sem autenticação na v1); [ADR-0002](0002-clean-architecture-pragmatica.md), [ADR-0013](0013-minimal-apis.md), [ADR-0023](0023-segredos-em-env-local.md)

## Contexto

Até a Sprint 5 não havia autenticação (P-03): o atendente se identificava num campo livre (`alteradoPor`, `autor`, `decididaPor`), e qualquer pessoa que abrisse a aplicação via todos os chamados. O enunciado lista "autenticação simples (JWT) com perfis solicitante e atendente" como diferencial.

O login não é um item isolado: muda o contrato da API (a identidade passa a vir do token, e não do corpo), todos os testes de integração, o frontend (tela de login, rotas protegidas, telas por perfil) e o seed (usuários de exemplo). Por isso ele é uma sprint própria e **condicional**: se não terminar antes de 06/10, a branch não é mergeada e a `main` (`v1.0.0`) continua sendo a entrega.

Restrições:

- O `docker compose up` de um clone limpo precisa continuar subindo **sem `.env`** (DoD), então nada pode exigir um segredo para funcionar.
- O token não pode ser legível por JavaScript, se houver alternativa simples: o front exibe texto vindo de usuários e da IA, e um XSS que lesse o token roubaria a sessão.
- O SSE do copiloto (`fetch` com `ReadableStream`) precisa continuar autenticado.

## Alternativas consideradas

### A) JWT próprio: tabela `usuarios`, hash de senha nativo e `JwtBearer`
Uma tabela `usuarios` (nome, e-mail, perfil, hash da senha), um endpoint de login que confere a senha e emite um JWT assinado (HMAC-SHA256) com o id, o nome, o e-mail e o perfil, e o `JwtBearer` do ASP.NET Core validando o token. O hash usa o PBKDF2 nativo do .NET (`Rfc2898DeriveBytes.Pbkdf2`, SHA-256, sal aleatório).
- ✅ Um pacote só (`Microsoft.AspNetCore.Authentication.JwtBearer`), da Microsoft.
- ✅ Exatamente o que o enunciado pede (JWT com dois perfis), sem tabelas nem endpoints que ninguém usa.
- ✅ Cabe no estilo do projeto: entidade no domínio, porta na Application, mapeamento manual (ADR-0002).
- ❌ Sem recuperação de senha, bloqueio por tentativas, confirmação de e-mail ou refresh token: tudo isso precisaria ser escrito.
- ❌ Código de segurança próprio (hash e emissão do token), que precisa de testes cuidadosos.

### B) ASP.NET Core Identity (`MapIdentityApi`)
O Identity com o EF Core, as suas tabelas (`AspNetUsers`, `AspNetRoles`...) e os endpoints prontos de registro, login e refresh.
- ✅ Hash, bloqueio por tentativas, recuperação de senha e refresh prontos e auditados.
- ❌ Os tokens do `MapIdentityApi` são opacos (formato do próprio Identity), e não JWT; para JWT, ainda seria preciso emitir o token à mão.
- ❌ Sete tabelas e vários endpoints para dois perfis e quatro usuários de demonstração; mais pacotes e um modelo de dados alheio ao resto do projeto.
- ❌ Mais tempo de integração numa sprint que tem prazo.

## Decisão

Escolhemos **A**, com estes detalhes:

| Tema | Decisão |
|---|---|
| **Transporte do token** | Cookie `httpOnly` + `Secure` + `SameSite=Strict`, gravado pela API no login. O JavaScript nunca vê o token. A API também aceita `Authorization: Bearer` (Swagger, testes, `curl`). Frontend e API estão na mesma origem (Nginx), então o `SameSite=Strict` basta contra CSRF. |
| **Validade** | 8 horas (um turno), sem refresh token: expirou, o usuário entra de novo. |
| **Chave de assinatura** | `JWT_CHAVE` no `.env` (ADR-0023). Sem ela, a API gera uma chave aleatória a cada subida e avisa no log: o compose sobe sem `.env`, e as sessões caem quando a API reinicia (aceitável em demonstração; em produção a chave é obrigatória). |
| **Perfis** | `Atendente`: tudo o que existia (lista completa, status, comentários, triagem, copiloto, dashboard). `Solicitante`: abre chamados (nome e e-mail vêm do token) e vê e comenta **só os próprios**. |
| **Posse do chamado** | O e-mail do token igual ao `solicitante_email` do chamado. Sem mudança na tabela de chamados; o atendente pode abrir um chamado em nome de um solicitante (pedido por telefone), informando nome e e-mail. |
| **Chamado alheio** | **404**, e não 403: o solicitante não descobre que o chamado existe. |
| **Triagem** | Interna: a API não devolve a triagem ao solicitante (a sugestão ainda não foi revisada por um humano). |
| **Identidade nas escritas** | `alteradoPor`, `autor` e `decididaPor` passam a vir do token (o nome do usuário), e não mais do corpo. Os registros antigos mantêm o texto que tinham. |
| **Usuários** | Criados pelo seed (idempotente), 2 atendentes e 2 solicitantes fictícios, com a senha de demonstração `HelpDesk@2026`: atendentes `ana.suporte@example.com` e `bruno.suporte@example.com`; solicitantes `marina.costa@example.com` e `paulo.reis@example.com`. Guardada só como hash PBKDF2. |

## Trade-offs aceitos

- Sem recuperação de senha, cadastro, bloqueio por tentativas nem refresh token: fora do escopo de uma demonstração.
- Sem a chave no `.env`, reiniciar a API derruba as sessões.
- O solicitante é identificado pelo e-mail: se o e-mail de um usuário mudar, os chamados antigos deixam de ser dele (não há tela para mudar e-mail).
- Mudança de contrato: os campos de identidade saem do corpo. Como não há clientes externos, não há versão `/api/v2`.

## Consequências

- Domínio: entidade `Usuario` com o perfil; Infrastructure: tabela `usuarios` (e-mail único, sem diferenciar caixa), hash PBKDF2 e emissão do JWT; API: `POST /api/auth/login`, `GET /api/auth/eu`, `POST /api/auth/sair` e políticas por perfil.
- Contrato (`04-contratos-api.md`): **401** sem sessão, **403** perfil sem permissão, os campos de identidade vindos do token e as regras do solicitante.
- Testes de integração autenticados (um helper emite o token); o smoke e o E2E fazem login.
- Frontend: tela de login, rotas protegidas, menu do usuário e telas por perfil; sai o campo "Seu nome (atendente)".
- **Gatilho de reavaliação:** usuários reais (cadastro, recuperação de senha, bloqueio) ou login corporativo → ASP.NET Core Identity ou um provedor OIDC externo (Entra ID, Keycloak), mantendo o JWT e as políticas por perfil.
