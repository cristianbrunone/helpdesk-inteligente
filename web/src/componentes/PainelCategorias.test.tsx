import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { categoriasPadrao } from '../testes/handlers';
import { renderizar } from '../testes/renderizar';
import { servidor } from '../testes/servidor';
import { PainelCategorias } from './PainelCategorias';

describe('PainelCategorias', () => {
  it('mostra o esqueleto enquanto carrega e depois lista as categorias', async () => {
    renderizar(<PainelCategorias />);

    expect(screen.getByLabelText('Carregando categorias')).toBeInTheDocument();

    for (const categoria of categoriasPadrao) {
      expect(await screen.findByText(categoria.nome)).toBeInTheDocument();
    }
    expect(screen.queryByLabelText('Carregando categorias')).not.toBeInTheDocument();
  });

  it('mostra mensagem de lista vazia quando não há categorias', async () => {
    servidor.use(http.get('/api/categorias', () => HttpResponse.json([])));

    renderizar(<PainelCategorias />);

    expect(await screen.findByText('Nenhuma categoria cadastrada.')).toBeInTheDocument();
  });

  it('mostra o erro com o código de rastreio e recupera ao tentar novamente', async () => {
    servidor.use(
      http.get(
        '/api/categorias',
        () =>
          HttpResponse.json(
            {
              title: 'Erro interno',
              detail: 'Ocorreu um erro inesperado. Informe o correlationId ao suporte.',
              codigo: 'erro_interno',
              correlationId: 'rastreio-123',
            },
            { status: 500, headers: { 'Content-Type': 'application/problem+json' } },
          ),
        { once: true },
      ),
    );

    renderizar(<PainelCategorias />);

    expect(await screen.findByText('Não foi possível carregar as categorias')).toBeInTheDocument();
    expect(screen.getByText(/Informe o correlationId ao suporte/)).toBeInTheDocument();
    expect(screen.getByText('Código de rastreio: rastreio-123')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Tentar novamente' }));

    expect(await screen.findByText('Financeiro')).toBeInTheDocument();
    expect(screen.queryByText('Não foi possível carregar as categorias')).not.toBeInTheDocument();
  });
});
