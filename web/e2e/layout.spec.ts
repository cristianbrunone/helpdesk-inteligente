import { expect, test, type Page } from '@playwright/test';

/**
 * Consistência visual (Sprint 7): num monitor largo, a lista, o detalhe e o dashboard têm o conteúdo com a mesma
 * largura, alinhado à esquerda junto ao menu. Antes eram 960, 1100 e 1100 px, e as bordas e o botão do topo mudavam
 * de lugar ao navegar.
 */
test.use({ viewport: { width: 1920, height: 1000 } });

async function larguraDoConteudo(page: Page) {
  const caixa = await page.locator('main > *').first().boundingBox();
  expect(caixa, 'o conteúdo da página precisa estar visível').not.toBeNull();
  return { esquerda: Math.round(caixa?.x ?? 0), largura: Math.round(caixa?.width ?? 0) };
}

test('lista, detalhe e dashboard ocupam a mesma largura', async ({ page, request }) => {
  await page.goto('/chamados');
  await expect(page.getByRole('link', { name: /^#\d+/ }).first()).toBeVisible();
  const lista = await larguraDoConteudo(page);

  const resposta = await request.get('/api/chamados?tamanhoPagina=1');
  const id = ((await resposta.json()) as { itens: { id: string }[] }).itens[0]?.id;
  await page.goto(`/chamados/${id}`);
  await expect(page.getByRole('heading', { name: 'Descrição' })).toBeVisible();
  const detalhe = await larguraDoConteudo(page);

  await page.goto('/dashboard');
  await expect(page.getByRole('region', { name: 'Total de chamados' })).toBeVisible();
  const dashboard = await larguraDoConteudo(page);

  expect(detalhe).toEqual(lista);
  expect(dashboard).toEqual(lista);
});
