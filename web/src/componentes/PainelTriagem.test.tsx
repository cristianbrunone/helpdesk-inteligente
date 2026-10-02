import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ChamadoDetalhe } from '../api/chamados';
import type { TriagemDetalhe } from '../api/triagem';
import { renderizarApp } from '../testes/renderizar';
import { servidor } from '../testes/servidor';

const ID = '0192f0c1-0000-7000-8000-0000000000bb';

function triagem(extra: Partial<TriagemDetalhe> = {}): TriagemDetalhe {
  return {
    id: 't1',
    status: 'Concluida',
    categoriaSugerida: { id: 2, nome: 'Financeiro' },
    prioridadeSugerida: 'Alta',
    resumo: 'Erro 403 ao abrir o módulo de boletos.',
    respostaSugerida: 'Olá! Vamos verificar seu acesso ao módulo de boletos.',
    confianca: 0.82,
    modelo: 'fake-triagem-v1',
    promptVersao: 'triagem.v1',
    fontes: [],
    erro: null,
    criadoEm: new Date().toISOString(),
    concluidaEm: new Date().toISOString(),
    decididaPor: null,
    decididaEm: null,
    totalTriagens: 1,
    ...extra,
  };
}

function chamado(extra: Partial<ChamadoDetalhe> = {}): ChamadoDetalhe {
  return {
    id: ID,
    numero: 2001,
    titulo: 'Não consigo emitir o boleto',
    descricao: 'Desde ontem aparece erro 403.',
    solicitanteNome: 'Maria Exemplo',
    solicitanteEmail: 'maria@example.com',
    categoria: null,
    prioridade: 'Media',
    status: 'Aberto',
    criadoEm: '2026-10-01T10:00:00Z',
    atualizadoEm: '2026-10-01T10:00:00Z',
    resolvidoEm: null,
    transicoesPermitidas: ['EmAndamento', 'Cancelado'],
    podeComentar: true,
    comentarios: [],
    historico: [
      {
        statusAnterior: null,
        statusNovo: 'Aberto',
        alteradoEm: '2026-10-01T10:00:00Z',
        alteradoPor: 'sistema',
      },
    ],
    triagem: triagem(),
    ...extra,
  };
}

/** GET do detalhe: cada leitura consome a próxima versão (a última se repete). */
function servirDetalhe(primeiro: ChamadoDetalhe, ...seguintes: ChamadoDetalhe[]) {
  const versoes = [primeiro, ...seguintes];
  let leituras = 0;
  servidor.use(
    http.get(`/api/chamados/${ID}`, () =>
      HttpResponse.json(versoes[Math.min(leituras++, versoes.length - 1)] ?? primeiro, {
        headers: { ETag: `"${leituras}"` },
      }),
    ),
  );
}

describe('PainelTriagem', () => {
  it('mostra a sugestão concluída identificada como gerada por IA', async () => {
    servirDetalhe(chamado());
    await renderizarApp(`/chamados/${ID}`);

    expect(await screen.findByText('Gerado por IA')).toBeInTheDocument();
    expect(screen.getByText('Financeiro')).toBeInTheDocument();
    expect(screen.getByText('Erro 403 ao abrir o módulo de boletos.')).toBeInTheDocument();
    expect(screen.getByText('Confiança da IA: 82%')).toBeInTheDocument();
    expect(
      screen.getByText('Olá! Vamos verificar seu acesso ao módulo de boletos.'),
    ).toBeInTheDocument();
    expect(screen.getByText('fake-triagem-v1 · prompt triagem.v1')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Aceitar sugestão' })).toBeEnabled();
  });

  it('aceita com If-Match e o nome do atendente, e mostra quem decidiu', async () => {
    servirDetalhe(chamado());
    let recebido: { ifMatch: string | null; corpo: unknown } | undefined;
    servidor.use(
      http.post(`/api/chamados/${ID}/triagem/aceitar`, async ({ request }) => {
        recebido = { ifMatch: request.headers.get('If-Match'), corpo: await request.json() };
        return HttpResponse.json(
          chamado({
            categoria: { id: 2, nome: 'Financeiro' },
            prioridade: 'Alta',
            triagem: triagem({
              status: 'Aceita',
              decididaPor: 'Ana (suporte)',
              decididaEm: '2026-10-01T10:05:00Z',
            }),
          }),
          { headers: { ETag: '"9"' } },
        );
      }),
    );
    await renderizarApp(`/chamados/${ID}`);

    await userEvent.click(await screen.findByRole('button', { name: 'Aceitar sugestão' }));

    expect(await screen.findByText(/Aceita por Ana \(suporte\) em/)).toBeInTheDocument();
    expect(
      await screen.findByText('Sugestão aceita: categoria e prioridade aplicadas ao chamado.'),
    ).toBeInTheDocument();
    expect(recebido).toEqual({ ifMatch: '"1"', corpo: {} });
    expect(screen.queryByRole('button', { name: 'Aceitar sugestão' })).not.toBeInTheDocument();
  });

  it('rejeita com motivo opcional pelo modal', async () => {
    servirDetalhe(chamado());
    let corpo: unknown;
    servidor.use(
      http.post(`/api/chamados/${ID}/triagem/rejeitar`, async ({ request }) => {
        corpo = await request.json();
        return HttpResponse.json(
          chamado({
            triagem: triagem({
              status: 'Rejeitada',
              decididaPor: 'Ana (suporte)',
              decididaEm: '2026-10-01T10:05:00Z',
            }),
          }),
          { headers: { ETag: '"9"' } },
        );
      }),
    );
    await renderizarApp(`/chamados/${ID}`);

    await userEvent.click(await screen.findByRole('button', { name: 'Rejeitar' }));
    await userEvent.type(screen.getByLabelText('Motivo (opcional)'), 'Categoria correta é Bug');
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar rejeição' }));

    expect(await screen.findByText(/Rejeitada por Ana \(suporte\) em/)).toBeInTheDocument();
    expect(await screen.findByText('Sugestão rejeitada.')).toBeInTheDocument();
    expect(corpo).toEqual({ motivo: 'Categoria correta é Bug' });
  });

  it('em falha mostra a mensagem amigável e refaz, passando a acompanhar a nova pendente', async () => {
    servirDetalhe(
      chamado({
        triagem: triagem({
          status: 'Falhou',
          erro: 'O provedor de IA não respondeu a tempo. Tente refazer a triagem.',
          categoriaSugerida: null,
          resumo: null,
        }),
      }),
      chamado({ triagem: triagem({ status: 'Pendente', totalTriagens: 2 }) }),
    );
    let refeita = false;
    servidor.use(
      http.post(`/api/chamados/${ID}/triagem`, () => {
        refeita = true;
        return HttpResponse.json(triagem({ status: 'Pendente' }), { status: 202 });
      }),
    );
    await renderizarApp(`/chamados/${ID}`);

    expect(
      await screen.findByText('O provedor de IA não respondeu a tempo. Tente refazer a triagem.'),
    ).toBeInTheDocument();
    expect(screen.queryByText('Gerado por IA')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Refazer triagem' }));

    expect(await screen.findByText('A IA está analisando o chamado…')).toBeInTheDocument();
    expect(refeita).toBe(true);
  });

  it('enquanto pendente, consulta de novo sozinho até a triagem concluir', async () => {
    servirDetalhe(chamado({ triagem: triagem({ status: 'Pendente' }) }), chamado());
    await renderizarApp(`/chamados/${ID}`);

    expect(await screen.findByText('A IA está analisando o chamado…')).toBeInTheDocument();
    expect(await screen.findByText('Gerado por IA', {}, { timeout: 5_000 })).toBeInTheDocument();
  });

  it('sem triagem e com a IA ativa, oferece solicitar a triagem', async () => {
    servirDetalhe(chamado({ triagem: null }));
    await renderizarApp(`/chamados/${ID}`);

    expect(await screen.findByText('Este chamado ainda não tem triagem.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Solicitar triagem' })).toBeInTheDocument();
  });

  it('com a triagem desativada, avisa e não oferece botões', async () => {
    servirDetalhe(chamado({ triagem: null }));
    servidor.use(
      http.get('/api/config/ia', () => HttpResponse.json({ triagem: false, copiloto: true })),
    );
    await renderizarApp(`/chamados/${ID}`);

    expect(await screen.findByText('Triagem por IA desativada no momento.')).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole('button', { name: 'Solicitar triagem' })).not.toBeInTheDocument(),
    );
  });

  it('mostra as fontes do RAG: chamado com link para o detalhe e artigo pelo título', async () => {
    servirDetalhe(
      chamado({
        triagem: triagem({
          promptVersao: 'triagem.v2',
          fontes: [
            {
              tipo: 'artigo',
              id: 'a1',
              numero: null,
              titulo: 'Erro 403 no módulo de boletos',
              similaridade: 0.692,
            },
            {
              tipo: 'chamado',
              id: '0192f0c1-0000-7000-8000-000000000877',
              numero: 877,
              titulo: 'Erro 403 ao abrir boletos',
              similaridade: 0.649,
            },
          ],
        }),
      }),
    );
    await renderizarApp(`/chamados/${ID}`);

    const lista = await screen.findByRole('list', { name: 'Baseado em' });
    expect(lista.querySelectorAll('li')).toHaveLength(2);
    expect(screen.getByText('Erro 403 no módulo de boletos')).toBeInTheDocument();
    expect(screen.getByText('69%')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '#877 Erro 403 ao abrir boletos' })).toHaveAttribute(
      'href',
      '/chamados/0192f0c1-0000-7000-8000-000000000877',
    );
    // O artigo não tem página: aparece sem link.
    expect(screen.queryByRole('link', { name: /módulo de boletos/ })).not.toBeInTheDocument();
  });

  it('sem fontes (sem RAG ou nada parecido), não mostra a seção', async () => {
    servirDetalhe(chamado());
    await renderizarApp(`/chamados/${ID}`);

    expect(await screen.findByText('Gerado por IA')).toBeInTheDocument();
    expect(screen.queryByText('Baseado em')).not.toBeInTheDocument();
  });

  it('chamado finalizado não oferece refazer', async () => {
    servirDetalhe(
      chamado({
        status: 'Cancelado',
        transicoesPermitidas: [],
        podeComentar: false,
        triagem: triagem({ status: 'Falhou', erro: 'Falhou.' }),
      }),
    );
    await renderizarApp(`/chamados/${ID}`);

    expect(await screen.findByText('Falhou.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Refazer triagem' })).not.toBeInTheDocument();
  });
});
