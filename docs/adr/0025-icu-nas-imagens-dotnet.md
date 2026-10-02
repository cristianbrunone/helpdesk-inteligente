# ADR-0025 — Instalar o ICU nas imagens do .NET, em vez de rodar em globalização invariante

- **Status:** Aceita
- **Data:** 2026-10-02
- **Fase:** 5 — Sprint 5 (hardening)
- **Requisitos relacionados:** RN-10, RN-11, NFR-05, NFR-06
- **Decisões relacionadas:** [ADR-0006](0006-gemini-free-tier-e-lgpd.md), [ADR-0015](0015-migrations-em-servico-one-shot.md)

## Contexto

As imagens Alpine do .NET (`aspnet` e `runtime`) vêm **sem ICU** e com `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true`. Nesse modo, duas operações das quais o código depende **não funcionam** para texto com acento:

- `string.Normalize(NormalizationForm.FormD)` devolve o texto sem decompor ("Não" continua com o "ã"), então remover acentos vira um no-op;
- a comparação com `CompareOptions.IgnoreNonSpace` deixa de ignorar acentos ("duvida" ≠ "Dúvida").

Oito pontos do código fazem isso: o **mascarador de nomes** (o nome "João" não casa com "Joao" no texto, RN-10), o **validador da saída da IA** (um "Média" do modelo pode não casar com `Media`), o embedding fake, o fake da triagem e do copiloto, o seed, a resolução de categoria das ferramentas do copiloto e a verificação de títulos de artigo no guardrail.

O problema ficou escondido porque **os testes rodam com ICU** (Windows local e runner Ubuntu do CI). Foi o E2E da Sprint 5 que o revelou: em "Não consigo emitir o boleto", o fake não reconhecia "não consigo", e a prioridade saía Média em vez de Alta. A reprodução numa imagem Alpine com o modo invariante confirmou a causa.

## Alternativas consideradas

### A) Instalar o ICU nas imagens e desligar o modo invariante
Copiar as bibliotecas e os dados do ICU para as imagens finais e definir `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false`.
- ✅ Produção passa a se comportar como os testes: o mesmo código, o mesmo resultado.
- ✅ Uma mudança só no Dockerfile, sem tocar no código nem nos testes.
- ✅ Vale para usos futuros de cultura (ordenação, formatação pt-BR).
- ❌ +58 MB por imagem (bibliotecas do ICU e os dados completos).

### B) Código independente do ICU
Uma tabela própria de remoção de acentos (Latin-1 e Latin Extended) nos oito pontos, troca das comparações com `CompareOptions` por comparação ordinal sobre o texto já sem acento, e os testes rodando em modo invariante (`InvariantGlobalization`) para espelhar produção.
- ✅ Imagens menores e comportamento idêntico em qualquer ambiente, com ou sem ICU.
- ❌ Mais código para manter, e uma tabela incompleta erra em silêncio (justamente no mascaramento de dados pessoais).
- ❌ Muda oito pontos do código no fim do projeto, com risco de regressão.

## Decisão

Escolhemos **A**. O ICU é **copiado da imagem do SDK** (estágio `build`, mesmo Alpine 3.23, onde já vem instalado), para duas bases de runtime (`base-aspnet` e `base-runtime`): as imagens finais não fazem `apk add`, então o build não precisa de rede nem da CA corporativa nesses estágios, e a versão do ICU é a mesma do SDK.

## Trade-offs aceitos

- Imagens maiores: API 204 → 263 MB; Worker e Migrator 165 → 223 MB.
- A versão do ICU acompanha a da imagem do SDK; uma atualização do SDK pode trazer outra versão do ICU (o smoke detecta se a remoção de acentos parar de funcionar).

## Consequências

- `Dockerfile`: estágios `base-aspnet` e `base-runtime` com o ICU; `api`, `worker` e `migrator` partem deles.
- Smoke (`scripts/smoke-compose.sh`): verificação "texto acentuado" — um chamado com "Não consigo" recebe prioridade Alta do fake, o que só acontece se o Worker remover acentos.
- **Gatilho de reavaliação:** se o tamanho das imagens passar a importar (por exemplo, cold start em serverless), ou se o projeto adotar imagens *chiseled* sem shell, revisitar a alternativa B, com os testes em modo invariante.
