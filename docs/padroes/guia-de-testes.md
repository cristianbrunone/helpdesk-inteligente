# Guia de testes

> **Regra de ouro:** todo comportamento novo vem com teste, e todo teste novo é **quebrado de propósito uma vez** antes de ir para o PR. Um teste que nunca falhou pode estar passando por acidente.

## As camadas

| Camada | Ferramenta | O que cobre | Onde |
|---|---|---|---|
| Arquitetura | NetArchTest | A regra de dependência (Domain ← Application ← Infrastructure), nos tipos e nos `.csproj` | `tests/HelpDesk.ArchitectureTests` |
| Unitários | xUnit v3 + Shouldly | Regras de domínio, mascaramento, validação da saída da IA, guardrails, fakes, resiliência. **Sem I/O** | `tests/HelpDesk.UnitTests` |
| Integração | `WebApplicationFactory` + Testcontainers | API, SQL, índices, filas, reconciliador, busca vetorial: contra o **PostgreSQL real** (imagem `pgvector/pgvector`) | `tests/HelpDesk.IntegrationTests` |
| Frontend | Vitest + Testing Library + MSW | Componentes e páginas nos estados carregando, vazio, erro e sucesso; a API é simulada pelo MSW | `web/src/**/*.test.tsx` |
| Smoke | `bash` + `curl` | Os critérios de aceite contra o `docker compose` de pé, passando pelo Nginx | `scripts/smoke-compose.sh` |
| E2E | Playwright | Fluxos no navegador real contra o compose: triagem, copiloto e telas em 375 px | `web/e2e/` |

Cada camada pega um tipo de erro que as outras não pegam. Na Sprint 5, o E2E encontrou dois bugs de produção que passavam em todos os unitários e de integração: o Nginx enfraquecia o ETag (toda escrita pelo navegador dava 412) e as imagens .NET rodavam sem ICU (acentos não eram removidos). **Teste o caminho que o usuário usa.**

## Que teste escrever

- **Regra de negócio** (transição de status, validação, cálculo): unitário no Domain ou na Application.
- **Consulta SQL, constraint, índice, concorrência**: integração com Testcontainers. Nunca banco em memória: ele não tem `CHECK`, `SKIP LOCKED`, `pg_trgm` nem `pgvector`.
- **Endpoint**: integração com `WebApplicationFactory`, conferindo status, ProblemDetails e cabeçalhos (`ETag`, `Retry-After`).
- **Tela**: Vitest com MSW, nos quatro estados; os botões que aparecem vêm do que a API devolve (`transicoesPermitidas`), nunca de regra replicada no front.
- **Integração entre serviços** (Nginx, Worker, compose): smoke ou E2E.

## Nomes

- Backend: `Metodo_Cenario_ResultadoEsperado`, por exemplo `Processar_CpfDivididoEntreDoisPedacos_SaiMascaradoEInteiroNuncaAparece`.
- Frontend e E2E: frase em português do comportamento, por exemplo `'mostra nos campos os erros 422 devolvidos pela API'`.

## Regras que evitam testes mentirosos

| Regra | Por quê |
|---|---|
| **A IA fica sempre no fake.** Testes com provedor real levam `[Trait("Category", "ProvedorReal")]` e não rodam no CI | Determinismo, custo zero e nenhuma chave no CI |
| **Datas fixas**, nunca `DateTimeOffset.UtcNow` no teste | O relógio do contêiner do banco pode estar segundos atrás do host; "agora" vira "no futuro" para a fila |
| **Massa controlada em banco isolado** quando o teste conta linhas (`BancoFixture.CriarBancoVazioAsync`) | Os testes rodam em paralelo no banco compartilhado |
| **`ActivityListener` é global**: filtre os spans pelo `TraceId` do teste | Outras classes de teste emitem os mesmos spans ao mesmo tempo |
| **MSW: segure a resposta com uma promise** liberada no fim do teste, nunca `delay('infinite')` | Uma resposta que nunca termina deixa o interceptador num estado em que as requisições dos testes seguintes escapam |
| **Sem retries no E2E** (`retries: 0`) | Um teste instável precisa aparecer, não ser escondido |
| **Teste de aceite com dado do jeito que o usuário manda**: acento, gzip, navegador | O `curl` sem `--compressed` não via o bug do ETag; o texto sem acento não via o bug do ICU |
| **Nada de `Thread.Sleep`/`Task.Delay` para "esperar dar certo"**: espere a condição (`waitFor`, polling com limite) | Tempo fixo é lento quando sobra e instável quando falta |

## Como rodar

```bash
bash scripts/testes.sh              # backend + frontend
bash scripts/testes.sh --completo   # + compose isolado, smoke e E2E
```

As suítes separadas, a cobertura e as variáveis do E2E estão no [README](../../README.md#rodar-os-testes). No CI, os três jobs rodam a cada push e publicam a cobertura no resumo da execução.

## Cobertura

O número é consequência, não meta. Ele serve para achar código **sem nenhum** teste (o Worker é o mais baixo, porque os `BackgroundService` são exercitados mais pelo smoke que pelos testes .NET). Um teste escrito só para subir a porcentagem, sem afirmar comportamento, é custo sem proteção.
