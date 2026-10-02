# Fluxo de ADR

Um ADR (*Architecture Decision Record*) registra **uma** decisão difícil de reverter: o contexto, as alternativas, a escolhida e o preço que se aceitou pagar. Ele existe para que daqui a seis meses ninguém precise adivinhar por que o sistema é como é, nem refazer uma discussão já feita.

## Quando escrever

Escreva um ADR quando a decisão:

- muda a estrutura (camadas, módulos, hosts, filas, banco);
- adiciona ou troca infraestrutura (um serviço no compose, uma imagem, um provedor);
- tem alternativas razoáveis e trade-offs reais;
- **contraria um ADR existente**: nesse caso, nunca em silêncio. Pare, escreva o novo ADR e marque o antigo como substituído.

Não escreva para escolhas locais e reversíveis (o nome de uma classe, um ajuste de layout). Essas vão, se valerem registro, na tabela de "decisões de implementação" da sprint no `DECISOES.md`.

## Como escrever

1. Copie [`docs/adr/0000-template.md`](../adr/0000-template.md) para `docs/adr/NNNN-titulo-curto.md`, com o próximo número livre.
2. Preencha:
   - **Contexto:** o problema e as restrições. Se a decisão nasceu de um bug ou de uma medição, conte como foi descoberto.
   - **Pelo menos duas alternativas**, cada uma com prós e contras honestos. Uma alternativa de fachada não ajuda ninguém.
   - **Decisão** e o porquê.
   - **Trade-offs aceitos:** o que se perde conscientemente (tamanho, latência, complexidade), com números quando houver.
   - **Consequências**, incluindo o **gatilho de reavaliação**: em que situação a decisão deve ser revista.
3. Status: `Proposta` enquanto discute; `Aceita` quando entra; `Substituída por ADR-XXXX` quando outro a troca; `Descartada` se não for adotada. Um ADR aceito não é editado no conteúdo: muda-se o status e cria-se outro.
4. Acrescente uma linha no [`DECISOES.md`](../../DECISOES.md) (decisão, alternativa rejeitada, trade-off principal), na seção da sprint.
5. Commite o ADR **junto com a mudança** que ele justifica, com o escopo `adr` ou o escopo da mudança. Exemplo: `fix(infra): instala o ICU nas imagens do .NET (ADR-0025)`.

## Revisões de arquitetura

Uma revisão que confronta o desenho com um catálogo ou checklist externo (como a [revisão de 30/09](../revisoes/2026-09-30-padroes-agenticos.md)) vai para `docs/revisoes/`, com o que motivou, o que foi adotado (cada adoção vira ADR) e o que foi rejeitado, com o motivo.

## Exemplo

O [ADR-0025](../adr/0025-icu-nas-imagens-dotnet.md) nasceu de um bug encontrado pelo E2E. O contexto conta como ele foi descoberto e por que os testes não o viam; as duas alternativas (instalar o ICU × código independente dele) têm custos medidos (+58 MB por imagem); e o gatilho de reavaliação diz quando voltar à alternativa rejeitada.
