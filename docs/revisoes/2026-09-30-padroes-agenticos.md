# Revisão de arquitetura: padrões agênticos e checklist de produção

- **Data:** 2026-09-30
- **Tipo:** revisão por pares do desenho (antes da implementação das sprints de IA)
- **Participantes:** o desenvolvedor, com a contribuição de um AI Engineer externo que sugeriu a referência
- **Estado do projeto na revisão:** Fases 1 a 3 concluídas (ADRs 0001–0014); Sprint 0 em andamento. **Nenhum código de IA implementado ainda**, então o custo de mudança é zero.
- **Resultado:** 4 decisões adotadas (ADRs 0018–0021), 1 mantida como opcional, 9 padrões rejeitados com justificativa. Plano atualizado para a v1.1 (`05-sprints.md`).

## 1. Motivação

Com o planejamento concluído, o desenho foi confrontado com um catálogo externo de **25 padrões de design de agentes** e um **checklist de produção** (design, controle, avaliação, observabilidade, runtime, segurança e operação). O catálogo se apoia em referências primárias que o projeto já usava: o artigo "Building effective agents" da Anthropic, o ReAct (Yao et al.) e o conceito de *lethal trifecta* de Simon Willison.

**Pergunta da revisão:** o desenho usa o padrão mais simples que resolve cada problema, e cobre o que se espera de um sistema de IA em produção?

## 2. Método

1. Para cada padrão do catálogo, classificar como **aplicado**, **rejeitado deliberadamente** (com motivo) ou **lacuna**.
2. Para cada área do checklist de produção, verificar a evidência no desenho (ADR, requisito ou contrato).
3. Para cada lacuna, estimar custo e valor, e decidir entre **adotar** (com ADR), **manter como opcional** ou **descartar**.
4. Encaixar o que for adotado na sprint em que o código relacionado nasce, e não numa sprint extra no final (o que ficaria por último seria o primeiro a ser cortado).

## 3. Achado principal

O catálogo abre com o **espectro de autonomia** (chamada única → cadeia de prompts → workflow → agente → multiagente) e a regra "comece o mais à esquerda que o problema permitir". O desenho já segue essa regra de forma explícita (ADR-0004):

- **Triagem = workflow** (*prompt chaining with gates*): uma sequência fixa com um gate de validação determinístico.
- **Copiloto = agente** (*tool use / ReAct*), aplicado só onde a sequência de passos não é conhecida de antemão.

## 4. Padrões já aplicados

| Padrão | Evidência no desenho |
|---|---|
| Prompt chaining com gates | Pipeline Mascarar → Recuperar → MontarPrompt → Completar → **Validar** (ADR-0004) |
| Tool use / ReAct | Copiloto com 4 ferramentas e invocação automática, no máximo 3 rodadas (ADR-0004, ADR-0012) |
| Tool design | Ferramentas com uma única função, parâmetros tipados e validados, saída limitada (`limite` de 1 a 5), somente leitura. A ferramenta de histórico **não recebe ID**: é *poka-yoke*, a chamada errada é impossível de expressar (contrato do copiloto) |
| Structured outputs | `json_schema` pedido ao provedor e validação própria **sempre** executada (ADR-0005) |
| Human-in-the-loop | Aceitar ou rejeitar a triagem. O copiloto não executa escrita (RF-13, RF-14, RF-22) |
| Guardrails de entrada | `TextoMascarado` (PII) e conteúdo do usuário delimitado no prompt (ADR-0006) |
| Guardrails de ação | Ferramentas somente leitura e rate limit no copiloto (ADR-0012) |
| Runtime de referência | A API devolve um ID imediatamente, há fila com workers (`SKIP LOCKED`), streaming (SSE) e estado no Postgres (ADR-0001, 0003, 0010, 0012) |
| Model gateway | Middleware de `IChatClient`: retry, timeout e contabilidade de custo em `uso_llm` (ADR-0005) |
| Memória | Trabalho = janela de contexto. Episódica = chamados resolvidos. Semântica = artigos no pgvector. Procedural = prompts versionados (ADR-0011) |
| Context engineering | Top-k limitado, limiar de similaridade, texto mascarado e truncado (ADR-0011) |
| Segurança: *lethal trifecta* | O copiloto acessa dados privados e lê conteúdo não confiável (as descrições dos chamados), mas **não se comunica com o exterior**. Uma das três pernas foi removida |
| Rollout em modo assistido | O produto **é** o modo assistido: a IA rascunha e o humano aprova |
| Versionar prompt, modelo e ferramentas juntos | `prompt_versao`, `provedor` e `modelo` gravados em cada triagem |

## 5. Padrões rejeitados deliberadamente

| Padrão | Motivo |
|---|---|
| Multiagente / supervisor-worker | Multiagente consome cerca de 15× os tokens de um chat. A triagem é uma classificação sem subtarefas separáveis. |
| Orchestrator-workers | Não há subtarefas cujo número dependa da entrada. |
| Planning / plan-and-execute / ReWOO | A triagem tem estrutura fixa (já é um "plano" em código). As perguntas do copiloto resolvem-se com 1 a 3 ferramentas. |
| Routing entre especialistas | Existe um único tipo de tarefa. O roteamento por modelo (barato × caro) fica como otimização de custo futura. |
| Tree of Thoughts / LATS | Custo muito alto. Um erro de triagem é barato e reversível (o humano rejeita). |
| Debate / consenso | Custo de 2 a 4× para uma decisão que já tem revisão humana. |
| Hierárquico | Não há vários agentes a coordenar. |
| Code as action (CodeAct) | Exigiria um sandbox de execução de código, sem necessidade no domínio. |
| Servidor MCP / A2A | Um servidor MCP se justifica quando **mais de um** agente ou produto consome as ferramentas. Hoje só o copiloto consome. Fica como próxima versão. |

## 6. Lacunas e decisões

| # | Lacuna (área do checklist) | Decisão | ADR | Sprint |
|---|---|---|---|---|
| L1 | **Avaliação:** só existe a métrica online (taxa de aceitação); não há conjunto de avaliação, métricas offline nem casos de segurança | **Adotar** evals offline, com comparação sem RAG × com RAG | [ADR-0018](../adr/0018-evals-offline-da-ia.md) | 3 |
| L2 | **Observabilidade:** logs estruturados e `uso_llm` existem, mas não há trace com spans aninhados por etapa | **Adotar** tracing com OpenTelemetry | [ADR-0019](../adr/0019-tracing-opentelemetry.md) | 2 |
| L3 | **Segurança:** a resposta do copiloto vai à tela sem verificação de saída | **Adotar** guardrail de saída (PII e fontes verificáveis) | [ADR-0020](../adr/0020-guardrail-de-saida-do-copiloto.md) | 4 |
| L4 | **Controle e operação:** não há kill switch por funcionalidade nem limite de tokens por chamada | **Adotar** kill switches e orçamentos | [ADR-0021](../adr/0021-kill-switch-e-orcamentos-de-ia.md) | 2 e 4 |
| L5 | **Qualidade:** uma triagem inválida vai direto para `Falhou` | **Opcional (Could):** uma nova tentativa corretiva que devolve ao modelo o erro de validação (*reflection* com crítico determinístico). Um ADR será escrito somente se for adotada | — | 3 (se houver folga) |

Custo estimado das adoções: ~3 h, absorvido pela folga do calendário. A linha de corte foi ajustada: **os evals ficam acima do E2E** em prioridade.

## 7. Itens do checklist de produção que ficam fora do escopo desta versão

Estes itens são registrados como "próxima versão" no README:

- *Circuit breaker* e *fallback* de modelo (outro provedor que passe nos mesmos evals).
- LLM-as-judge calibrado com rótulos humanos, para a `respostaSugerida` e o copiloto.
- Modo *shadow* e *canary* de prompts.
- Revisão semanal de traces e evals rodando em CI com bloqueio de deploy (exige orçamento de provedor pago).

## 8. Lições

- **Revisar o desenho antes do código muda decisões a custo zero.** As quatro adoções afetam sprints ainda não iniciadas.
- **Rejeitar com justificativa vale tanto quanto adotar.** A maior parte do catálogo não se aplica a este problema, e o registro explica por quê.
- **A lacuna mais relevante não era de funcionalidade, e sim de medição.** Sem evals, o RAG seria "uma funcionalidade implementada". Com evals, é "uma funcionalidade que melhorou a acurácia em X pontos".
