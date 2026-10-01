# Relatórios de eval da IA

Relatórios gerados pelo harness `tools/HelpDesk.Evals` ([ADR-0018](../adr/0018-evals-offline-da-ia.md)) sobre o conjunto rotulado de [`evals/triagem`](../../evals/triagem/LEIAME.md). Cada arquivo é gerado pelo harness e não é editado à mão; a análise e as decisões ficam aqui.

## Como rodar

```bash
# Smoke com o fake (é o que o CI roda): não mede qualidade, garante que o harness funciona
LLM_PROVIDER=fake dotnet run --project tools/HelpDesk.Evals -- --rag off --repeticoes 1

# Medição real: variáveis do provedor no ambiente (as mesmas do Worker) e, com RAG, o banco já indexado
# pelo mesmo modelo de embedding (rode o Worker no host com esse provedor até o reconciliador terminar)
ConnectionStrings__Default="Host=localhost;Port=55432;Database=helpdesk;Username=helpdesk;Password=helpdesk_dev" \
  dotnet run --project tools/HelpDesk.Evals -- --rag on --repeticoes 3 --intervalo-ms 4500
```

`--rag off` usa a `triagem.v1` (linha de base) e `--rag on`, a `triagem.v2`. `--intervalo-ms` espaça as execuções para o limite por minuto do free tier. As opções completas aparecem com um argumento inválido (ex.: `-- --ajuda`).

## Histórico

| Data | Relatório | Provedor | Resumo |
|---|---|---|---|
| 01/10/2026 | [triagem.v1 sem RAG](2026-10-01-triagem-v1-sem-rag.md) | Gemini `gemini-3.5-flash-lite` | Linha de base: categoria 86/90, prioridade 82/90, segurança 5/5 |
| 01/10/2026 | [triagem.v2 com RAG](2026-10-01-triagem-v2-com-rag.md) | Gemini + `gemini-embedding-001` | Categoria 90/90, prioridade 78/90, segurança 5/5 |

## 01/10/2026: triagem.v1 × triagem.v2 (primeira medição)

| Métrica | v1 sem RAG | v2 com RAG |
|---|---|---|
| Acurácia de categoria | 95,6% (86/90) | **100% (90/90)** |
| Categoria pass^3 | 28/30 | **30/30** |
| Acurácia de prioridade | **91,1% (82/90)** | 86,7% (78/90) |
| Prioridade pass^3 (held-out) | **26/30 (8/10)** | 23/30 (6/10) |
| Saída válida | 100% | 100% |
| Segurança (injeção e PII) | 5/5 | 5/5 |
| Latência p50 / p95 | 1,2 s / **4,0 s** | 1,7 s / 19,8 s |
| Tokens por triagem | **881** | 1.674 |

**Leitura:**

- **Categoria:** o RAG corrigiu os quatro erros da v1 (um caso ambíguo, uma injeção e duas execuções isoladas) e acertou todos os casos nas três execuções, inclusive os held-out.
- **Prioridade:** piorou num padrão identificável. Nos chamados com contorno ("consigo achar pela busca", "estamos usando a impressora do 2º andar"), o esperado é Média, e a v2 escolhe Baixa com mais frequência (4 casos contra 2 da v1). A regra "na dúvida, escolha a menor" parece pesar mais com o contexto no prompt.
- **Segurança:** intacta. Nenhuma injeção mudou a prioridade para a pedida e nenhum dado pessoal apareceu no prompt nem na resposta.
- **Custo:** o prompt da v2 tem quase o dobro de tokens (os trechos recuperados). Os tokens dos embeddings não entram na conta: o endpoint OpenAI-compatível do Gemini não os devolve.
- **Latência:** o p95 da v2 inclui novas tentativas. A v2 faz duas chamadas por triagem (embedding e chat) e esbarra mais no limite por minuto do free tier (429 com backoff, observado também na indexação). O harness ainda não separa o tempo de espera do tempo do modelo.

**Decisão:** a `triagem.v2` passa a ser a padrão. Pesou a categoria (a sugestão que o atendente mais usa ao aceitar) e as fontes visíveis no painel (RF-16), com a segurança intacta. A queda na prioridade fica registrada como o próximo alvo: uma `triagem.v3` com a regra "problema real com contorno = Média" explícita, ajustada sem olhar os casos held-out e medida pelo mesmo harness. A `triagem.v1` continua disponível por `TRIAGEM_PROMPT_VERSAO=triagem.v1`.

Com 30 casos, diferenças de 1 ou 2 acertos podem ser ruído: por isso cada caso roda três vezes e o relatório mostra a contagem absoluta.
