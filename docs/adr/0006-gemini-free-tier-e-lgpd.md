# ADR-0006 — Gemini free tier como provedor real de demonstração, com mascaramento obrigatório

- **Status:** Aceita
- **Data:** 2026-09-30
- **Fase:** 2 — Estilo arquitetural
- **Requisitos relacionados:** RN-10, RN-11, NFR-06, NFR-07, NFR-12, D3

## Contexto

Queremos um LLM real e gratuito para a demonstração. O Gemini tem free tier, mas com duas condições relevantes, verificadas em 2026-09-30:

1. **Uso dos dados.** No free tier, prompts e respostas **podem ser usados para melhorar os produtos Google**. No tier pago, não são ([Google AI — Billing](https://ai.google.dev/gemini-api/docs/billing)).
2. **Rate limit.** O free tier tem limites de requisições por minuto e por dia, que variam por modelo.

O enunciado exige mascarar e-mails, telefones e CPFs e nunca enviar o nome ou o e-mail do solicitante (LGPD).

## Alternativas consideradas

### A) Gemini free tier + mascaramento obrigatório + fake como padrão
- ✅ Custo zero para o avaliador testar com LLM real.
- ✅ Força o mascaramento a ser uma **garantia arquitetural**, não uma boa intenção. Ele é o controle que torna aceitável enviar texto a um tier que pode reter dados.
- ❌ O texto mascarado ainda pode conter dados sensíveis não cobertos pelos padrões (nomes de terceiros, endereços, números de contrato).
- ❌ O rate limit pode fazer a triagem falhar em demonstrações com muitos chamados.

### B) Apenas LLM local (Ollama) ou tier pago
- ✅ Nenhum dado sai da máquina (Ollama), ou não há uso dos dados para treinamento (pago).
- ❌ Ollama: exige GPU ou é lento na CPU, a imagem é pesada, e a experiência do avaliador piora (download de GBs no `docker compose up`).
- ❌ Tier pago: exige cartão e custo de quem testa.

## Decisão

Escolhemos **A** para a demonstração, deixando **B como recomendação explícita para produção**:

- O `docker compose up` usa o **fake** (nenhum dado sai do ambiente).
- O Gemini free tier é opt-in, via `.env`.
- O Ollama é documentado como alternativa 100% local (por ser um endpoint compatível, não exige código novo, ver ADR-0005).

O mascaramento é implementado como **um ponto único e obrigatório**: os clientes de chat e embedding recebem um tipo `TextoMascarado`, não uma `string`. Com isso, não compila enviar texto cru ao LLM.

## Trade-offs aceitos

- O mascaramento por regex cobre e-mail, telefone BR e CPF (os itens exigidos), mas não é um DLP completo. Isso fica documentado como limitação, e NER/DLP é citado como próxima versão.
- Sob rate limit, a triagem pode ir para `falhou` e ser refeita manualmente.

## Consequências

- `TextoMascarado` é um value object construído **apenas** pelo `MascaradorDadosPessoais`, e testado com casos positivos e negativos (formatos variados de CPF, telefone com e sem DDD, e-mails com subdomínio).
- Os logs das chamadas de IA registram apenas metadados (tamanho do texto, número de mascaramentos por tipo, latência, tokens), nunca o conteúdo.
- O índice vetorial só recebe `TextoMascarado` (RN-11).
- Uma seção do README explica a diferença entre free e pago e recomenda o tier pago, a Vertex AI ou o Ollama para dados reais.
- **Gatilho de reavaliação:** uso com dados reais de clientes. Nesse caso, o tier pago ou o provedor local passa a ser obrigatório, e NER entra como segunda camada de mascaramento.
