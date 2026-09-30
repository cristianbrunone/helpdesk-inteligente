# ADR-0004 — Triagem como pipeline RAG determinístico; tool calling só no copiloto

- **Status:** Aceita
- **Data:** 2026-09-30
- **Fase:** 2 — Estilo arquitetural
- **Requisitos relacionados:** RF-10, RF-11, RF-15, RF-16, RF-20..22, NFR-03, NFR-05, NFR-10, NFR-12, D2, D5

## Contexto

A triagem precisa de contexto além do texto do chamado (casos semelhantes resolvidos e artigos da base de conhecimento) para sugerir melhor a categoria, a prioridade e a resposta. Há duas formas de dar esse contexto ao LLM: **nós** buscamos e injetamos (RAG clássico), ou o **modelo** decide o que buscar via ferramentas (agente).

A triagem roda **para todo chamado**, em background, sem humano olhando, e sua saída precisa passar por validação estrita.

## Alternativas consideradas

### A) Agente com tool calling na triagem
O LLM recebe o chamado e ferramentas (`buscar_chamados_similares`, `buscar_artigos`, `listar_categorias`) e decide quantas vezes chamar cada uma antes de responder.
- ✅ É flexível: o modelo busca mais quando o caso é ambíguo.
- ✅ Demonstra tool calling.
- ❌ **Não é determinístico:** o número de chamadas varia, e com ele a latência, o custo e os tokens.
- ❌ Multiplica as chamadas ao LLM (de 2 a 4 por triagem), o que é crítico no free tier com rate limit.
- ❌ É mais difícil de testar: o fake precisa simular sequências de tool calls, e os cenários de falha se multiplicam.
- ❌ Se o modelo "decide" não buscar, a sugestão perde o fundamento, sem nenhum sinal disso.

### B) Pipeline RAG determinístico
Os passos são fixos: mascarar → gerar o embedding → buscar os top-k (chamados resolvidos + artigos) → montar o prompt → **uma** chamada ao LLM com saída estruturada → validar.
- ✅ Uma chamada ao LLM por triagem, com custo e latência previsíveis.
- ✅ Cada etapa é testável isoladamente (mascaramento, recuperação, montagem de prompt, parsing).
- ✅ As fontes usadas são conhecidas **antes** da chamada, então é trivial registrá-las e exibi-las (RF-16).
- ✅ Encaixa-se naturalmente no Worker assíncrono (ADR-0003).
- ❌ A recuperação é "cega": ela sempre busca o top-k, mesmo quando é desnecessário.
- ❌ Não se adapta a casos que pediriam uma segunda busca.

## Decisão

- **Triagem → B (pipeline RAG determinístico).** É uma tarefa de **classificação** repetitiva, em background, com saída validada. Previsibilidade, custo e testabilidade pesam mais que flexibilidade.
- **Copiloto → tool calling (A).** Ele é **conversacional**, o atendente está presente, as perguntas são abertas ("já tivemos casos assim?", "qual o tempo médio dessa categoria?"), e o modelo precisa escolher qual dado buscar. É o lugar onde a flexibilidade do agente paga o custo.

Princípio: **usar agente só onde a sequência de passos não é conhecida de antemão.**

## Trade-offs aceitos

- A triagem não se autocorrige com buscas adicionais. Mitigação: o top-k e o limiar de similaridade são configuráveis, e o atendente pode "Refazer".
- Existem dois estilos de integração com o LLM no código. A abstração do ADR-0005 cobre os dois.

## Consequências

- O **pipeline de triagem** é uma classe da `Application` com etapas explícitas e logadas: `Mascarar → Recuperar → MontarPrompt → Completar → Validar`.
- As **ferramentas do copiloto** são somente leitura, com parâmetros validados e resultados limitados (máximo de N itens). Elas também passam pelo mascaramento antes de voltar ao LLM.
- O **fake do LLM** suporta dois modos: uma resposta estruturada (triagem) e uma sequência "tool call → resposta" (copiloto), para testar os dois fluxos sem chave de API.
- O prompt da triagem fica versionado em arquivo (`prompts/triagem.v1.md`), e a versão é gravada em cada `TriagemIA`.
- **Gatilho de reavaliação:** se a taxa de rejeição (RF-43) for alta em categorias ambíguas, avaliar um passo de *query rewriting* ou uma segunda recuperação condicional. Continua sendo pipeline, não agente.
