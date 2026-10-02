import { screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { renderizarApp } from '../testes/renderizar';

describe('LayoutBase', () => {
  it('abre na lista de chamados, com o título, o menu móvel e a navegação', async () => {
    const { roteador } = await renderizarApp('/');

    await waitFor(() => expect(roteador.state.location.pathname).toBe('/chamados'));
    expect(
      screen.getByRole('heading', { level: 1, name: 'HelpDesk Inteligente' }),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Abrir menu' })).toBeInTheDocument();
    expect(await screen.findByRole('heading', { level: 2, name: 'Chamados' })).toBeInTheDocument();
    const navegacao = screen.getByRole('navigation', { name: 'Navegação' });
    expect(navegacao).toContainElement(
      screen.getByRole('link', { name: 'Chamados', current: 'page' }),
    );
  });

  it('mostra a página de não encontrada para uma rota inexistente', async () => {
    await renderizarApp('/rota-que-nao-existe');

    expect(
      await screen.findByRole('heading', { name: 'Página não encontrada' }),
    ).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Voltar para os chamados' })).toHaveAttribute(
      'href',
      '/chamados',
    );
  });
});
