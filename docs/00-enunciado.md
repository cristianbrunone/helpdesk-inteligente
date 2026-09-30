# Teste Técnico — Desenvolvedor Full Stack Sênior

**Projeto:** HelpDesk Inteligente — gestão de chamados com triagem assistida por IA

**Resumo:** construa uma aplicação completa (backend, frontend, banco de dados e testes) para abertura e acompanhamento de chamados de suporte. Uma funcionalidade deve usar IA para sugerir categoria, prioridade, resumo e uma resposta inicial para cada chamado.

- **Prazo:** 7 dias corridos a partir do recebimento.
- **Esforço estimado:** 10 a 14 horas.
- **Entrega:** link de um repositório Git público (GitHub, GitLab ou Azure DevOps) enviado para [e-mail do recrutador].

## O que avaliamos

- Qualidade e organização do código, decisões de arquitetura e clareza das justificativas.
- Modelagem do banco de dados e qualidade das consultas SQL.
- Testes automatizados e facilidade para rodar o projeto.
- Uso responsável de IA: tratamento de falhas, validação da resposta e proteção de dados.
- Experiência de uso do frontend (estados de carregamento, erro e validação).
- Não esperamos perfeição em tudo. Priorize, entregue algo que funcione e explique o que ficou de fora e por quê.

## Após a entrega

Nossa equipe vai avaliar o projeto internamente e retornaremos com o resultado e um feedback.

---

## 1. Stack

| Camada | Obrigatório | Observação |
|---|---|---|
| Backend | C# (.NET 8 ou superior) ou Node.js com TypeScript | C# é a stack principal da vaga e será considerado um diferencial. |
| Frontend | React com TypeScript | Vite ou Next.js. Biblioteca de UI livre. |
| Banco de dados | Relacional: PostgreSQL ou SQL Server | Com migrations versionadas no repositório. |
| Testes | Backend e frontend | Frameworks livres (xUnit, NUnit, Jest, Vitest, Testing Library, Playwright...). |
| IA | Qualquer provedor de LLM | OpenAI, Azure OpenAI, Anthropic, Google, Ollama local etc. |
| Infra | Docker Compose | O projeto inteiro deve subir com um único comando. |

---

## 2. Domínio e regras de negócio

Um chamado é aberto por um **solicitante** e tratado por um **atendente**. Modele no mínimo as entidades abaixo (você pode adicionar campos e entidades se achar necessário):

| Entidade | Campos mínimos |
|---|---|
| **Chamado** | id, titulo, descricao, solicitanteNome, solicitanteEmail, categoriaId, prioridade, status, criadoEm, atualizadoEm, resolvidoEm |
| **Categoria** | id, nome (ex.: Acesso/Login, Financeiro, Bug no sistema, Dúvida, Infraestrutura) |
| **Comentario** | id, chamadoId, autor, texto, criadoEm |
| **HistoricoStatus** | id, chamadoId, statusAnterior, statusNovo, alteradoEm, alteradoPor |
| **TriagemIA** | id, chamadoId, categoriaSugerida, prioridadeSugerida, resumo, respostaSugerida, modelo, status (pendente / concluida / falhou / aceita / rejeitada), criadoEm |

**Prioridade:** Baixa, Média, Alta, Crítica.

### Status e transições permitidas

Qualquer outra transição deve ser rejeitada pela API.

```
Aberto ---> EmAndamento ---> Resolvido ---> Fechado
  |              ^               |
  |              +--- reabrir ---+
  +---> Cancelado
```

Transições permitidas:

| De | Para |
|---|---|
| Aberto | EmAndamento |
| Aberto | Cancelado |
| EmAndamento | Resolvido |
| Resolvido | Fechado |
| Resolvido | EmAndamento (reabrir) |

Regras:

- Toda mudança de status gera um registro em `HistoricoStatus`.
- Ao entrar em **Resolvido**, preencher `resolvidoEm`. Ao reabrir, limpar `resolvidoEm`.
- **Fechado** e **Cancelado** são estados finais: não aceitam novos comentários nem mudança de status.
- Chamados com prioridade **Crítica** não podem ser cancelados.

---

## 3. Backend — API REST

| Método e rota | Descrição |
|---|---|
| `POST /api/chamados` | Cria um chamado. Valida campos obrigatórios e formato do e-mail. Dispara a triagem por IA (seção 6). |
| `GET /api/chamados` | Lista com filtros (status, prioridade, categoria, texto no título/descrição, período), paginação e ordenação (por data de criação ou prioridade). |
| `GET /api/chamados/{id}` | Detalhe com comentários, histórico de status e triagem de IA. |
| `PATCH /api/chamados/{id}/status` | Muda o status respeitando as regras da seção 2. |
| `POST /api/chamados/{id}/comentarios` | Adiciona um comentário. |
| `POST /api/chamados/{id}/triagem` | Refaz a triagem por IA manualmente. |
| `POST /api/chamados/{id}/triagem/aceitar` | Aplica a categoria e a prioridade sugeridas ao chamado. |
| `POST /api/chamados/{id}/triagem/rejeitar` | Marca a sugestão como rejeitada, sem alterar o chamado. |
| `GET /api/dashboard/resumo` | Totais por status e por prioridade, tempo médio de resolução (em horas) por categoria e taxa de aceitação das sugestões da IA. |
| `GET /health` | Health check da API e do banco. |

### Requisitos da API

- Erros com formato padronizado (ex.: ProblemDetails / RFC 9457) e status HTTP corretos (400, 404, 409, 422...).
- Documentação OpenAPI/Swagger acessível ao subir o projeto.
- Logs estruturados, incluindo as chamadas à IA (tempo de resposta e sucesso/falha, sem registrar dados pessoais).
- Separação clara de responsabilidades (ex.: rotas/controllers, regras de negócio, acesso a dados, integrações).
- Configuração por variáveis de ambiente. Nenhuma chave ou segredo no repositório — forneça um `.env.example`.

---

## 4. Banco de dados

- Migrations versionadas (EF Core Migrations, Prisma, Knex, Flyway, DbUp ou similar).
- Seed com pelo menos **3 categorias** e **40 chamados** em status e datas variados, para que listagem e dashboard tenham dados.
- Índices criados de acordo com os filtros e ordenações da listagem. **Justifique cada um no README.**
- O endpoint de dashboard deve ser resolvido com agregações no banco (`GROUP BY`, `AVG` etc.), e não carregando todos os registros em memória. Você pode usar SQL puro ou o ORM, desde que a consulta seja eficiente.
- Constraints de integridade (chaves estrangeiras, `NOT NULL`, `CHECK` ou enum para status e prioridade).

---

## 5. Frontend — React

| Tela | Requisitos |
|---|---|
| **Lista de chamados** | Tabela ou cards com filtros, busca, paginação e ordenação. Os filtros devem ficar na URL (recarregar a página mantém o estado). Indicador visual de prioridade e status. |
| **Novo chamado** | Formulário com validação no cliente, mensagens de erro claras e tratamento de erro vindo da API. |
| **Detalhe do chamado** | Dados, comentários (adicionar novo), histórico de status e botões de mudança de status mostrando apenas as transições permitidas. |
| **Painel da IA (no detalhe)** | Mostra categoria, prioridade, resumo e resposta sugerida, com botões Aceitar, Rejeitar e Refazer triagem. Deixa claro que o conteúdo foi gerado por IA. Trata os estados pendente, falhou e concluída. |
| **Dashboard** | Cartões com os totais e pelo menos um gráfico (ex.: chamados por status). |

- Estados de carregamento, vazio e erro em todas as telas.
- Layout responsivo (funcionar em tela de celular).
- Camada de acesso à API isolada dos componentes (ex.: React Query, SWR ou serviço próprio).

---

## 6. Funcionalidade com IA — triagem de chamados

Ao criar um chamado, o sistema deve enviar título e descrição para um LLM e obter uma sugestão estruturada. A sugestão **não é aplicada automaticamente**: um atendente decide se aceita ou rejeita (humano no controle).

Formato de resposta esperado do modelo (valide antes de salvar):

```jsonc
{
  "categoria": "Acesso/Login",          // deve ser uma das categorias existentes
  "prioridade": "Alta",                 // Baixa | Média | Alta | Crítica
  "resumo": "Usuário não consegue ...", // até 200 caracteres
  "respostaSugerida": "Olá, ...",       // resposta inicial ao solicitante
  "confianca": 0.82                     // 0 a 1
}
```

### Requisitos obrigatórios

- **Não bloquear a criação do chamado:** se a IA estiver lenta ou fora do ar, o chamado é criado mesmo assim e a triagem fica como `pendente` ou `falhou`. Processamento assíncrono (fila, background job) é um diferencial.
- **Timeout e retry** configuráveis para a chamada ao provedor.
- **Validação da saída:** resposta fora do formato, categoria inexistente ou prioridade inválida não devem quebrar o sistema. Registre a falha e marque a triagem como `falhou`.
- **Proteção de dados (LGPD):** antes de enviar o texto ao LLM, mascare e-mails, telefones e CPFs que apareçam na descrição. Não envie o nome nem o e-mail do solicitante.
- **Abstração do provedor:** a integração deve estar atrás de uma interface, com uma implementação fake/mock que permita rodar o projeto e os testes sem chave de API (selecionada por variável de ambiente).
- O **prompt** deve ficar versionado em arquivo ou constante clara, e o README deve explicar como ele foi construído.

### Diferenciais de IA (opcionais)

- Busca de chamados semelhantes já resolvidos (embeddings + pgvector, ou busca full-text) para enriquecer a sugestão.
- Métrica de qualidade: comparar sugestões aceitas x rejeitadas por categoria no dashboard.
- Controle de custo: registrar tokens consumidos por chamada.

---

## 7. Testes automatizados

| Tipo | Mínimo esperado |
|---|---|
| **Unitários (backend)** | Regras de transição de status (todas as permitidas e ao menos 3 proibidas), mascaramento de dados pessoais e validação/parsing da resposta da IA (resposta válida, JSON inválido, categoria inexistente). |
| **Integração (backend)** | Endpoints principais contra um banco real (Testcontainers, banco em Docker ou similar — não usar banco em memória como substituto do relacional). A IA deve estar mockada. |
| **Frontend** | Pelo menos 3 testes de componente (ex.: validação do formulário, painel da IA nos estados concluída/falhou, botões de status exibindo só transições permitidas). |
| **E2E (diferencial)** | Um fluxo com Playwright ou Cypress: criar chamado, ver triagem, aceitar sugestão. |

- Todos os testes devem rodar com um comando documentado no README. Informe a cobertura, se medir.

---

## 8. Entrega

- `docker compose up` deve subir banco, API e frontend, com migrations e seed aplicados, usando a IA fake por padrão.
- `README.md` com:
  - como rodar;
  - como rodar os testes;
  - como ativar um provedor real de IA;
  - arquitetura em alto nível (um diagrama simples ajuda);
  - justificativa dos índices;
  - o que ficaria para uma próxima versão.
- `DECISOES.md` (curto): principais decisões técnicas, alternativas consideradas e trade-offs.
- Histórico de commits real e incremental — não um único commit com tudo.
- Pipeline de CI (GitHub Actions, Azure Pipelines ou GitLab CI) rodando build e testes é um **diferencial forte**.

---

## 9. Diferenciais gerais (opcionais)

- Autenticação simples (JWT) com perfis solicitante e atendente.
- Arquitetura orientada a eventos para a triagem (fila como RabbitMQ, Azure Service Bus, Redis ou tabela outbox).
- Observabilidade: correlation id nas requisições, métricas ou tracing (OpenTelemetry).
- App mobile mínimo em Flutter listando chamados e mostrando o detalhe.
- Deploy em nuvem (Azure, AWS ou similar) com link acessível.

---

## 10. Regras

- Você pode usar assistentes de IA (Copilot, ChatGPT, Claude etc.) para desenvolver. Informe no README onde e como usou.
- Pode usar bibliotecas e templates, desde que indique quais e por quê.
- Não use dados reais de pessoas no seed.
- Dúvidas sobre o enunciado: envie para [e-mail do recrutador]. Se preferir, assuma uma premissa e registre no `DECISOES.md`.

> **Dica:** entregar bem feito o que é obrigatório vale mais do que muitos diferenciais incompletos.
