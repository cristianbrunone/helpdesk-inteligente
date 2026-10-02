import { expect, test } from '@playwright/test';

/**
 * Critério da Sprint 4, no navegador: "Já tivemos casos parecidos?" dispara a busca de chamados semelhantes, a
 * resposta chega em stream citando os números, e as fontes verificadas abrem o chamado citado. Usa um chamado do
 * seed sobre "erro 403", que tem semelhantes resolvidos no índice do RAG.
 */
test('copiloto responde citando chamados parecidos, com fontes clicáveis', async ({
  page,
  request,
}) => {
  const lista = await request.get('/api/chamados?q=403&tamanhoPagina=1');
  expect(lista.ok()).toBeTruthy();
  const id = ((await lista.json()) as { itens: { id: string }[] }).itens[0]?.id;
  expect(id).toBeTruthy();

  await page.goto(`/chamados/${id}`);
  await page
    .getByPlaceholder('Faça uma pergunta ao copiloto...')
    .fill('Já tivemos casos parecidos?');
  await page.getByRole('button', { name: 'Enviar' }).click();

  // A resposta cita chamados (#numero) e as fontes verificadas viram links para eles.
  const fontes = page.getByTestId('fonte-chamado');
  await expect(fontes.first()).toBeVisible({ timeout: 30_000 });
  await expect(page.getByText(/#\d+/).first()).toBeVisible();
  await expect(page.getByText('Contém referências não verificadas')).toHaveCount(0);

  const citado = (await fontes.first().textContent()) ?? '';
  const numero = citado.match(/#(\d+)/)?.[1];
  expect(numero).toBeTruthy();
  await fontes.first().click();

  await expect(page).not.toHaveURL(new RegExp(`/chamados/${id}$`));
  await expect(page.getByRole('heading', { level: 2 })).toContainText(`#${numero} ·`);
});
