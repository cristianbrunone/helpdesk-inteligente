# Análise de experiência das telas (Sprint 7)

- **Data:** 2026-10-02
- **Versão analisada:** `v1.1.0` (login e perfis), no `docker compose` local com a IA fake
- **Como:** percorremos todas as telas com os dois perfis (atendente `ana.suporte@example.com` e solicitante `marina.costa@example.com`), em 1366 px e em 375 px, com o Playwright (capturas fora do repositório), e lemos os componentes de `web/src/`.
- **Critério:** cada problema descreve o que atrapalha o uso, e não uma preferência estética. A prioridade **alta** marca o que atrapalha o trabalho diário ou engana o usuário. Os itens de prioridade alta entram nesta sprint; os de média entram se sobrar tempo; os de baixa ficam registrados.

## Resumo

A base está correta: todas as telas tratam carregando, vazio e erro, funcionam em 375 px e passaram no axe-core na Sprint 5. Os problemas estão na **ordem do conteúdo no celular**, no **feedback das ações que deram certo** e na **experiência do solicitante**, que até a Sprint 6 não existia como perfil e herdou as telas do atendente.

## Problemas encontrados

| # | Prioridade | Tela · perfil · largura | Problema | Proposta |
|---|---|---|---|---|
| A1 | **Alta** | Detalhe · atendente · 375 px | As **Ações** (mudar status) e a **Triagem por IA** ficam depois da descrição, do copiloto e dos comentários: o atendente rola cerca de 900 px para chegar à ação mais usada. No desktop, ficam na coluna da direita, visíveis de imediato. | No celular, a coluna lateral vem antes, logo abaixo do cabeçalho do chamado (ordem do `Grid`); no desktop, nada muda. |
| A2 | **Alta** | Lista · os dois perfis · 375 px | Os filtros, sempre abertos (busca, 3 grupos de chips, 2 datas e a ordenação), ocupam a primeira tela inteira: o primeiro chamado só aparece depois de ~740 px. | A busca continua sempre visível; o restante fica num painel recolhível "Filtros (n)", fechado por padrão no celular e aberto quando há filtro ativo. No desktop, nada muda. |
| A3 | **Alta** | Detalhe e novo chamado · os dois perfis | As escritas que dão certo **não avisam nada**: o modal de mudança de status fecha em silêncio, e "Abrir chamado" só troca de página. Só os erros mostram notificação. | Uma notificação curta de sucesso ("Chamado #208 aberto", "Status alterado para Em andamento"), no mesmo padrão das notificações de erro. |
| A4 | **Alta** | Lista · solicitante | O solicitante sem chamados vê "Nenhum chamado encontrado **com esses filtros**." sem ter aplicado filtro nenhum, e sem caminho para abrir o primeiro chamado. O título "Chamados" também não diz que a lista mostra só os dele. | Sem filtros, um estado vazio próprio ("Você ainda não abriu chamados" + botão "Abrir chamado"); com filtros, a mensagem atual. Título "Meus chamados" para o solicitante. |
| M1 | Média | Menu lateral · os dois perfis | No detalhe (`/chamados/:id`), nenhum item do menu fica marcado: o usuário perde a referência de onde está. | O item "Chamados" fica ativo também nas sub-rotas (exceto "Novo chamado", que tem item próprio). |
| M2 | Média | Cabeçalho · os dois perfis · 375 px | No celular, o nome e o perfil do usuário ficam ocultos: só aparece "Sair". Quem testa os dois perfis não sabe em qual está. | Nome e perfil no topo do menu aberto pelo hambúrguer. |
| M3 | Média | Dashboard · solicitante | Pela URL, o solicitante vê um alerta vermelho "Não foi possível carregar o dashboard" com "Tentar novamente", que nunca vai funcionar (403). | Uma mensagem neutra de "sem permissão", com um link para "Meus chamados", sem o botão de nova tentativa. |
| M4 | Média | Dashboard · atendente · 375 px | Os gráficos de barras omitem rótulos alternados do eixo X ("Em andamento" e "Fechado" somem), e a tabela de consumo de IA precisa de rolagem horizontal. | Barras horizontais no celular (os rótulos cabem) e a tabela com colunas compactas. |
| M5 | Média | Todas | O título da aba do navegador é sempre "HelpDesk Inteligente": com vários chamados abertos em abas, não dá para distinguir. | Título por página ("#208 · Impressora… — HelpDesk"). |
| B1 | Baixa | Menu lateral | Só texto, sem ícones nem identidade visual; o botão "Novo chamado" aparece no menu e no cabeçalho da lista. | Ícones no menu (exige uma biblioteca de ícones, que precisa de justificativa). |
| B2 | Baixa | Lista · os dois perfis | Só o título do cartão é clicável; o resto do cartão parece clicável e não é. | Cartão inteiro clicável, mantendo um único link acessível. |
| B3 | Baixa | Detalhe · os dois perfis | Os botões variam de tamanho (`xs` nas ações e nos comentários, padrão nos formulários), e o modal usa "Voltar" onde o formulário usa "Cancelar". | Padronizar tamanhos e rótulos. |

## Fora do escopo desta sprint

- **O solicitante não vê o andamento da triagem nem pode cancelar o próprio chamado:** são regras de produto (ADR-0026) e mudariam o contrato da API.
- **Ambiente com volume antigo:** num banco criado antes da Sprint 6, a Marina e o Paulo ficam sem chamados, porque o seed não reatribui os chamados que já existiam. Num clone limpo eles têm 20 cada. É uma questão de ambiente, não de tela; o caminho é recriar o volume (`docker compose down -v`).

## Commits da sprint

| Item | Commit |
|---|---|
| A1 | `feat(web): ações e triagem antes do conteúdo no detalhe em telas pequenas` |
| A2 | `feat(web): filtros da lista recolhíveis no celular` |
| A3 | `feat(web): confirma com notificação as ações que deram certo` |
| A4 | `feat(web): lista do solicitante com título e estado vazio próprios` |
| M1–M5 | um commit por item, na ordem, se sobrar tempo antes de 06/10 |

Cada commit traz os testes de componente afetados. Ao final: o E2E, a auditoria do axe-core (zero violações) e as capturas no README.

## Resultado (fim da sprint)

| Item | Situação | Commit |
|---|---|---|
| A1 | ✅ Resolvido | `feat(web): ações e triagem antes do conteúdo no detalhe em telas pequenas` |
| A2 | ✅ Resolvido | `feat(web): filtros da lista recolhíveis no celular` |
| A3 | ✅ Resolvido | `feat(web): confirma com notificação as ações que deram certo` |
| A4 | ✅ Resolvido | `feat(web): lista do solicitante com título e estado vazio próprios` |
| M1 | ✅ Resolvido | `feat(web): menu marca "Chamados" também no detalhe do chamado` |
| M2 | ✅ Resolvido | `feat(web): nome e perfil do usuário no menu do celular` |
| M3 | ✅ Resolvido | `feat(web): dashboard explica a falta de permissão sem alerta de erro` |
| M4 | ✅ Resolvido | `feat(web): gráficos e consumo de IA legíveis no dashboard em 375 px` |
| M5 | ✅ Resolvido | `feat(web): título da aba do navegador por página` |
| B1 | ✅ Resolvido | `feat(web): ícones no menu lateral e no cabeçalho` (a biblioteca `@tabler/icons-react` foi aprovada) |
| B2 | Para a próxima versão | Cartão inteiro clicável |
| B3 | Para a próxima versão | Padronizar tamanhos e rótulos dos botões |

**Fora da análise, pedidos ao ver as telas:** a mesma largura de conteúdo na lista, no detalhe e no dashboard (antes 960, 1100 e 1100 px); cantos arredondados no item ativo do menu; favicon.

**Auditoria de acessibilidade (axe-core, WCAG 2.1 AA):** 38 telas e estados, nos dois perfis, em 1366 e 375 px, incluindo formulários com erro, filtros abertos, o menu do celular e botões em hover. Rodou contra um compose isolado com o banco limpo, com o axe-core instalado fora do repositório, como na Sprint 5. Achou dois problemas de contraste, ambos corrigidos no tema (`web/src/tema.ts`), e terminou com **zero violações**:

| Problema | Antes | Depois |
|---|---|---|
| Texto e mensagem dos campos com erro (`red.6` sobre branco) | 3,28:1 | 5,46:1 (`red.9`) |
| Hover da variante "light" dos botões (texto tom 9 sobre fundo tom 2) | indigo 4,13:1, red 3,76:1 | indigo 5,34:1, red 4,51:1 (fundo tom 1) |

**Testes:** o frontend passou de 70 para 92 testes, e o E2E de 8 para 12 cenários. O contraste das cores ajustadas é recalculado num teste de unidade (`web/src/tema.test.ts`).
