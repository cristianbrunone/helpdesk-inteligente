import { expect, test } from '@playwright/test';

/**
 * Fluxo do enunciado (§7, E2E): criar um chamado → ver a triagem da IA → aceitar a sugestão. Com o provedor fake,
 * "boleto" vira Financeiro e "não consigo" vira prioridade Alta. Os dados são fictícios (§10: sem pessoas reais).
 */
test('criar chamado, ver a triagem da IA e aceitar a sugestão', async ({ page }) => {
  const titulo = `Não consigo emitir o boleto (E2E ${Date.now()})`;

  // 1. Criar o chamado, sem categoria nem prioridade: quem sugere é a IA.
  await page.goto('/chamados/novo');
  await page.getByLabel('Título').fill(titulo);
  await page.getByLabel('Descrição').fill('O boleto do mês não é gerado no portal desde ontem.');
  await page.getByLabel('Nome do solicitante').fill('Pessoa de Teste E2E');
  await page.getByLabel('E-mail do solicitante').fill('e2e@example.com');
  await page.getByRole('button', { name: 'Abrir chamado' }).click();

  // 2. O detalhe abre na hora: a criação não espera a IA (RF-02).
  await expect(page).toHaveURL(/\/chamados\/[0-9a-f-]{36}$/);
  const cabecalho = page.getByRole('heading', { level: 2 }).locator('xpath=..');
  await expect(cabecalho).toContainText(titulo);
  await expect(cabecalho).toContainText('Sem categoria');

  // 3. O Worker conclui a triagem e o painel mostra a sugestão (o painel consulta com polling).
  const painel = page.getByRole('region', { name: 'Triagem por IA' });
  await expect(painel.getByText('Gerado por IA')).toBeVisible({ timeout: 30_000 });
  await expect(painel).toContainText(/Financeiro|Bug no sistema/);

  // 4. Aceitar aplica categoria e prioridade ao chamado (identidade vem da sessão autenticada).
  await painel.getByRole('button', { name: 'Aceitar sugestão' }).click();

  await expect(painel.getByText(/Aceita por Ana/)).toBeVisible();
  await expect(cabecalho).not.toContainText('Sem categoria');
  await expect(cabecalho).toContainText('Alta');
});
