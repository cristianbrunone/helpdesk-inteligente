import { expect, test } from '@playwright/test';

/**
 * ADR-0026: fluxo de autenticação no navegador (página pública, redirecionamento guardando a rota, credenciais incorretas).
 */
test.describe('Login e sessão', () => {
  test('página protegida redireciona para login e volta ao destino após entrar', async ({
    browser,
  }) => {
    // Contexto limpo sem sessão salva
    const context = await browser.newContext({ storageState: undefined });
    const page = await context.newPage();

    await page.goto('/dashboard');
    await expect(page).toHaveURL(/\/entrar\?voltar=%2Fdashboard/);
    await expect(page.getByRole('heading', { level: 2, name: 'Entrar' })).toBeVisible();

    await page.getByLabel('E-mail').fill('ana.suporte@example.com');
    await page.getByLabel('Senha').fill('HelpDesk@2026');
    await page.getByRole('button', { name: 'Entrar' }).click();

    await expect(page).toHaveURL(/\/dashboard/);
    await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible();
    await expect(page.getByText('Ana (suporte)')).toBeVisible();

    await context.close();
  });

  test('credenciais inválidas exibem alerta de erro', async ({ browser }) => {
    const context = await browser.newContext({ storageState: undefined });
    const page = await context.newPage();

    await page.goto('/entrar');
    await page.getByLabel('E-mail').fill('ana.suporte@example.com');
    await page.getByLabel('Senha').fill('senha-incorreta');
    await page.getByRole('button', { name: 'Entrar' }).click();

    await expect(page.getByRole('alert')).toBeVisible();
    await expect(page).toHaveURL(/\/entrar/);

    await context.close();
  });
});
