import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { renderizarApp } from '../testes/renderizar';
import { servidor } from '../testes/servidor';

const ID_CRIADO = '0192f0c1-0000-7000-8000-0000000000aa';

/** Responde o POST e guarda os corpos recebidos pela "API". */
function capturarCriacao(
  resposta = () => HttpResponse.json({ id: ID_CRIADO, numero: 1043 }, { status: 201 }),
) {
  const corpos: unknown[] = [];
  servidor.use(
    http.post('/api/chamados', async ({ request }) => {
      corpos.push(await request.json());
      return resposta();
    }),
  );
  return corpos;
}

async function preencherValido() {
  await userEvent.type(screen.getByLabelText(/^Título/), '  Erro ao emitir boleto  ');
  await userEvent.type(
    screen.getByLabelText(/^Descrição/),
    'Desde ontem aparece erro 403 no módulo de boletos.',
  );
  await userEvent.type(screen.getByLabelText(/^Nome do solicitante/), 'Maria Exemplo');
  await userEvent.type(screen.getByLabelText(/^E-mail do solicitante/), 'maria@example.com');
}

const abrir = () => userEvent.click(screen.getByRole('button', { name: 'Abrir chamado' }));

describe('NovoChamado', () => {
  it('valida no cliente e não envia nada enquanto houver erro', async () => {
    const corpos = capturarCriacao();
    await renderizarApp('/chamados/novo');

    await userEvent.type(screen.getByLabelText(/^Título/), 'abc');
    await userEvent.type(screen.getByLabelText(/^E-mail do solicitante/), 'maria@');
    await abrir();

    expect(
      await screen.findByText('O título deve ter entre 5 e 150 caracteres.'),
    ).toBeInTheDocument();
    expect(screen.getByText('Informe a descrição.')).toBeInTheDocument();
    expect(screen.getByText('Informe o nome do solicitante.')).toBeInTheDocument();
    expect(screen.getByText('Informe um e-mail válido.')).toBeInTheDocument();
    expect(screen.getByLabelText(/^Título/)).toHaveAttribute('aria-invalid', 'true');
    expect(corpos).toHaveLength(0);
  });

  it('envia os dados aparados, sem categoria nem prioridade, e abre o chamado criado', async () => {
    const corpos = capturarCriacao();
    const { roteador } = await renderizarApp('/chamados/novo');

    await preencherValido();
    await abrir();

    await waitFor(() => expect(roteador.state.location.pathname).toBe(`/chamados/${ID_CRIADO}`));
    expect(await screen.findByText('Chamado #1043 aberto.')).toBeInTheDocument();
    expect(corpos).toEqual([
      {
        titulo: 'Erro ao emitir boleto',
        descricao: 'Desde ontem aparece erro 403 no módulo de boletos.',
        solicitanteNome: 'Maria Exemplo',
        solicitanteEmail: 'maria@example.com',
        categoriaId: null,
        prioridade: null,
      },
    ]);
  });

  it('envia categoria e prioridade quando escolhidas', async () => {
    const corpos = capturarCriacao();
    await renderizarApp('/chamados/novo');
    await screen.findByRole('option', { name: 'Financeiro' });

    await preencherValido();
    await userEvent.selectOptions(screen.getByLabelText(/^Categoria/), 'Financeiro');
    await userEvent.selectOptions(screen.getByLabelText(/^Prioridade/), 'Crítica');
    await abrir();

    await waitFor(() => expect(corpos).toHaveLength(1));
    expect(corpos[0]).toMatchObject({ categoriaId: 2, prioridade: 'Critica' });
  });

  it('mostra nos campos os erros 422 devolvidos pela API', async () => {
    capturarCriacao(() =>
      HttpResponse.json(
        {
          title: 'Dados inválidos',
          codigo: 'validacao',
          errors: {
            categoriaId: ['A categoria informada não existe.'],
            solicitanteEmail: ['Informe um e-mail válido.'],
          },
        },
        { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
      ),
    );
    const { roteador } = await renderizarApp('/chamados/novo');

    await preencherValido();
    await abrir();

    expect(await screen.findByText('A categoria informada não existe.')).toBeInTheDocument();
    expect(screen.getByText('Informe um e-mail válido.')).toBeInTheDocument();
    expect(screen.getByLabelText(/^Categoria/)).toHaveAttribute('aria-invalid', 'true');
    expect(screen.queryByText('Não foi possível abrir o chamado')).not.toBeInTheDocument();
    expect(roteador.state.location.pathname).toBe('/chamados/novo');
  });

  it('mostra um alerta com o código de rastreio para erros que não são de validação', async () => {
    capturarCriacao(() =>
      HttpResponse.json(
        {
          detail: 'Ocorreu um erro inesperado. Informe o correlationId ao suporte.',
          codigo: 'erro_interno',
          correlationId: 'rastreio-789',
        },
        { status: 500, headers: { 'Content-Type': 'application/problem+json' } },
      ),
    );
    await renderizarApp('/chamados/novo');

    await preencherValido();
    await abrir();

    expect(await screen.findByText('Não foi possível abrir o chamado')).toBeInTheDocument();
    expect(screen.getByText('Código de rastreio: rastreio-789')).toBeInTheDocument();
  });

  it('para perfil solicitante, não exibe os campos de nome e e-mail do solicitante', async () => {
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
    const corpos = capturarCriacao();
    const { roteador } = await renderizarApp('/chamados/novo');

    expect(screen.queryByLabelText(/^Nome do solicitante/)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/^E-mail do solicitante/)).not.toBeInTheDocument();

    await userEvent.type(screen.getByLabelText(/^Título/), 'Meu problema de acesso');
    await userEvent.type(
      screen.getByLabelText(/^Descrição/),
      'Não consigo acessar nada desde hoje cedo.',
    );
    await abrir();

    await waitFor(() => expect(roteador.state.location.pathname).toBe(`/chamados/${ID_CRIADO}`));
    expect(corpos).toHaveLength(1);
    expect(corpos[0]).toMatchObject({
      titulo: 'Meu problema de acesso',
      descricao: 'Não consigo acessar nada desde hoje cedo.',
    });
  });
});
