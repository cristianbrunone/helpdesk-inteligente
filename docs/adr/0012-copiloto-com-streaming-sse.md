# ADR-0012 — Copiloto com streaming via Server-Sent Events

- **Status:** Aceita
- **Data:** 2026-09-30
- **Fase:** 3 — Estratégia de comunicação
- **Requisitos relacionados:** RF-20..22, NFR-04, NFR-14, P-08

## Contexto

O copiloto é o ponto do produto que mais expõe a experiência de **IA conversacional**, o foco da vaga. Uma resposta com tool calling pode levar de 5 a 15 segundos (rodadas de ferramenta + geração). Sem feedback, o atendente fica olhando um spinner sem saber se algo está acontecendo. O canal é unidirecional depois da pergunta: o cliente envia uma mensagem e o servidor devolve uma sequência de eventos.

## Alternativas consideradas

### A) Requisição/resposta simples (JSON completo ao final)
- ✅ É o mais simples de implementar, testar e tratar erros (o status HTTP é definitivo).
- ✅ O fake é trivial.
- ❌ O usuário espera até 15 s sem nenhum feedback. A percepção de latência é ruim.
- ❌ As etapas de ferramenta ficam invisíveis, e perde-se a transparência sobre *o que* a IA consultou.
- ❌ Não é o padrão de UX que se espera de um assistente conversacional.

### B) Server-Sent Events (SSE) sobre `POST`
- ✅ O texto aparece à medida que é gerado, e o *time-to-first-token* vira a latência percebida.
- ✅ **Eventos tipados** (`ferramenta`, `delta`, `fontes`, `fim`, `erro`): a UI mostra "Buscando chamados semelhantes…", o que dá transparência sobre as ações do agente.
- ✅ Funciona sobre HTTP comum: passa por proxy e Nginx sem upgrade de protocolo e é suportado nativamente pelo ASP.NET Core.
- ✅ O `Microsoft.Extensions.AI` oferece streaming com invocação de ferramentas (ADR-0005).
- ❌ O `EventSource` do navegador só faz `GET`. Com `POST`, o consumo é via `fetch` + `ReadableStream` (cerca de 40 linhas de parser no frontend).
- ❌ Erros no meio do stream não podem mudar o status HTTP (já foi enviado `200`), então exigem um evento de erro próprio.
- ❌ É preciso desligar o buffering no proxy (`proxy_buffering off` no Nginx).

Uma terceira opção foi descartada logo: **WebSocket**. Ele é bidirecional e persistente, e nenhuma das duas coisas é necessária. Traria custo de conexão, reconexão e infraestrutura sem benefício.

## Decisão

Escolhemos **B: SSE sobre `POST`**, com o contrato de eventos definido em [`04-contratos-api.md`](../04-contratos-api.md#post-apichamadosidcopiloto-conversar-com-o-copiloto-sse).

**Plano de degradação:** se o prazo apertar, o mesmo endpoint pode emitir um único `delta` com a resposta completa seguido de `fim`. **O contrato não muda**, e o frontend continua funcionando.

## Trade-offs aceitos

- É preciso escrever um parser de SSE no frontend (ou usar uma biblioteca pequena, como `@microsoft/fetch-event-source`).
- Os testes de integração precisam ler o stream e verificar a sequência de eventos.

## Consequências

- A configuração do Nginx do frontend desliga o buffering na rota `/api/chamados/*/copiloto`.
- O fake do chat suporta streaming em pedaços e uma sequência simulada de "tool call → resultado → resposta".
- O `CancellationToken` da requisição é propagado até o provedor: se o atendente fecha o painel, a geração é interrompida e para de consumir cota.
- A telemetria (`uso_llm`) é gravada no evento `fim`, ou no erro.
- **Gatilho de reavaliação:** necessidade de o servidor iniciar mensagens (por exemplo, "a triagem terminou") → avaliar SignalR para notificações em geral. Nesse caso, o polling da triagem também migraria.
