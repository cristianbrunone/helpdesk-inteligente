import { expect, test } from '@playwright/test';

/**
 * ADR-0026: fluxo completo do usuário com perfil Solicitante.
 * O solicitante tem visão restrita: não acessa o dashboard, não vê os campos de nome/e-mail
 * na abertura de chamado (a API vincula à sessão), não vê painéis de IA (triagem/copiloto)
 * nem botões de transição de status, mas pode acompanhar e comentar em seus chamados.
 */
test.describe('Fluxo do Solicitante', () => {
  test('login, restrições de navegação, abertura simplificada e comentários', async ({
    browser,
  }) => {
    // Contexto limpo para autenticar como solicitante (Marina Costa)
    const context = await browser.newContext({ storageState: undefined });
    const page = await context.newPage();

    // 1. Login com perfil Solicitante
    await page.goto('/entrar');
    await page.getByLabel('E-mail').fill('marina.costa@example.com');
    await page.getByLabel('Senha').fill('HelpDesk@2026');
    await page.getByRole('button', { name: 'Entrar' }).click();

    await expect(page).toHaveURL(/\/chamados/);
    const banner = page.getByRole('banner');
    await expect(banner.getByText('Marina Costa')).toBeVisible();
    await expect(banner.getByText('Solicitante')).toBeVisible();

    // 2. Não vê o link do Dashboard no menu de navegação
    const nav = page.getByRole('navigation', { name: 'Navegação' });
    await expect(nav.getByRole('link', { name: 'Meus chamados' })).toBeVisible();
    await expect(nav.getByRole('link', { name: 'Novo chamado' })).toBeVisible();
    await expect(nav.getByRole('link', { name: 'Dashboard' })).toHaveCount(0);

    // 3. Abertura simplificada: campos de nome e e-mail não aparecem
    await page.goto('/chamados/novo');
    await expect(page.getByLabel('Nome do solicitante')).toHaveCount(0);
    await expect(page.getByLabel('E-mail do solicitante')).toHaveCount(0);

    const titulo = `Dúvida sobre relatório financeiro (E2E ${Date.now()})`;
    await page.getByLabel('Título').fill(titulo);
    await page
      .getByLabel('Descrição')
      .fill('Solicito liberação de exportação para CSV no fechamento.');
    await page.getByRole('button', { name: 'Abrir chamado' }).click();

    // 4. Detalhe do chamado: sem triagem por IA, sem copiloto e sem botões de status
    await expect(page).toHaveURL(/\/chamados\/[0-9a-f-]{36}$/);
    const cabecalho = page.getByRole('heading', { level: 2 }).locator('xpath=..');
    await expect(cabecalho).toContainText(titulo);

    await expect(page.getByRole('region', { name: 'Triagem por IA' })).toHaveCount(0);
    await expect(page.getByPlaceholder('Faça uma pergunta ao copiloto...')).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Iniciar atendimento' })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Resolver' })).toHaveCount(0);

    // 5. Adicionar comentário no chamado como Solicitante
    const textoComentario = 'Preciso deste relatório até o final da tarde.';
    await page.getByLabel('Novo comentário').fill(textoComentario);
    await page.getByRole('button', { name: 'Comentar' }).click();

    const secaoComentarios = page.getByRole('region', { name: /Comentários/ });
    await expect(secaoComentarios.getByText(textoComentario)).toBeVisible();
    await expect(secaoComentarios.getByText(/Marina Costa/)).toBeVisible();

    await context.close();
  });
});
