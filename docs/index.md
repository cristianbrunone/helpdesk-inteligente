# HelpDesk Inteligente

<div align="center">
  <p><strong>Gestão de chamados de suporte com triagem assistida por IA (RAG com pgvector) e copiloto conversacional com tool calling.</strong></p>
  <p><code>.NET 10 (C#)</code> · <code>React + TypeScript</code> · <code>PostgreSQL + pgvector</code> · <code>Docker Compose</code></p>
</div>

---

## 🧭 Visão Geral

O **HelpDesk Inteligente** foi concebido e implementado como uma aplicação de missão crítica para suporte ao cliente e eficiência operacional de times de atendimento. O sistema combina uma arquitetura robusta e determinística em **Clean Architecture** com recursos avançados de **Inteligência Artificial Generativa**:

1. **Triagem Automatizada por IA:** classificação de categoria, prioridade, resumo estruturado, confiança e resposta sugerida baseada no histórico com versionamento de prompts (`triagem.v1` e `triagem.v2`).
2. **RAG Vetorial (Retrieval-Augmented Generation):** busca semântica em base de conhecimento e histórico de chamados semelhantes via `pgvector` com índice HNSW de 768 dimensões.
3. **Copiloto Conversacional:** assistente interativo para o atendente com respostas em streaming via Server-Sent Events (SSE), execução de ferramentas (*tool calling*) e citação obrigatória de fontes verificáveis.
4. **Guardrails Estritos e LGPD:** mascaramento determinístico de dados sensíveis (PII) antes de qualquer envio ao provedor de IA e verificação ativa contra alucinações de citações.
5. **Autenticação JWT com Perfis:** controle granular de acesso para *Atendentes* e *Solicitantes* com cookies seguros `HttpOnly` e isolamento multi-inquilino.
6. **Deploy de Demonstração em Nuvem:** publicado em VPS sob HTTPS válido (Let's Encrypt), subdomínio próprio e operando com **Google Gemini real** no plano gratuito.

---

## 🏛️ Mapa da Documentação

A documentação do projeto foi estruturada com foco em clareza técnica, rastreabilidade de decisões e reprodutibilidade:

| Seção | Descrição | Onde encontrar |
|---|---|---|
| **Arquitetura (ADD)** | Diagramas C4 (Contexto, Contêineres, Componentes), fluxos e topologia | [Arquitetura & Design](02-add.md) |
| **Requisitos & Regras** | Requisitos funcionais (RF-01 a RF-18), não funcionais e regras de negócio | [Requisitos de Negócio](01-requisitos.md) |
| **Modelo de Dados** | Estrutura de tabelas PostgreSQL, migrações, índices HNSW e consultas | [Modelo de Dados](03-modelo-de-dados.md) |
| **Contratos da API** | Especificação OpenAPI/REST, payloads, cabeçalhos de concorrência e SSE | [Contratos da API](04-contratos-api.md) |
| **Decisões Técnicas (ADRs)** | Catálogo completo dos **27 Architecture Decision Records (ADRs)** | [Catálogo de ADRs](decisoes.md) |
| **Jornada de Desenvolvimento** | Narrativa detalhada da construção incremental, fase a fase | [Jornada do Projeto](JORNADA.md) |
| **Plano de Sprints** | Critérios de aceite, evolução das 8 sprints e histórico de entregas | [Plano de Sprints](05-sprints.md) |
| **Métricas de IA & Evals** | Harness de avaliação offline, matriz de acurácia e benchmarks de prompt | [Evals da IA](evals/LEIAME.md) |
| **Operação & Nuvem** | Guia passo a passo de deploy na VPS com Gemini real e SSL | [Deploy em Nuvem](deploy-vps.md) |

---

## 🚀 Como Rodar Localmente em 5 Minutos

O repositório é 100% autocontido e não requer chaves de API nem configurações manuais para execução local:

```bash
git clone https://github.com/cristianbrunone/helpdesk-inteligente.git
cd helpdesk-inteligente
docker compose up --build
```

Abra [http://localhost:8080](http://localhost:8080) e faça login com os usuários de demonstração do seed:

- **Atendente (acesso total):** `ana.suporte@example.com` / `HelpDesk@2026`
- **Solicitante (visão restrita):** `marina.costa@example.com` / `HelpDesk@2026`

---

## 📊 Matriz de Qualidade e Testes

O projeto conta com mais de **770 testes automatizados** distribuídos em pirâmide de testes completa:

```
          / \
         / E2E \         Testes de ponta a ponta (Playwright)
        /------- \       Fluxos críticos, responsividade 375px e IA
       / Integra- \      Testcontainers (PostgreSQL real, pgvector)
      /    ção     \     Concorrência xmin, isolamento e SSE
     /--------------\
    /   Unitários    \   Domínio puro, máquinas de estado, guardrails,
   /                  \  mascaramento PII e validadores
  ----------------------
```

- **482 testes unitários:** cobertura de domínio, validador de IA, mascaramento e serviços de segurança.
- **292 testes de integração:** PostgreSQL com Testcontainers, transações reais, concorrência otimista (`xmin`) e SSE.
- **Testes E2E (Playwright):** validação dos fluxos do usuário em desktop e mobile (375 px).
- **CI 100% verde no GitHub Actions:** executando build, linters, testes unitários, de integração e smoke test do Compose sem `.env`.
