# ADR-0021 — Kill switches por funcionalidade e orçamentos de IA

- **Status:** Aceita
- **Data:** 2026-09-30
- **Fase:** revisão de arquitetura ([registro](../revisoes/2026-09-30-padroes-agenticos.md), lacuna L4)
- **Requisitos relacionados:** RF-18 e RF-24 (novos), NFR-04, NFR-12

## Contexto

Duas perguntas operacionais não tinham resposta no desenho:

1. **Como desligar a IA rapidamente,** sem derrubar o sistema? Por exemplo, se o provedor começar a devolver respostas ruins, se a cota acabar ou se houver um incidente de segurança.
2. **Qual é o custo máximo de uma chamada?** Já havia timeout, retries e limite de rodadas de ferramenta, mas nenhum **limite de tokens** por chamada. Uma resposta longa demais do copiloto, ou uma triagem com uma `respostaSugerida` enorme, consumiria cota sem controle.

## Alternativas consideradas

### A) Usar `LLM_PROVIDER=fake` como "desligamento"
- ✅ Já existe, sem código novo.
- ❌ **É perigoso:** em produção, o fake geraria sugestões falsas que pareceriam reais para o atendente.
- ❌ Desliga tudo ao mesmo tempo: não permite manter a triagem e desligar só o copiloto.
- ❌ Não resolve o limite de tokens.

### B) Flags por funcionalidade + orçamentos explícitos por chamada
- **Kill switches:** `IA_TRIAGEM_HABILITADA` e `IA_COPILOTO_HABILITADO` (padrão `true`).
- **Orçamentos:** `TRIAGEM_MAX_TOKENS_SAIDA` (padrão 600), `COPILOTO_MAX_TOKENS_SAIDA` (padrão 800), além dos limites já existentes (`LLM_TIMEOUT_SECONDS`, `LLM_MAX_RETRIES`, 3 rodadas de ferramenta, 20 mensagens por requisição).
- ✅ Desliga com precisão, e o sistema continua funcionando **honestamente**: sem sugestão, em vez de com sugestão falsa.
- ✅ O custo máximo por chamada fica previsível e documentado.
- ❌ Adiciona mais configurações. Hoje as flags só mudam com um restart (seria aceitável ler a cada requisição, mas o ganho não justifica).

## Decisão

Escolhemos **B**, com este comportamento:

| Situação | Comportamento |
|---|---|
| `IA_TRIAGEM_HABILITADA=false` na criação | O chamado é criado **sem** triagem (`triagem: null`), e a UI mostra "Triagem por IA desativada". Nenhuma triagem pendente fica acumulada. |
| `IA_TRIAGEM_HABILITADA=false` com triagens pendentes | O Worker **para de consumir** a fila. As pendentes permanecem e são processadas quando a flag voltar. O `/health` reporta `Degraded`, com o motivo "triagem desativada". |
| "Refazer triagem" com a triagem desativada | **503** `ia_indisponivel`. |
| `IA_COPILOTO_HABILITADO=false` | O endpoint devolve **503** `ia_indisponivel`, e a UI esconde o painel do copiloto. |
| A resposta atinge `*_MAX_TOKENS_SAIDA` | **Triagem:** o JSON truncado falha na validação e vira `Falhou` ("resposta excedeu o limite"). **Copiloto:** o stream termina com `aviso` `{"tipo":"resposta_truncada"}`. |

A API expõe o estado das flags em `GET /api/config/ia` (`{ "triagem": true, "copiloto": true }`) para o frontend adaptar a interface.

## Trade-offs aceitos

- A troca de uma flag exige reiniciar o contêiner (segundos no Compose). Feature flags dinâmicas ficam como evolução.
- Os limites de tokens padrão são estimativas iniciais. O harness de evals (ADR-0018) mede o consumo real, e os valores são ajustados com base nele.

## Consequências

- **Testes de integração:** com a triagem desativada, a criação não gera triagem, o "refazer" devolve 503 e o Worker não consome a fila. Com o copiloto desativado, o endpoint devolve 503.
- As variáveis entram no `.env.example`, com comentários.
- O README documenta o "procedimento de emergência": qual flag desligar em cada cenário.
- **Gatilho de reavaliação:** operação em produção com mais de um ambiente → migrar para feature flags dinâmicas (Azure App Configuration ou similar).
