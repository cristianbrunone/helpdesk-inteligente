# DECISOES.md

Resumo das principais decisões técnicas. Cada linha aponta para um ADR completo em [`docs/adr/`](docs/adr/), com as alternativas comparadas, os trade-offs e o gatilho de reavaliação. A narrativa de como o projeto foi construído está em [`docs/JORNADA.md`](docs/JORNADA.md).

## Decisões de arquitetura

| # | Decisão | Alternativa rejeitada | Trade-off principal |
|---|---|---|---|
| [0001](docs/adr/0001-monolito-modular.md) | Monólito modular com dois hosts (API + Worker) | Microsserviços | Deploy acoplado em troca de consistência transacional e simplicidade. |
| [0002](docs/adr/0002-clean-architecture-pragmatica.md) | Clean Architecture pragmática (camadas + features) | Vertical Slice pura | Mais cerimônia em troca de um domínio puro e testável. |
| [0003](docs/adr/0003-fila-em-tabela-postgres.md) | Fila em tabela PostgreSQL (`SKIP LOCKED`) | RabbitMQ/Redis + outbox | 1–2 s de latência de pickup em troca de zero infraestrutura extra. |
| [0004](docs/adr/0004-triagem-pipeline-rag-deterministico.md) | Triagem = pipeline RAG determinístico; tool calling só no copiloto | Agente com tools na triagem | Menos flexibilidade em troca de custo, latência e testes previsíveis. |
| [0005](docs/adr/0005-abstracao-provedor-llm.md) | Microsoft.Extensions.AI + endpoint OpenAI-compatível | SDK nativo por provedor | Sem recursos exclusivos de provedor em troca de um adaptador para vários provedores. |
| [0006](docs/adr/0006-gemini-free-tier-e-lgpd.md) | Gemini free tier opt-in + mascaramento tipado | Apenas Ollama ou tier pago | Regex não é DLP completo; documentado como limitação. |
| [0007](docs/adr/0007-pgvector-no-postgres.md) | pgvector no próprio PostgreSQL | Banco vetorial dedicado (Qdrant) | Vetores competem com o OLTP em troca de consistência transacional e zero infraestrutura extra. |
| [0008](docs/adr/0008-busca-textual-pg-trgm.md) | Busca com `pg_trgm` (substring, sem acento) | Full-text search (`tsvector`) | Sem ranking por relevância em troca de achar códigos e trechos parciais. |
| [0009](docs/adr/0009-sql-explicito-no-dashboard.md) | SQL explícito no dashboard; EF Core nas escritas | Tudo em LINQ | Duas formas de acesso a dados em troca de SQL visível e idiomático. |
| [0010](docs/adr/0010-filas-derivadas-do-estado.md) | Filas derivadas do estado + reconciliador | Tabela genérica de jobs | Varredura periódica em troca de fonte única de verdade e autocorreção. |
| [0011](docs/adr/0011-estrategia-de-embeddings.md) | Tabela única de documentos RAG, 768 dimensões | Coluna de embedding nas tabelas de negócio | Precisão máxima do modelo em troca de compatibilidade entre fake e real. |
| [0012](docs/adr/0012-copiloto-com-streaming-sse.md) | Copiloto com streaming SSE | Resposta JSON completa | Parser de SSE e erro no meio do stream em troca de UX conversacional e transparência das ferramentas. |
| [0013](docs/adr/0013-minimal-apis.md) | Minimal APIs com route groups | Controllers (MVC) | Organização por convenção própria em troca de endpoints finos por construção. |
| [0014](docs/adr/0014-fluxo-git-trunk-based.md) | Trunk-based com uma branch por sprint, PR e merge commit | GitFlow clássico | Sem área de integração separada em troca de uma `main` sempre entregável e sprints visíveis como PRs. |

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
