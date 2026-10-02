import { waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { renderizarApp } from '../testes/renderizar';
import { servidor } from '../testes/servidor';

// O título do detalhe (com o número e o título do chamado) é conferido em DetalheChamado.test.tsx.
describe('Título da aba do navegador', () => {
  it.each([
    ['/chamados', 'Chamados — HelpDesk Inteligente'],
    ['/chamados/novo', 'Novo chamado — HelpDesk Inteligente'],
    ['/dashboard', 'Dashboard — HelpDesk Inteligente'],
    ['/rota-que-nao-existe', 'Página não encontrada — HelpDesk Inteligente'],
  ])('em %s é "%s"', async (rota, titulo) => {
    servidor.use(http.get('/api/dashboard/resumo', () => new HttpResponse(null, { status: 403 })));
    await renderizarApp(rota);

    await waitFor(() => expect(document.title).toBe(titulo));
  });

  it('para o solicitante, a lista é "Meus chamados"', async () => {
    servidor.use(
      http.get('/api/auth/eu', () =>
        HttpResponse.json({
          id: '0192f0c1-0000-7000-8000-0000000000bb',
          nome: 'Marina Costa',
          email: 'marina.costa@example.com',
          perfil: 'Solicitante',
        }),
      ),
    );
    await renderizarApp('/chamados');

    await waitFor(() => expect(document.title).toBe('Meus chamados — HelpDesk Inteligente'));
  });

  it('no login é "Entrar"', async () => {
    servidor.use(http.get('/api/auth/eu', () => new HttpResponse(null, { status: 401 })));
    await renderizarApp('/entrar');

    await waitFor(() => expect(document.title).toBe('Entrar — HelpDesk Inteligente'));
  });
});
