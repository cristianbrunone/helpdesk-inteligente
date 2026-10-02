# DECISOES.md

Resumo das principais decisões técnicas. Cada linha aponta para um ADR completo em [`docs/adr/`](docs/adr/), com as alternativas comparadas, os trade-offs e o gatilho de reavaliação. A narrativa de como o projeto foi construído está em [`docs/JORNADA.md`](docs/JORNADA.md).

## Decisões de arquitetura

| # | Decisão | Alternativa rejeitada | Trade-off principal |
|---|---|---|---|
| [0001](docs/adr/0001-monolito-modular.md) | Monólito modular com dois hosts (API + Worker) | Microsserviços | Deploy acoplado em troca de consistência transacional e simplicidade. |
| [0002](docs/adr/0002-clean-architecture-pragmatica.md) | Clean Architecture pragmática (camadas + features) | Vertical Slice pura | Mais cerimônia em troca de um domínio puro e testável. |
| [0003](docs/adr/0003-fila-em-tabela-postgres.md) | Fila em tabela PostgreSQL (`SKIP LOCKED`) | RabbitMQ/Redis + outbox | 1–2 s de latência de pickup em troca de zero infraestrutura extra. |
| [0004](docs/adr/0004-triagem-pipeline-rag-deterministico.md) | Triagem = pipeline RAG determinístico; tool calling só no copiloto | Agente com tools na triagem | Menos flexibilidade em troca de custo, latência e testes previsíveis. |
| [0005](docs/adr/0005-abstracao-provedor-llm.md) | Microsoft.Extensions.AI + endpoint OpenAI-compatível (validada na PoC: `gemini-3.5-flash-lite`; tool calling no Gemini 3 com preservação da `thought_signature`) | SDK nativo por provedor | Sem recursos exclusivos de provedor em troca de um adaptador para vários provedores. |
| [0006](docs/adr/0006-gemini-free-tier-e-lgpd.md) | Gemini free tier opt-in + mascaramento tipado | Apenas Ollama ou tier pago | Regex não é DLP completo; documentado como limitação. |
| [0007](docs/adr/0007-pgvector-no-postgres.md) | pgvector no próprio PostgreSQL | Banco vetorial dedicado (Qdrant) | Vetores competem com o OLTP em troca de consistência transacional e zero infraestrutura extra. |
| [0008](docs/adr/0008-busca-textual-pg-trgm.md) | Busca com `pg_trgm` (substring, sem acento) | Full-text search (`tsvector`) | Sem ranking por relevância em troca de achar códigos e trechos parciais. |
| [0009](docs/adr/0009-sql-explicito-no-dashboard.md) | SQL explícito no dashboard; EF Core nas escritas | Tudo em LINQ | Duas formas de acesso a dados em troca de SQL visível e idiomático. |
| [0010](docs/adr/0010-filas-derivadas-do-estado.md) | Filas derivadas do estado + reconciliador | Tabela genérica de jobs | Varredura periódica em troca de fonte única de verdade e autocorreção. |
| [0011](docs/adr/0011-estrategia-de-embeddings.md) | Tabela única de documentos RAG, 768 dimensões (validada na PoC; normalização obrigatória) | Coluna de embedding nas tabelas de negócio | Precisão máxima do modelo em troca de compatibilidade entre fake e real. |
| [0012](docs/adr/0012-copiloto-com-streaming-sse.md) | Copiloto com streaming SSE | Resposta JSON completa | Parser de SSE e erro no meio do stream em troca de UX conversacional e transparência das ferramentas. |
| [0013](docs/adr/0013-minimal-apis.md) | Minimal APIs com route groups | Controllers (MVC) | Organização por convenção própria em troca de endpoints finos por construção. |
| [0014](docs/adr/0014-fluxo-git-trunk-based.md) | Trunk-based com uma branch por sprint, PR e merge commit | GitFlow clássico | Sem área de integração separada em troca de uma `main` sempre entregável e sprints visíveis como PRs. |

### Decisões de plataforma (Sprint 0)

| # | Decisão | Alternativa rejeitada | Trade-off principal |
|---|---|---|---|
| [0015](docs/adr/0015-migrations-em-servico-one-shot.md) | Migrations e seed num serviço one-shot (`migrator`) | Migrate no startup da API | Um contêiner a mais em troca de um único ponto de DDL e de falhas isoladas e visíveis. |
| [0016](docs/adr/0016-logs-estruturados-nativos.md) | Logging nativo do .NET em JSON no stdout, com `CorrelationId` no scope | Serilog | Formato de campos fixo e um middleware próprio em troca de zero pacotes e de logs ligados aos traces do OpenTelemetry sem ponte extra. |
| [0017](docs/adr/0017-ui-kit-mantine.md) | Mantine como biblioteca de UI (AppShell, estados, notificações e gráficos) | Tailwind CSS + shadcn/ui | Visual padrão e bundle maior em troca de estados, responsividade e acessibilidade prontos no prazo. |
| [0022](docs/adr/0022-ci-com-smoke-do-compose.md) | CI no GitHub Actions: backend, frontend e smoke test do `docker compose up` sem `.env` | Só build e testes | CI alguns minutos mais lento em troca de verificar o item 1 da DoD (clone limpo) em todo push. |
| [0023](docs/adr/0023-segredos-em-env-local.md) | Segredos em variáveis de ambiente via `.env` local + push protection, sem segredos no CI | Secrets em arquivo (`/run/secrets` + user-secrets) | Chave em texto puro no disco local em troca de ativação simples (uma linha) e defesa focada no risco real: vazamento. |

### Decisões da Sprint 2 (triagem por IA)

| # | Decisão | Alternativa rejeitada | Trade-off principal |
|---|---|---|---|
| [0024](docs/adr/0024-retry-no-cliente-de-chat.md) | Novas tentativas ao provedor no cliente de chat (middleware do `IChatClient`), com o retry do SDK desligado; a fila só retoma Worker que caiu | Retry pela fila (`proxima_tentativa_em`) | Worker ocupado durante as esperas e 429 longo vira `Falhou`, em troca de um trace com todas as tentativas e uma regra só para triagem e copiloto. |

| Decisão de implementação | Motivo |
|---|---|
| A etapa "Recuperar" do pipeline já existe, devolvendo zero fontes até o RAG (Sprint 3) | O pipeline e os spans já têm as 5 etapas do ADR-0004; a Sprint 3 só troca a implementação |
| O prompt pede o **nome** da categoria, e o validador o converte em id | O modelo erra menos com nomes; categoria inexistente vira `Falhou` (RN-09) |
| Telemetria **por tentativa** em `uso_llm` (por dentro da resiliência) | O custo e as falhas de cada chamada ao provedor ficam visíveis, inclusive as que foram repetidas |
| Falha de transporte do SDK (`ClientResultException` sem resposta HTTP) é transitória | Queda de rede, DNS e TLS são exatamente o caso que a resiliência deve repetir |
| Triagem com `xmin` como token de concorrência; o índice único de pendente vira 409 | Decisões e "Refazer" simultâneos são resolvidos pelo banco, sem sobrescrever |
| Variável vazia no `.env` vale o padrão; valor inválido impede a subida | Uma linha `CHAVE=` não pode virar "desligado" sem querer; um erro de digitação aparece na hora, sem nunca ecoar a chave |
| Em rede com inspeção TLS, provedor real só com o Worker fora do contêiner (ou Ollama) | As imagens continuam sem CA corporativa (decisão da Sprint 0); a limitação é da rede, não do projeto |
| A imagem `aspire-dashboard:13.6.0` roda com o `entrypoint` sobrescrito no Compose | A própria imagem aponta para um `.dll` que não existe mais na 13.x |

### Decisões da Sprint 3 (RAG, dashboard e evals)

Nenhum ADR novo: as decisões abaixo aplicam os ADRs 0007, 0009, 0010, 0011 e 0018 e ficam registradas para quem lê o código.

| Decisão | Motivo |
|---|---|
| **A `triagem.v2` (com RAG) virou o prompt padrão** depois do eval no Gemini: categoria 86/90 → 90/90, prioridade 82/90 → 78/90, segurança 5/5 nas duas | A categoria é o que o atendente mais aceita, e as fontes no painel entregam o RF-16. A queda na prioridade (casos "com contorno") fica como alvo da `triagem.v3` ([docs/evals](docs/evals/LEIAME.md)) |
| Os trechos recuperados vão na **mensagem do usuário**, num bloco `<contexto>` separado do `<chamado>`, e não no prompt de sistema | Vêm de chamados escritos por outros usuários: são dados não confiáveis, como o próprio chamado, e as tags dos dois blocos são neutralizadas contra injeção |
| A versão do prompt é configurável (`TRIAGEM_PROMPT_VERSAO`) e só a que descreve o `<contexto>` aciona a recuperação | A v1 continua sendo a linha de base sem RAG (sem custo de embedding) e dá para voltar atrás sem rebuild; o Worker não sobe com um prompt inexistente |
| `documentos_rag.origem_atualizada_em` registra a versão da origem que o documento reflete | O reconciliador só remonta e recalcula o hash das origens alteradas, em vez de todos os resolvidos a cada passada (o "hash diferente → reindexar" do ADR-0010 com um filtro barato antes) |
| A sincronização de uma origem é serializada com `pg_advisory_xact_lock` | Duas instâncias gravando os mesmos chunks entravam em deadlock (o EF ordena os INSERTs por Guid v7, sem ordem garantida no mesmo milissegundo); a segunda agora espera e só confirma |
| Fonte exibida = uma por documento de origem, com a similaridade do melhor trecho; o prompt recebe os trechos | O atendente vê "o artigo X" uma vez; o modelo recebe o texto mais relevante |
| Sem o provedor de embeddings, a triagem segue **sem contexto** | Uma sugestão sem fontes é melhor que nenhuma (NFR-04); o span registra a falha |
| O dashboard executa as cinco consultas numa transação `REPEATABLE READ` somente leitura | Todas veem o mesmo instantâneo, então os totais batem entre si mesmo com escritas no meio |
| O harness de evals usa um registro de uso **em memória**, e não o `uso_llm` | Medir a IA não pode sujar o dashboard de produção; o custo do eval sai no relatório |
| Os tokens dos embeddings do Gemini não entram no custo | O endpoint OpenAI-compatível do Gemini não os devolve; o relatório registra a limitação |

### Decisões da Sprint 4 (copiloto conversacional)

Nenhum ADR novo: as decisões abaixo aplicam os ADRs 0004, 0012, 0020 e 0021 e ficam registradas para quem lê o código.

| Decisão | Motivo |
|---|---|
| SSE com `TypedResults.ServerSentEvents` (nativo do .NET 10), rate limiter nativo do ASP.NET Core e parser SSE escrito à mão no front | Nenhuma biblioteca nova para o streaming; o parser tem ~40 linhas e testes próprios (o ADR-0012 previa a opção) |
| O caso de uso tem dois passos: `PrepararAsync` (kill switch, validação, chamado, prompt) e `ResponderAsync` (eventos) | Depois do primeiro byte do stream o status HTTP não muda mais: 503, 422 e 404 precisam acontecer antes |
| A porta `ICopilotoLlm` recebe só `TextoMascarado` e devolve passos neutros (texto, ferramenta, fim); a falha do provedor vira `IaIndisponivelException` | O laço de ferramentas do `Microsoft.Extensions.AI` fica na Infrastructure, e a Application não conhece as exceções do SDK |
| Um parâmetro inválido de ferramenta volta ao modelo como `{"erro": "..."}`, e não como erro HTTP | O modelo corrige a chamada (por exemplo, usa uma categoria que existe); exceções inesperadas não mandam detalhes ao modelo (`IncludeDetailedErrors = false`) |
| Os parâmetros opcionais das ferramentas têm valor padrão no C# | Sem ele, o `AIFunctionFactory` os trata como obrigatórios, e a chamada do modelo falha antes de executar |
| No stream, a resiliência só repete **antes do primeiro pedaço**, e o prazo vira de inatividade depois dele | Repetir depois duplicaria texto na tela; um prazo total cortaria respostas longas e saudáveis |
| `uso_llm` ganha `erro_tipo = cancelado`, distinto de `timeout` (a resiliência passa o token do prazo nas opções clonadas) | Fechar o painel não é falha do provedor; sem a distinção, todo timeout apareceria como cancelamento |
| O caso de uso reafirma o `Activity.Current` a cada passo do iterador assíncrono | A cada `MoveNextAsync` o contexto volta ao de quem consome; sem isso, as ferramentas e as chamadas ao provedor ficariam fora do span `copiloto.responder` |
| O evento `fontes` leva os chamados **citados e devolvidos pelas ferramentas**, mais os artigos citados pelo título; o número do chamado em contexto pode ser citado sem aviso | Só aparece o que a resposta de fato usou e é verificável; o chamado aberto está no prompt e não é invenção |
| O buffer do guardrail corta preferindo um espaço, nunca dentro de um marcador | Um dado pessoal sem espaço (e-mail, CPF) fica inteiro no buffer até ser mascarado |
| A ferramenta de similares exclui o próprio chamado em contexto, e a de artigos só devolve artigos ativos | O chamado resolvido não é "parecido consigo mesmo"; um artigo desativado some antes de o reconciliador limpá-lo do índice |
| Rate limit por IP de origem, **limitação conhecida** atrás do Nginx (todos chegam com o IP do proxy) | Particionar pelo `X-Forwarded-For` exige confiar só no proxy; sem isso, qualquer um burlaria o limite chamando a API direto. Fica para a próxima versão |
| O prompt `copiloto.v1` não passou pelo harness de evals | O harness (ADR-0018) cobre só a triagem; evals do copiloto ficam para a próxima versão |

### Decisões da Sprint 5 (hardening)

| # | Decisão | Alternativa rejeitada | Trade-off principal |
|---|---|---|---|
| [0025](docs/adr/0025-icu-nas-imagens-dotnet.md) | ICU nas imagens do .NET (copiado da imagem do SDK), sem globalização invariante | Código independente do ICU (tabela própria de acentos) | +58 MB por imagem em troca de produção se comportar como os testes ao remover e comparar acentos (mascarador de nomes, validador da IA, copiloto). |

| Decisão | Motivo |
|---|---|
| O Nginx não comprime as respostas da API (`gzip off` em `/api/`) | Ao comprimir, ele trocava o ETag forte por um fraco (`W/"..."`), e o `If-Match` de toda escrita feita pelo navegador dava 412; os assets seguem comprimidos |

### Decisões da Sprint 6 (login e perfis, condicional)

| # | Decisão | Alternativa rejeitada | Trade-off principal |
|---|---|---|---|
| [0026](docs/adr/0026-autenticacao-jwt-com-usuarios-do-seed.md) | JWT próprio (tabela `usuarios`, PBKDF2 nativo, `JwtBearer`) em cookie `httpOnly` + `SameSite=Strict`; perfis atendente e solicitante; identidade vinda do token | ASP.NET Core Identity (`MapIdentityApi`) | Sem recuperação de senha, bloqueio nem refresh token, em troca de um pacote só, JWT de verdade e um token que o JavaScript nunca lê. |

### Decisões da Sprint 7 (design e experiência)

Sem ADR novo: nada mudou na arquitetura nem no contrato da API. O que guiou a sprint está na [análise de experiência](docs/06-analise-de-experiencia.md).

| Decisão | Motivo |
|---|---|
| A ordem do HTML do detalhe é a do celular (ações e triagem primeiro), e áreas de CSS grid reposicionam no desktop | Duplicar o bloco para cada largura repetiria IDs e formulários; a ordem do HTML também é a do teclado e do leitor de tela, e vira um teste simples |
| Componentes diferentes no celular (filtros recolhíveis, barras deitadas, consumo em cartões) só pelo hook `useCelular`, no mesmo breakpoint do menu | Posição o CSS resolve; trocar componente, não. Um ponto único evita breakpoints divergentes |
| O dashboard decide "sem permissão" pelo 403 da API, e não pelo perfil lido no front | A regra de quem vê o quê fica só no backend (ADR-0026), como a máquina de estados |
| Notificação de sucesso só onde o resultado não aparece onde o usuário está olhando (abrir chamado, mudar status, decidir a triagem) | Comentar e refazer a triagem já mostram o resultado no painel; avisar tudo vira ruído |
| Conteúdo alinhado à esquerda, junto ao menu, com uma largura única (1200 px) na lista, no detalhe e no dashboard | Padrão de aplicações de trabalho; antes, 960 e 1100 px faziam as bordas e o botão do topo mudarem de lugar |
| Ícones com `@tabler/icons-react`, sempre decorativos (`aria-hidden`) | É a biblioteca dos exemplos da Mantine, MIT, e só os ícones importados entram no pacote; o nome lido pelo leitor de tela continua sendo o texto |
| Contraste ajustado no tema, e não em cada componente: erro dos campos em `red.9`; variante "light" com fundo no tom 0 e hover no tom 1 | Corrige todos os usos de uma vez (inclusive os que a auditoria não visitou); um teste de unidade recalcula o contraste |
| O README descreve as mudanças da sprint em texto, sem capturas de tela | Decisão do desenvolvedor; o plano (05-sprints.md) previa as telas no README |

### Decisões de implementação (Sprint 1)

Decisões menores, que não contrariam nem acrescentam ADR, registradas para quem lê o código.

| Decisão | Motivo |
|---|---|
| Toda escrita concorrente sobre o chamado responde **412** `versao_desatualizada`: `If-Match` velho **ou** corrida detectada pelo `xmin` no `SaveChanges` | Um único código para o mesmo problema; o front trata um caso só (recarrega e avisa). O `03-modelo-de-dados.md` dizia 409 e foi alinhado ao contrato |
| Ordem das verificações nas escritas: 404 → 412 → 422 → 409 | A precondição HTTP é avaliada antes de qualquer outra coisa (RFC 9110) |
| Enums só como texto no JSON (`"Critica"`); número dá 400 | O contrato define strings; aceitar `2` em silêncio esconderia erro do cliente |
| Casos de uso registrados no host (`Api/ServicosAplicacao.cs`) | A Application segue sem nenhum pacote (ADR-0002); evita trazer `DependencyInjection.Abstractions` |
| A URL da lista usa os mesmos nomes e valores da query da API | Recarregar e compartilhar o link reproduzem a consulta; uma única conversão nos dois sentidos |
| Seed gerado pelo próprio domínio (`Abrir` → `MudarStatus` → `Comentar`) | Histórico, `resolvidoEm` e comentários coerentes por construção; se o seed violasse uma regra, o domínio recusaria |

### Adicionadas na revisão de arquitetura de 30/09

Uma revisão por pares confrontou o desenho com um catálogo de 25 padrões agênticos e um checklist de produção, antes de qualquer código de IA existir. Os detalhes estão em [`docs/revisoes/2026-09-30-padroes-agenticos.md`](docs/revisoes/2026-09-30-padroes-agenticos.md).

| # | Decisão | Alternativa rejeitada | Trade-off principal |
|---|---|---|---|
| [0018](docs/adr/0018-evals-offline-da-ia.md) | Evals offline: conjunto rotulado de ~30 casos + harness próprio, comparando sem RAG × com RAG | Só a métrica online (taxa de aceitação) | Exige chave e cota, então não roda no CI; em troca, a qualidade é medida antes de mudar. |
| [0019](docs/adr/0019-tracing-opentelemetry.md) | Tracing com OpenTelemetry, spans por etapa e Aspire Dashboard opcional | Só logs estruturados + `uso_llm` | 5 pacotes e um contêiner opcional em troca de ver onde cada segundo e cada token foram gastos. |
| [0020](docs/adr/0020-guardrail-de-saida-do-copiloto.md) | Guardrail de saída do copiloto: PII mascarada no stream e citações verificadas | Confiar só nos guardrails de entrada e de ação | ~64 caracteres de atraso no stream em troca de defesa em profundidade e de um *grounding check* determinístico. |
| [0021](docs/adr/0021-kill-switch-e-orcamentos-de-ia.md) | Kill switch por funcionalidade + limite de tokens por chamada | Usar `LLM_PROVIDER=fake` como desligamento | Mais configurações em troca de desligar com precisão, sem sugestões falsas, e de custo previsível. |

## Premissas assumidas

O enunciado permite registrar premissas aqui em vez de consultar o recrutador.

| # | Premissa |
|---|---|
| P-01 | Backend em C# / .NET 10 (LTS), a stack principal da vaga. |
| P-02 | `categoriaId` e `prioridade` são opcionais na criação (padrão: sem categoria e prioridade Média). A IA existe justamente para sugeri-las. |
| P-03 | ~~Sem autenticação na v1. O atendente é identificado por um campo livre (`alteradoPor`, `autor`).~~ **Substituída na Sprint 6** pelo [ADR-0026](docs/adr/0026-autenticacao-jwt-com-usuarios-do-seed.md): login com JWT e perfis; a identidade vem do token. Valeu até a `v1.0.0`. |
| P-04 | "Refazer triagem" cria um novo registro de `TriagemIA`. A vigente é a mais recente, e o histórico é preservado. |
| P-05 | O LLM responde em português do Brasil. |
| P-06 | É permitido comentar em chamados Resolvidos. Só Fechado e Cancelado bloqueiam comentários. |
| P-07 | A ordenação por prioridade segue a ordem de negócio (Crítica > Alta > Média > Baixa). |
| P-08 | O histórico do copiloto é efêmero (não é persistido na v1). |
| P-09 | A mudança de status aceita um comentário opcional na mesma transação (o comentário de resolução alimenta o RAG). |
| P-10 | A criação do chamado registra o histórico `null → Aberto`. |
| P-11 | Estados finais também bloqueiam refazer e aceitar triagem. |

Detalhes e justificativas: [`docs/01-requisitos.md`](docs/01-requisitos.md#8-premissas-a-registrar-no-decisoesmd).
