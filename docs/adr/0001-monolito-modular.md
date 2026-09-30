# ADR-0001 — Monólito modular com dois hosts (API + Worker)

- **Status:** Aceita
- **Data:** 2026-09-30
- **Fase:** 2 — Estilo arquitetural
- **Requisitos relacionados:** RF-02, NFR-01, NFR-08, NFR-13, D1, D6, D7

## Contexto

O sistema tem dois perfis de carga bem diferentes:

- **Interativo:** CRUD de chamados, listagem e dashboard. Rápido e previsível.
- **Lento e falível:** triagem por LLM e geração de embeddings. Leva de segundos a dezenas de segundos, sofre rate limit e pode falhar.

O enunciado exige subir tudo com um único `docker compose up`, com um prazo de 7 dias e um dev solo. Ao mesmo tempo, o trabalho de IA não pode degradar a API (D1).

## Alternativas consideradas

### A) Microsserviços por domínio (Chamados, Triagem, Conhecimento, Copiloto)
Cada serviço com seu deploy e, idealmente, seu banco, comunicando-se por eventos.
- ✅ Isolamento máximo e escala independente por domínio.
- ✅ "Parece" moderno.
- ❌ Consistência distribuída: o chamado e a triagem pendente não ficam mais na mesma transação, então passa a ser necessário outbox, idempotência e sagas.
- ❌ Contratos entre serviços, versionamento, mais contêineres e mais pontos de falha no Compose.
- ❌ Desproporcional para o domínio (5 entidades) e para o prazo. O avaliador tende a ler isso como over-engineering.

### B) Monólito modular com dois processos: API e Worker
Uma única solução .NET com módulos internos bem delimitados e dois *hosts*: a API (HTTP) e o Worker (background). Os dois compartilham o Domain, a Application e a Infrastructure, e usam o mesmo banco.
- ✅ A transação local garante que o chamado e a triagem `pendente` nasçam juntos (sem dual-write).
- ✅ A latência e as falhas do LLM ficam isoladas no Worker, e a API segue saudável mesmo com o provedor fora do ar.
- ✅ O Worker escala de forma independente (réplicas + `SKIP LOCKED`, ver ADR-0003).
- ✅ Um único build, um único conjunto de testes e um Compose simples.
- ❌ O banco é compartilhado: um módulo *pode* violar a fronteira de outro (mitigado por convenção de namespaces e testes de arquitetura).
- ❌ O deploy é acoplado: uma mudança no domínio exige redeploy dos dois hosts.

## Decisão

Escolhemos **B: monólito modular com dois hosts**.

Os módulos (Chamados, Triagem, Conhecimento, Copiloto e Dashboard) são pastas e namespaces dentro das camadas, com dependências explícitas. O Worker é um *Worker Service* .NET que roda em um contêiner próprio.

## Trade-offs aceitos

- Não há isolamento de falha entre módulos no mesmo processo (por exemplo, um bug no Copiloto pode derrubar a API). Isso é aceitável porque o Copiloto é somente leitura e síncrono ao atendente.
- A fronteira entre módulos depende de disciplina, não de rede.

## Consequências

- A solução terá os projetos `Domain`, `Application`, `Infrastructure`, `Api` e `Worker` (detalhados no ADR-0002).
- O Compose terá `db`, `api`, `worker` e `web`.
- Testes de arquitetura (NetArchTest ou equivalente) verificam a regra de dependência entre camadas.
- **Gatilho de reavaliação:** equipes distintas mantendo módulos diferentes, ou uma necessidade real de escalar o Copiloto separadamente da API. Nesse caso, o módulo já delimitado seria extraído primeiro.
