import { expect, test, type Page } from '@playwright/test';

/**
 * Definition of Done: as telas funcionam em 375 px. O sinal objetivo de layout quebrado nessa largura é a página
 * rolar na horizontal (algo mais largo que a tela). Cada tela é verificada depois de carregar os dados.
 */
test.use({ viewport: { width: 375, height: 812 } });

async function semRolagemHorizontal(page: Page) {
  const { larguraConteudo, larguraTela } = await page.evaluate(() => ({
    larguraConteudo: document.documentElement.scrollWidth,
    larguraTela: document.documentElement.clientWidth,
  }));
  expect(larguraConteudo, 'a página não pode rolar na horizontal').toBeLessThanOrEqual(larguraTela);
}

test('lista de chamados cabe em 375 px', async ({ page }) => {
  await page.goto('/chamados');
  await expect(page.getByRole('link', { name: /^#\d+/ }).first()).toBeVisible();
  await semRolagemHorizontal(page);
});

test('novo chamado cabe em 375 px', async ({ page }) => {
  await page.goto('/chamados/novo');
  await expect(page.getByRole('button', { name: 'Abrir chamado' })).toBeVisible();
  await semRolagemHorizontal(page);
});

test('detalhe do chamado cabe em 375 px', async ({ page, request }) => {
  const lista = await request.get('/api/chamados?q=403&tamanhoPagina=1');
  const id = ((await lista.json()) as { itens: { id: string }[] }).itens[0]?.id;
  await page.goto(`/chamados/${id}`);
  await expect(page.getByRole('region', { name: 'Triagem por IA' })).toBeVisible();
  await semRolagemHorizontal(page);
});

test('dashboard cabe em 375 px', async ({ page }) => {
  await page.goto('/dashboard');
  await expect(page.getByRole('region', { name: 'Total de chamados' })).toBeVisible();
  await semRolagemHorizontal(page);
});
