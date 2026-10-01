import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { renderizarApp } from '../testes/renderizar';

describe('LayoutBase', () => {
  it('monta a casca da aplicação com o título, o menu móvel e as categorias da API', async () => {
    renderizarApp('/');

    expect(
      screen.getByRole('heading', { level: 1, name: 'HelpDesk Inteligente' }),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Abrir menu' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: 'Bem-vindo' })).toBeInTheDocument();
    expect(await screen.findByText('Acesso/Login')).toBeInTheDocument();
  });

  it('mostra a página de não encontrada para uma rota inexistente', async () => {
    renderizarApp('/rota-que-nao-existe');

    expect(
      await screen.findByRole('heading', { name: 'Página não encontrada' }),
    ).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Voltar ao início' })).toHaveAttribute('href', '/');
  });
});
