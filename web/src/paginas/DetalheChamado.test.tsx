import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ChamadoDetalhe } from '../api/chamados';
import { renderizarApp } from '../testes/renderizar';
import { servidor } from '../testes/servidor';

const ID = '0192f0c1-0000-7000-8000-000000000001';

function detalhe(extra: Partial<ChamadoDetalhe> = {}): ChamadoDetalhe {
  return {
    id: ID,
    numero: 1042,
    titulo: 'Não consigo acessar o portal financeiro',
    descricao: 'Desde ontem aparece erro 403 ao abrir o módulo de boletos.',
    solicitanteNome: 'Maria Exemplo',
    solicitanteEmail: 'maria@example.com',
    categoria: { id: 2, nome: 'Financeiro' },
    prioridade: 'Alta',
    status: 'Resolvido',
    criadoEm: '2026-09-30T14:03:00Z',
    atualizadoEm: '2026-09-30T18:00:00Z',
    resolvidoEm: '2026-09-30T18:00:00Z',
    transicoesPermitidas: ['Fechado', 'EmAndamento'],
    podeComentar: true,
    comentarios: [
      {
        id: 'c1',
        autor: 'Ana (suporte)',
        texto: 'Permissão reaplicada no perfil.',
        criadoEm: '2026-09-30T18:00:00Z',
      },
    ],
    historico: [
      {
        statusAnterior: null,
        statusNovo: 'Aberto',
        alteradoEm: '2026-09-30T14:03:00Z',
        alteradoPor: 'sistema',
      },
      {
        statusAnterior: 'Aberto',
        statusNovo: 'EmAndamento',
        alteradoEm: '2026-09-30T15:00:00Z',
        alteradoPor: 'Ana (suporte)',
      },
      {
        statusAnterior: 'EmAndamento',
        statusNovo: 'Resolvido',
        alteradoEm: '2026-09-30T18:00:00Z',
        alteradoPor: 'Ana (suporte)',
      },
    ],
    triagem: null,
    ...extra,
  };
}

/** GET do detalhe com ETag; cada chamada consome a próxima versão da lista (a última se repete). */
function servirDetalhe(
  primeira: [ChamadoDetalhe, string],
  ...seguintes: [ChamadoDetalhe, string][]
) {
  const versoes = [primeira, ...seguintes];
  let leituras = 0;
  servidor.use(
    http.get(`/api/chamados/${ID}`, () => {
      const [corpo, etag] = versoes[Math.min(leituras++, versoes.length - 1)] ?? primeira;
      return HttpResponse.json(corpo, { headers: { ETag: etag } });
    }),
  );
  return () => leituras;
}

async function informarAtendente() {
  await userEvent.type(await screen.findByLabelText('Seu nome (atendente)'), 'Bruno (suporte)');
}

describe('DetalheChamado', () => {
  it('mostra dados, comentários e histórico, e só os botões das transições permitidas', async () => {
    servirDetalhe([detalhe(), '"7"']);
    renderizarApp(`/chamados/${ID}`);

    expect(
      await screen.findByRole('heading', {
        name: '#1042 · Não consigo acessar o portal financeiro',
      }),
    ).toBeInTheDocument();
    expect(screen.getByText('Permissão reaplicada no perfil.')).toBeInTheDocument();
    expect(screen.getByText('maria@example.com')).toBeInTheDocument();
    expect(screen.getByText('Em andamento → Resolvido')).toBeInTheDocument();

    const acoes = within(screen.getByRole('group', { name: 'Mudar status' }));
    expect(acoes.getAllByRole('button').map((b) => b.textContent)).toEqual(['Fechar', 'Reabrir']);
    expect(screen.queryByRole('button', { name: 'Cancelar chamado' })).not.toBeInTheDocument();
  });

  it('chamado fechado não mostra botões de status nem formulário de comentário', async () => {
    servirDetalhe([
      detalhe({ status: 'Fechado', transicoesPermitidas: [], podeComentar: false }),
      '"9"',
    ]);
    renderizarApp(`/chamados/${ID}`);

    expect(
      await screen.findByText('Chamado finalizado: não aceita mais mudanças de status.'),
    ).toBeInTheDocument();
    expect(screen.queryByRole('group', { name: 'Mudar status' })).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Novo comentário')).not.toBeInTheDocument();
    expect(
      screen.getByText('Chamados fechados ou cancelados não aceitam comentários.'),
    ).toBeInTheDocument();
  });

  it('exige o nome do atendente antes de habilitar as ações', async () => {
    servirDetalhe([detalhe(), '"7"']);
    renderizarApp(`/chamados/${ID}`);

    expect(await screen.findByRole('button', { name: 'Fechar' })).toBeDisabled();
    await informarAtendente();
    expect(screen.getByRole('button', { name: 'Fechar' })).toBeEnabled();
  });

  it('muda o status com If-Match e comentário, e mostra o resultado', async () => {
    servirDetalhe([detalhe(), '"7"']);
    let recebido: { ifMatch: string | null; corpo: unknown } | undefined;
    servidor.use(
      http.patch(`/api/chamados/${ID}/status`, async ({ request }) => {
        recebido = { ifMatch: request.headers.get('If-Match'), corpo: await request.json() };
        return HttpResponse.json(
          detalhe({ status: 'Fechado', transicoesPermitidas: [], podeComentar: false }),
          { headers: { ETag: '"8"' } },
        );
      }),
    );
    renderizarApp(`/chamados/${ID}`);
    await informarAtendente();

    await userEvent.click(screen.getByRole('button', { name: 'Fechar' }));
    await userEvent.type(
      screen.getByLabelText('Comentário (opcional)'),
      'Confirmado pelo cliente.',
    );
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar' }));

    expect(
      await screen.findByText('Chamado finalizado: não aceita mais mudanças de status.'),
    ).toBeInTheDocument();
    expect(recebido).toEqual({
      ifMatch: '"7"',
      corpo: {
        status: 'Fechado',
        alteradoPor: 'Bruno (suporte)',
        comentario: 'Confirmado pelo cliente.',
      },
    });
  });

  it('em 412 avisa que outra pessoa alterou e recarrega a versão atual', async () => {
    const leituras = servirDetalhe(
      [detalhe(), '"7"'],
      [detalhe({ status: 'EmAndamento', transicoesPermitidas: ['Resolvido'] }), '"9"'],
    );
    servidor.use(
      http.patch(`/api/chamados/${ID}/status`, () =>
        HttpResponse.json(
          { codigo: 'versao_desatualizada', detail: 'O chamado foi alterado por outra pessoa.' },
          { status: 412, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    renderizarApp(`/chamados/${ID}`);
    await informarAtendente();

    await userEvent.click(screen.getByRole('button', { name: 'Fechar' }));
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar' }));

    expect(
      await screen.findByText('Este chamado foi alterado por outra pessoa'),
    ).toBeInTheDocument();
    expect(
      screen.getByText('Carregamos a versão mais recente. Confira as mudanças e tente de novo.'),
    ).toBeInTheDocument();
    expect(
      await screen.findByRole('button', { name: 'Marcar como resolvido' }),
    ).toBeInTheDocument();
    expect(leituras()).toBe(2);
  });

  it('comenta com If-Match e recarrega o detalhe com o novo comentário', async () => {
    servirDetalhe(
      [detalhe(), '"7"'],
      [
        detalhe({
          comentarios: [
            ...detalhe().comentarios,
            {
              id: 'c2',
              autor: 'Bruno (suporte)',
              texto: 'Tudo certo?',
              criadoEm: '2026-10-01T10:00:00Z',
            },
          ],
        }),
        '"8"',
      ],
    );
    let recebido: { ifMatch: string | null; corpo: unknown } | undefined;
    servidor.use(
      http.post(`/api/chamados/${ID}/comentarios`, async ({ request }) => {
        recebido = { ifMatch: request.headers.get('If-Match'), corpo: await request.json() };
        return HttpResponse.json({ id: 'c2' }, { status: 201, headers: { ETag: '"8"' } });
      }),
    );
    renderizarApp(`/chamados/${ID}`);
    await informarAtendente();

    await userEvent.type(screen.getByLabelText('Novo comentário'), '  Tudo certo?  ');
    await userEvent.click(screen.getByRole('button', { name: 'Comentar' }));

    expect(await screen.findByText('Tudo certo?')).toBeInTheDocument();
    expect(recebido).toEqual({
      ifMatch: '"7"',
      corpo: { autor: 'Bruno (suporte)', texto: 'Tudo certo?' },
    });
    expect(screen.getByLabelText('Novo comentário')).toHaveValue('');
  });

  it('mostra "Chamado não encontrado" para um id inexistente', async () => {
    servidor.use(
      http.get(`/api/chamados/${ID}`, () =>
        HttpResponse.json(
          { codigo: 'nao_encontrado' },
          { status: 404, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    renderizarApp(`/chamados/${ID}`);

    expect(
      await screen.findByRole('heading', { name: 'Chamado não encontrado' }),
    ).toBeInTheDocument();
  });

  it('mostra o erro com o código de rastreio quando a API falha', async () => {
    // O MSW consulta primeiro o handler registrado por último: o 500 (uma vez) vem antes do detalhe.
    servirDetalhe([detalhe(), '"7"']);
    servidor.use(
      http.get(
        `/api/chamados/${ID}`,
        () =>
          HttpResponse.json(
            { detail: 'Ocorreu um erro inesperado.', correlationId: 'rastreio-321' },
            { status: 500, headers: { 'Content-Type': 'application/problem+json' } },
          ),
        { once: true },
      ),
    );
    renderizarApp(`/chamados/${ID}`);

    expect(await screen.findByText('Não foi possível carregar o chamado')).toBeInTheDocument();
    expect(screen.getByText('Código de rastreio: rastreio-321')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Tentar novamente' }));
    expect(await screen.findByText('Permissão reaplicada no perfil.')).toBeInTheDocument();
  });
});
