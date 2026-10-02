import { screen, waitFor, within } from '@testing-library/react';
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

  it('no detalhe de um chamado, marca "Chamados" no menu', async () => {
    await renderizarApp('/chamados/0192f0c1-0000-7000-8000-000000000001');
    const navegacao = within(screen.getByRole('navigation', { name: 'Navegação' }));

    const chamados = navegacao.getByRole('link', { name: 'Chamados' });
    expect(chamados).toHaveAttribute('data-active', 'true');
    expect(chamados).not.toHaveAttribute('aria-current');
    expect(navegacao.getByRole('link', { name: 'Novo chamado' })).not.toHaveAttribute(
      'data-active',
    );
  });

  it('em "Novo chamado", "Chamados" não fica marcado', async () => {
    await renderizarApp('/chamados/novo');
    const navegacao = within(screen.getByRole('navigation', { name: 'Navegação' }));

    expect(navegacao.getByRole('link', { name: 'Novo chamado', current: 'page' })).toHaveAttribute(
      'data-active',
      'true',
    );
    expect(navegacao.getByRole('link', { name: 'Chamados' })).not.toHaveAttribute('data-active');
  });

  it('o menu (que no celular abre pelo hambúrguer) mostra o nome e o perfil do usuário', async () => {
    await renderizarApp('/chamados');
    const navegacao = within(screen.getByRole('navigation', { name: 'Navegação' }));

    const usuario = within(
      await navegacao.findByRole('group', { name: 'Usuário da sessão no menu' }),
    );
    expect(usuario.getByText('Ana (suporte)')).toBeInTheDocument();
    expect(usuario.getByText('Atendente')).toBeInTheDocument();
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
