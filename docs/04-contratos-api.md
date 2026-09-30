# Contratos da API

> **Fase do checklist:** 3. Estratégia de comunicação (atualização do ADD)
> **Estilo:** REST + JSON; copiloto via Server-Sent Events ([ADR-0012](adr/0012-copiloto-com-streaming-sse.md))
> **Implementação:** Minimal APIs ([ADR-0013](adr/0013-minimal-apis.md))
> **Fonte da verdade a partir da Sprint 1:** o documento OpenAPI gerado pela API (`/openapi/v1.json`, com Swagger UI em `/swagger`). Este arquivo é o **desenho**. O teste de contrato garante que os dois não divergem.

## 1. Convenções

| Tema | Convenção |
|---|---|
| Base | `/api`. Sem versão na URL na v1; o versionamento, quando necessário, será por prefixo (`/api/v2`). |
| JSON | `camelCase`, datas em ISO 8601 UTC (`2026-09-30T14:03:00Z`), IDs em UUID. |
| Enums | Strings **ASCII** em PascalCase: `Aberto`, `EmAndamento`, `Resolvido`, `Fechado`, `Cancelado` / `Baixa`, `Media`, `Alta`, `Critica` / `Pendente`, `Concluida`, `Falhou`, `Aceita`, `Rejeitada`. Os rótulos com acento ("Média", "Crítica") ficam no frontend. Sem acento, os valores funcionam em query string sem encoding. |
| Identificação do atendente | Sem autenticação na v1 (P-03): os campos `alteradoPor`, `autor` e `decididaPor` vêm no corpo. |
| Correlação | O header `X-Correlation-Id` é aceito; se ausente, é gerado. Ele é devolvido na resposta e aparece nos logs e no ProblemDetails. |
| Concorrência | `GET /api/chamados/{id}` devolve um `ETag`. As operações de escrita sobre o chamado aceitam `If-Match` opcional; se o valor estiver desatualizado, a resposta é **412**. |
| Timeouts | A API nunca espera pelo LLM, exceto no copiloto (streaming com timeout próprio). |

## 2. Erros: ProblemDetails (RFC 9457)

Todas as respostas de erro usam `application/problem+json`:

```json
{
  "type": "https://helpdesk.local/problemas/transicao-invalida",
  "title": "Transição de status não permitida",
  "status": 409,
  "detail": "Não é possível ir de 'Aberto' para 'Resolvido'.",
  "instance": "/api/chamados/0192f0c1-.../status",
  "codigo": "transicao_invalida",
  "correlationId": "7c1e...",
  "transicoesPermitidas": ["EmAndamento", "Cancelado"]
}
```

Semântica dos códigos:

| HTTP | Quando | `codigo` |
|---|---|---|
| **400** | A requisição é malformada: JSON inválido, tipo errado, parâmetro de query inválido (por exemplo, `q` com menos de 3 caracteres ou `pagina=0`). | `requisicao_invalida` |
| **404** | O recurso não existe (chamado, triagem vigente ou categoria). | `nao_encontrado` |
| **409** | A requisição é bem formada, mas **conflita com o estado atual**. | ver catálogo abaixo |
| **412** | O `If-Match` não confere (outro atendente alterou o chamado). | `versao_desatualizada` |
| **422** | O corpo é bem formado, mas **viola regras de validação** (obrigatório, formato de e-mail, tamanho). Inclui `errors: { campo: [mensagens] }`. | `validacao` |
| **429** | O limite de requisições do copiloto foi excedido. | `limite_excedido` |
| **500** | Erro inesperado. Sem stack trace; com `correlationId` para rastrear nos logs. | `erro_interno` |
| **503** | O provedor de LLM está indisponível para o copiloto (a triagem nunca devolve 503: ela vira `Falhou`). | `ia_indisponivel` |

Catálogo de conflitos (**409**):

| `codigo` | Regra |
|---|---|
| `transicao_invalida` | RN-01 / RN-06. A resposta inclui `transicoesPermitidas`. |
| `chamado_finalizado` | RN-04: comentário, mudança de status ou triagem em chamado Fechado ou Cancelado. |
| `critico_nao_cancelavel` | RN-05. |
| `triagem_nao_concluida` | RN-07: aceitar ou rejeitar uma triagem que não está `Concluida`. |
| `triagem_em_andamento` | "Refazer" quando já existe uma triagem `Pendente` (índice único parcial). |

## 3. Endpoints

### `POST /api/chamados`: criar chamado

```json
// request
{
  "titulo": "Não consigo acessar o portal financeiro",
  "descricao": "Desde ontem aparece erro 403 ao abrir o módulo de boletos. Meu CPF é 123.456.789-00.",
  "solicitanteNome": "Maria Exemplo",
  "solicitanteEmail": "maria@example.com",
  "categoriaId": null,          // opcional (P-02)
  "prioridade": null            // opcional, padrão Media (P-02)
}
```

- **201 Created**, com `Location: /api/chamados/{id}` e o corpo = detalhe do chamado (seção abaixo), com `triagem.status = "Pendente"`.
- **422**: título com 5–150 caracteres, descrição com 10–5000, nome obrigatório (até 120), e-mail válido, categoria existente (quando informada).
- É **sempre** rápido: grava o chamado, o histórico (`null → Aberto`) e a triagem pendente numa transação (ADR-0003).

### `GET /api/chamados`: listar

| Parâmetro | Tipo | Exemplo | Regra |
|---|---|---|---|
| `status` | enum, repetível | `status=Aberto&status=EmAndamento` | Filtro OR entre os valores. |
| `prioridade` | enum, repetível | `prioridade=Critica` | Filtro OR entre os valores. |
| `categoriaId` | int, repetível | `categoriaId=2` | Use `semCategoria=true` para os não classificados. |
| `semCategoria` | bool | `true` | |
| `q` | string (3–100) | `q=boleto` | Substring em título ou descrição, insensível a acento (ADR-0008). |
| `criadoDe` / `criadoAte` | data ISO | `2026-09-01` | O intervalo é fechado-aberto `[de, ate + 1 dia)`. |
| `ordenarPor` | `criadoEm` \| `prioridade` | | Padrão: `criadoEm`. O desempate é `criadoEm desc, id desc`. |
| `direcao` | `asc` \| `desc` | | Padrão: `desc`. |
| `pagina` | int ≥ 1 | | Padrão: 1. |
| `tamanhoPagina` | int 1–100 | | Padrão: 20. |

```json
// 200 OK
{
  "itens": [
    {
      "id": "0192f0c1-...",
      "numero": 1042,
      "titulo": "Não consigo acessar o portal financeiro",
      "status": "Aberto",
      "prioridade": "Media",
      "categoria": null,
      "solicitanteNome": "Maria Exemplo",
      "criadoEm": "2026-09-30T14:03:00Z",
      "atualizadoEm": "2026-09-30T14:03:00Z",
      "triagemStatus": "Concluida"
    }
  ],
  "pagina": 1,
  "tamanhoPagina": 20,
  "totalItens": 187,
  "totalPaginas": 10
}
```

**Paginação por offset (e não keyset):** a tela precisa de "página X de Y" e de salto direto para uma página. No volume esperado (milhares), `OFFSET` com os índices 1 a 4 é barato. O keyset (cursor) fica registrado como evolução caso o volume passe de ~100 mil chamados. O e-mail do solicitante **não** aparece na listagem (minimização de dados).

### `GET /api/chamados/{id}`: detalhe

```json
{
  "id": "0192f0c1-...",
  "numero": 1042,
  "titulo": "...",
  "descricao": "...",
  "solicitanteNome": "Maria Exemplo",
  "solicitanteEmail": "maria@example.com",
  "categoria": { "id": 2, "nome": "Financeiro" },
  "prioridade": "Alta",
  "status": "EmAndamento",
  "criadoEm": "...", "atualizadoEm": "...", "resolvidoEm": null,
  "transicoesPermitidas": ["Resolvido"],
  "podeComentar": true,
  "comentarios": [
    { "id": "...", "autor": "Ana (suporte)", "texto": "...", "criadoEm": "..." }
  ],
  "historico": [
    { "statusAnterior": null, "statusNovo": "Aberto", "alteradoEm": "...", "alteradoPor": "sistema" },
    { "statusAnterior": "Aberto", "statusNovo": "EmAndamento", "alteradoEm": "...", "alteradoPor": "Ana (suporte)" }
  ],
  "triagem": {
    "id": "...",
    "status": "Concluida",
    "categoriaSugerida": { "id": 2, "nome": "Financeiro" },
    "prioridadeSugerida": "Alta",
    "resumo": "Usuário recebe erro 403 no módulo de boletos desde ontem.",
    "respostaSugerida": "Olá! Obrigado por nos avisar...",
    "confianca": 0.82,
    "modelo": "fake-triagem-v1",
    "promptVersao": "triagem.v1",
    "fontes": [
      { "tipo": "chamado", "id": "...", "numero": 877, "titulo": "Erro 403 em boletos", "similaridade": 0.91 },
      { "tipo": "artigo", "id": "...", "titulo": "Permissões do módulo financeiro", "similaridade": 0.84 }
    ],
    "erro": null,
    "criadoEm": "...", "concluidaEm": "...",
    "totalTriagens": 1
  }
}
```

- `transicoesPermitidas` e `podeComentar` são **calculados pelo domínio**. O frontend não replica a máquina de estados, só renderiza os botões (objetivo técnico 1 do ADD).
- O header `ETag` carrega a versão do chamado.
- `triagem` é `null` somente se nunca houve uma triagem. Se `status = "Falhou"`, `erro` traz uma mensagem amigável (por exemplo, "A IA retornou uma resposta fora do formato esperado"), nunca o detalhe técnico.

### `PATCH /api/chamados/{id}/status`: mudar status

```json
{ "status": "Resolvido", "alteradoPor": "Ana (suporte)", "comentario": "Permissão reaplicada no perfil." }
```

- O `comentario` é opcional. Quando presente, é gravado como comentário **na mesma transação**. Ao resolver, ele é o insumo principal do RAG (P-09).
- **200**: detalhe atualizado.
- **409**: `transicao_invalida` / `chamado_finalizado` / `critico_nao_cancelavel`. **412** se houver `If-Match` desatualizado. **422** se faltar `alteradoPor`.

### `POST /api/chamados/{id}/comentarios`: comentar

```json
{ "autor": "Ana (suporte)", "texto": "Pode me enviar um print do erro?" }
```

**201** com o comentário. **409** `chamado_finalizado`. **422** para validação (texto com 1–4000 caracteres).

### `POST /api/chamados/{id}/triagem`: refazer triagem

Sem corpo. **202 Accepted**, com a nova triagem (`Pendente`). **409** `triagem_em_andamento` ou `chamado_finalizado`.

### `POST /api/chamados/{id}/triagem/aceitar`

```json
{ "decididaPor": "Ana (suporte)" }
```

Aplica `categoriaSugerida` e `prioridadeSugerida` da **triagem vigente** ao chamado e marca a triagem como `Aceita`, na mesma transação. **200** com o detalhe. **404** se não houver triagem. **409** `triagem_nao_concluida` / `chamado_finalizado`.

> Aceitar uma prioridade `Critica` num chamado que depois seria cancelado segue a RN-05: o cancelamento passa a ser bloqueado.

### `POST /api/chamados/{id}/triagem/rejeitar`

```json
{ "decididaPor": "Ana (suporte)", "motivo": "Categoria correta é Bug no sistema" }
```

O `motivo` é opcional, mas é um insumo valioso para melhorar o prompt. **200**. Os códigos de erro são os mesmos do aceitar.

### `POST /api/chamados/{id}/copiloto`: conversar com o copiloto (SSE)

O histórico fica no cliente (P-08), e o servidor é **stateless**:

```json
{
  "mensagens": [
    { "papel": "usuario", "conteudo": "Já tivemos casos parecidos? Como resolvemos?" }
  ]
}
```

A resposta é `200`, com `Content-Type: text/event-stream`:

```
event: ferramenta
data: {"nome":"buscar_chamados_similares","fase":"iniciada","descricao":"Buscando chamados semelhantes resolvidos"}

event: ferramenta
data: {"nome":"buscar_chamados_similares","fase":"concluida","resultados":3}

event: delta
data: {"texto":"Sim, encontrei 3 casos parecidos. No chamado #877..."}

event: fontes
data: {"itens":[{"tipo":"chamado","id":"...","numero":877,"titulo":"Erro 403 em boletos"}]}

event: fim
data: {"tokensEntrada":1840,"tokensSaida":212}
```

Se ocorrer um erro **depois** que o stream já começou, o servidor envia `event: erro` com o payload de ProblemDetails e encerra. Se o erro ocorrer **antes**, a resposta é um HTTP normal (404/409/422/429/503).

Limites: até 20 mensagens por requisição, 2000 caracteres por mensagem, 3 rodadas de ferramentas por pergunta, e rate limit por IP (padrão: 10 requisições/min, protegendo a cota do free tier).

**Ferramentas expostas ao modelo** (todas somente leitura, com resultado mascarado e limitado):

| Ferramenta | Parâmetros | Retorna |
|---|---|---|
| `buscar_chamados_similares` | `consulta: string`, `categoria?: string`, `limite?: int (1–5)` | Chamados resolvidos semelhantes: número, título, resumo da resolução e similaridade. |
| `buscar_artigos` | `consulta: string`, `limite?: int (1–5)` | Trechos de artigos: título, trecho e similaridade. |
| `obter_historico_do_chamado` | *(nenhum; sempre o chamado atual)* | O histórico de status e os comentários do chamado em contexto. |
| `obter_metricas_da_categoria` | `categoria: string` | Volume, tempo médio de resolução e taxa de aceitação da IA na categoria. |

A ferramenta de histórico **não recebe ID** de propósito: o escopo é o chamado aberto na tela. Isso reduz a superfície de uma eventual prompt injection.

### `GET /api/categorias`

**200**: `[{ "id": 1, "nome": "Acesso/Login" }, ...]`. É usado nos filtros e no formulário.

### `GET /api/dashboard/resumo`

```json
{
  "totalChamados": 200,
  "porStatus":     [{ "status": "Aberto", "total": 41 }, ...],
  "porPrioridade": [{ "prioridade": "Critica", "total": 12 }, ...],
  "tempoMedioResolucaoPorCategoria": [
    { "categoriaId": 1, "categoria": "Acesso/Login", "resolvidos": 22, "tempoMedioHoras": 6.4 }
  ],
  "ia": {
    "taxaAceitacao": 0.71,
    "aceitas": 60, "rejeitadas": 24, "pendentes": 3, "falhas": 5,
    "porCategoria": [{ "categoria": "Financeiro", "aceitas": 14, "rejeitadas": 3, "taxaAceitacao": 0.824 }],
    "consumo30d": [{ "operacao": "triagem", "modelo": "...", "chamadas": 140, "tokensEntrada": 98000, "tokensSaida": 21000, "latenciaP95Ms": 3200 }]
  }
}
```

`tempoMedioHoras` é `null` para categorias sem chamados resolvidos. `taxaAceitacao` é `null` se ainda não houver decisões.

### `GET /health`

Usa os health checks do ASP.NET Core:

```json
{
  "status": "Healthy",
  "checks": {
    "banco":        { "status": "Healthy", "duracaoMs": 4 },
    "filaTriagem":  { "status": "Healthy", "pendentes": 0, "maisAntigaSegundos": null }
  }
}
```

- `banco` indisponível → **503** `Unhealthy`.
- A fila com uma triagem pendente há mais de 5 minutos → `Degraded` (**200**). Isso indica que o Worker está parado ou que o provedor está lento, sem derrubar a API.
- O provedor de LLM **não** entra no health check de propósito: a API funciona sem ele (D1).
