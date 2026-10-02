import { expect, test as setup } from '@playwright/test';

const arquivoAuth = 'playwright/.auth/usuario.json';

/**
 * ADR-0026: autentica uma vez pela tela de login e salva a sessão no cookie httpOnly.
 * Os testes E2E seguintes reutilizam esta sessão para não repetir o login em cada spec.
 */
setup('autenticar como atendente (Ana)', async ({ page }) => {
  await page.goto('/entrar');
  await page.getByLabel('E-mail').fill('ana.suporte@example.com');
  await page.getByLabel('Senha').fill('HelpDesk@2026');
  await page.getByRole('button', { name: 'Entrar' }).click();

  await expect(page).toHaveURL(/\/chamados/);
  await expect(page.getByText('Ana (suporte)')).toBeVisible();

  await page.context().storageState({ path: arquivoAuth });
});
