# DECISOES.md

Resumo das principais decisões técnicas. Cada linha aponta para um ADR completo em [`docs/adr/`](docs/adr/), com as alternativas comparadas, os trade-offs e o gatilho de reavaliação. A narrativa de como o projeto foi construído está em [`docs/JORNADA.md`](docs/JORNADA.md).

## Decisões de arquitetura

| # | Decisão | Alternativa rejeitada | Trade-off principal |
|---|---|---|---|
| [0001](docs/adr/0001-monolito-modular.md) | Monólito modular com dois hosts (API + Worker) | Microsserviços | Deploy acoplado em troca de consistência transacional e simplicidade. |
| [0002](docs/adr/0002-clean-architecture-pragmatica.md) | Clean Architecture pragmática (camadas + features) | Vertical Slice pura | Mais cerimônia em troca de um domínio puro e testável. |
| [0003](docs/adr/0003-fila-em-tabela-postgres.md) | Fila em tabela PostgreSQL (`SKIP LOCKED`) | RabbitMQ/Redis + outbox | 1–2 s de latência de pickup em troca de zero infraestrutura extra. |
| [0004](docs/adr/0004-triagem-pipeline-rag-deterministico.md) | Triagem = pipeline RAG determinístico; tool calling só no copiloto | Agente com tools na triagem | Menos flexibilidade em troca de custo, latência e testes previsíveis. |
| [0005](docs/adr/0005-abstracao-provedor-llm.md) | Microsoft.Extensions.AI + endpoint OpenAI-compatível (validada na PoC: `gemini-3.5-flash-lite`; tool calling no Gemini 3 com preservação da `thought_signature`) | SDK nativo por provedor | Sem recursos exclusivos de provedor em troca de um adaptador para vários provedores. |
| [0006](docs/adr/0006-gemini-free-tier-e-lgpd.md) | Gemini free tier opt-in + mascaramento tipado | Apenas Ollama ou tier pago | Regex não é DLP completo; documentado como limitação. |
| [0007](docs/adr/0007-pgvector-no-postgres.md) | pgvector no próprio PostgreSQL | Banco vetorial dedicado (Qdrant) | Vetores competem com o OLTP em troca de consistência transacional e zero infraestrutura extra. |
| [0008](docs/adr/0008-busca-textual-pg-trgm.md) | Busca com `pg_trgm` (substring, sem acento) | Full-text search (`tsvector`) | Sem ranking por relevância em troca de achar códigos e trechos parciais. |
| [0009](docs/adr/0009-sql-explicito-no-dashboard.md) | SQL explícito no dashboard; EF Core nas escritas | Tudo em LINQ | Duas formas de acesso a dados em troca de SQL visível e idiomático. |
| [0010](docs/adr/0010-filas-derivadas-do-estado.md) | Filas derivadas do estado + reconciliador | Tabela genérica de jobs | Varredura periódica em troca de fonte única de verdade e autocorreção. |
| [0011](docs/adr/0011-estrategia-de-embeddings.md) | Tabela única de documentos RAG, 768 dimensões (validada na PoC; normalização obrigatória) | Coluna de embedding nas tabelas de negócio | Precisão máxima do modelo em troca de compatibilidade entre fake e real. |
| [0012](docs/adr/0012-copiloto-com-streaming-sse.md) | Copiloto com streaming SSE | Resposta JSON completa | Parser de SSE e erro no meio do stream em troca de UX conversacional e transparência das ferramentas. |
| [0013](docs/adr/0013-minimal-apis.md) | Minimal APIs com route groups | Controllers (MVC) | Organização por convenção própria em troca de endpoints finos por construção. |
| [0014](docs/adr/0014-fluxo-git-trunk-based.md) | Trunk-based com uma branch por sprint, PR e merge commit | GitFlow clássico | Sem área de integração separada em troca de uma `main` sempre entregável e sprints visíveis como PRs. |

### Decisões de plataforma (Sprint 0)

| # | Decisão | Alternativa rejeitada | Trade-off principal |
|---|---|---|---|
| [0015](docs/adr/0015-migrations-em-servico-one-shot.md) | Migrations e seed num serviço one-shot (`migrator`) | Migrate no startup da API | Um contêiner a mais em troca de um único ponto de DDL e de falhas isoladas e visíveis. |
| [0016](docs/adr/0016-logs-estruturados-nativos.md) | Logging nativo do .NET em JSON no stdout, com `CorrelationId` no scope | Serilog | Formato de campos fixo e um middleware próprio em troca de zero pacotes e de logs ligados aos traces do OpenTelemetry sem ponte extra. |
| [0017](docs/adr/0017-ui-kit-mantine.md) | Mantine como biblioteca de UI (AppShell, estados, notificações e gráficos) | Tailwind CSS + shadcn/ui | Visual padrão e bundle maior em troca de estados, responsividade e acessibilidade prontos no prazo. |
| [0022](docs/adr/0022-ci-com-smoke-do-compose.md) | CI no GitHub Actions: backend, frontend e smoke test do `docker compose up` sem `.env` | Só build e testes | CI alguns minutos mais lento em troca de verificar o item 1 da DoD (clone limpo) em todo push. |
| [0023](docs/adr/0023-segredos-em-env-local.md) | Segredos em variáveis de ambiente via `.env` local + push protection, sem segredos no CI | Secrets em arquivo (`/run/secrets` + user-secrets) | Chave em texto puro no disco local em troca de ativação simples (uma linha) e defesa focada no risco real: vazamento. |

### Decisões de implementação (Sprint 1)

Decisões menores, que não contrariam nem acrescentam ADR, registradas para quem lê o código.

| Decisão | Motivo |
|---|---|
| Toda escrita concorrente sobre o chamado responde **412** `versao_desatualizada`: `If-Match` velho **ou** corrida detectada pelo `xmin` no `SaveChanges` | Um único código para o mesmo problema; o front trata um caso só (recarrega e avisa). O `03-modelo-de-dados.md` dizia 409 e foi alinhado ao contrato |
| Ordem das verificações nas escritas: 404 → 412 → 422 → 409 | A precondição HTTP é avaliada antes de qualquer outra coisa (RFC 9110) |
| Enums só como texto no JSON (`"Critica"`); número dá 400 | O contrato define strings; aceitar `2` em silêncio esconderia erro do cliente |
| Casos de uso registrados no host (`Api/ServicosAplicacao.cs`) | A Application segue sem nenhum pacote (ADR-0002); evita trazer `DependencyInjection.Abstractions` |
| A URL da lista usa os mesmos nomes e valores da query da API | Recarregar e compartilhar o link reproduzem a consulta; uma única conversão nos dois sentidos |
| Seed gerado pelo próprio domínio (`Abrir` → `MudarStatus` → `Comentar`) | Histórico, `resolvidoEm` e comentários coerentes por construção; se o seed violasse uma regra, o domínio recusaria |

### Adicionadas na revisão de arquitetura de 30/09

Uma revisão por pares confrontou o desenho com um catálogo de 25 padrões agênticos e um checklist de produção, antes de qualquer código de IA existir. Os detalhes estão em [`docs/revisoes/2026-09-30-padroes-agenticos.md`](docs/revisoes/2026-09-30-padroes-agenticos.md).

| # | Decisão | Alternativa rejeitada | Trade-off principal |
|---|---|---|---|
| [0018](docs/adr/0018-evals-offline-da-ia.md) | Evals offline: conjunto rotulado de ~30 casos + harness próprio, comparando sem RAG × com RAG | Só a métrica online (taxa de aceitação) | Exige chave e cota, então não roda no CI; em troca, a qualidade é medida antes de mudar. |
| [0019](docs/adr/0019-tracing-opentelemetry.md) | Tracing com OpenTelemetry, spans por etapa e Aspire Dashboard opcional | Só logs estruturados + `uso_llm` | 5 pacotes e um contêiner opcional em troca de ver onde cada segundo e cada token foram gastos. |
| [0020](docs/adr/0020-guardrail-de-saida-do-copiloto.md) | Guardrail de saída do copiloto: PII mascarada no stream e citações verificadas | Confiar só nos guardrails de entrada e de ação | ~64 caracteres de atraso no stream em troca de defesa em profundidade e de um *grounding check* determinístico. |
| [0021](docs/adr/0021-kill-switch-e-orcamentos-de-ia.md) | Kill switch por funcionalidade + limite de tokens por chamada | Usar `LLM_PROVIDER=fake` como desligamento | Mais configurações em troca de desligar com precisão, sem sugestões falsas, e de custo previsível. |

## Premissas assumidas

O enunciado permite registrar premissas aqui em vez de consultar o recrutador.

| # | Premissa |
|---|---|
| P-01 | Backend em C# / .NET 10 (LTS), a stack principal da vaga. |
| P-02 | `categoriaId` e `prioridade` são opcionais na criação (padrão: sem categoria e prioridade Média). A IA existe justamente para sugeri-las. |
| P-03 | Sem autenticação na v1. O atendente é identificado por um campo livre (`alteradoPor`, `autor`). |
| P-04 | "Refazer triagem" cria um novo registro de `TriagemIA`. A vigente é a mais recente, e o histórico é preservado. |
| P-05 | O LLM responde em português do Brasil. |
| P-06 | É permitido comentar em chamados Resolvidos. Só Fechado e Cancelado bloqueiam comentários. |
| P-07 | A ordenação por prioridade segue a ordem de negócio (Crítica > Alta > Média > Baixa). |
| P-08 | O histórico do copiloto é efêmero (não é persistido na v1). |
| P-09 | A mudança de status aceita um comentário opcional na mesma transação (o comentário de resolução alimenta o RAG). |
| P-10 | A criação do chamado registra o histórico `null → Aberto`. |
| P-11 | Estados finais também bloqueiam refazer e aceitar triagem. |

Detalhes e justificativas: [`docs/01-requisitos.md`](docs/01-requisitos.md#8-premissas-a-registrar-no-decisoesmd).
