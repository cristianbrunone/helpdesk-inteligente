# ADR-0020 — Guardrail de saída do copiloto: filtro de PII e fontes verificáveis

- **Status:** Aceita
- **Data:** 2026-09-30
- **Fase:** revisão de arquitetura ([registro](../revisoes/2026-09-30-padroes-agenticos.md), lacuna L3)
- **Requisitos relacionados:** RF-23 (novo), RF-20..22, RN-10, NFR-06, NFR-07

## Contexto

A triagem tem um guardrail de saída forte: toda resposta passa por schema e validação de domínio antes de ser gravada. O copiloto, por outro lado, envia texto livre gerado pelo modelo direto para a tela do atendente. Existem dois riscos:

1. **Vazamento de dados pessoais na saída.** As entradas são mascaradas, mas o modelo pode reproduzir ou "completar" um dado pessoal (por exemplo, a partir de um histórico que tenha escapado ao mascarador).
2. **Resposta sem fundamento.** O modelo pode citar um "chamado #877" que nenhuma ferramenta retornou. Isso é alucinação apresentada como fato, justamente no produto em que o atendente confia nas fontes.

A resposta vem em **streaming** (ADR-0012), o que complica qualquer verificação: um dado pessoal pode chegar dividido entre dois pedaços.

## Alternativas consideradas

### A) Confiar só nos guardrails de entrada e de ação
Mascaramento na entrada, ferramentas somente leitura com resultados mascarados.
- ✅ É o mais simples, sem nenhum custo de latência.
- ❌ Não há defesa em profundidade: uma falha do mascarador de entrada chega direto à tela.
- ❌ Não detecta citações inventadas.

### B) Filtro de saída no stream + verificação de citações
- **Filtro de PII:** os pedaços (`delta`) passam por um buffer com uma janela de retenção curta (~64 caracteres), para que um CPF ou e-mail dividido entre pedaços seja visto inteiro. O texto liberado passa pelo mesmo `MascaradorDadosPessoais`. No `fim`, o restante do buffer é liberado.
- **Verificação de citações:** o prompt exige citar chamados como `#numero` e artigos pelo título. Ao final, o servidor compara os `#numero` citados com os resultados **que as ferramentas realmente retornaram** nessa resposta. O evento `fontes` só contém fontes verificadas. Uma citação sem correspondência gera o evento `aviso`, e a UI mostra o selo "Contém referências não verificadas".
- ✅ Defesa em profundidade para a LGPD, reaproveitando um componente já testado.
- ✅ Um *grounding check* leve e determinístico, sem chamada extra ao LLM.
- ✅ A latência adicional é imperceptível (~64 caracteres de atraso no stream).
- ❌ A verificação de citações cobre só referências explícitas (`#numero`), não afirmações livres sem citação.
- ❌ O buffer adiciona um pouco de complexidade ao streaming.

Também foi considerado um **LLM-as-judge** para verificar o fundamento de cada afirmação. Foi descartado nesta versão: dobra o custo e a latência de cada resposta e exige calibração.

## Decisão

Escolhemos **B**.

- O `FiltroSaidaCopiloto` (na `Application`) encapsula o buffer, o mascaramento e a coleta de citações. Ele é testável sem streaming real.
- Um novo evento SSE, `aviso`, é adicionado ao contrato: `{"tipo":"referencia_nao_verificada","referencias":["#912"]}`.
- As ocorrências (quantas máscaras aplicadas na saída e quantas citações sem fonte) são registradas como atributos do span `copiloto.responder`, **sem** o conteúdo.

## Trade-offs aceitos

- Uma afirmação sem citação não é verificada. O prompt instrui a sempre citar, e o selo "gerado por IA" continua visível.
- Se o mascarador de saída atuar, é um sinal de falha em outra camada. Isso é registrado como métrica para investigação.

## Consequências

- **Testes unitários do filtro:** um CPF dividido em dois pedaços é mascarado; o texto sem PII passa intacto e na ordem; uma citação existente é verificada; uma citação inventada gera `aviso`.
- **Teste de integração:** o fake do copiloto é roteirizado para "vazar" um CPF e citar um chamado inexistente, e a resposta que chega ao cliente sai mascarada e com `aviso`.
- `04-contratos-api.md` recebe o evento `aviso`, e a UI recebe o selo.
- **Gatilho de reavaliação:** respostas longas com muitas afirmações sem citação → avaliar LLM-as-judge de *grounding* por amostragem.
