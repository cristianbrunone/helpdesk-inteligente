import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ResumoDashboard } from '../api/dashboard';
import { renderizarApp } from '../testes/renderizar';
import { servidor } from '../testes/servidor';

function resumo(extra: Partial<ResumoDashboard> = {}): ResumoDashboard {
  return {
    totalChamados: 200,
    porStatus: [
      { status: 'Aberto', total: 41 },
      { status: 'EmAndamento', total: 39 },
      { status: 'Resolvido', total: 50 },
      { status: 'Fechado', total: 40 },
      { status: 'Cancelado', total: 30 },
    ],
    porPrioridade: [
      { prioridade: 'Baixa', total: 50 },
      { prioridade: 'Media', total: 80 },
      { prioridade: 'Alta', total: 50 },
      { prioridade: 'Critica', total: 20 },
    ],
    tempoMedioResolucaoPorCategoria: [
      { categoriaId: 1, categoria: 'Acesso/Login', resolvidos: 22, tempoMedioHoras: 6.4 },
      { categoriaId: 4, categoria: 'Dúvida', resolvidos: 0, tempoMedioHoras: null },
    ],
    ia: {
      taxaAceitacao: 0.714,
      aceitas: 60,
      rejeitadas: 24,
      pendentes: 3,
      falhas: 5,
      porCategoria: [{ categoria: 'Financeiro', aceitas: 14, rejeitadas: 3, taxaAceitacao: 0.824 }],
      consumo30d: [
        {
          operacao: 'triagem',
          modelo: 'gemini-3.5-flash-lite',
          chamadas: 140,
          falhas: 2,
          tokensEntrada: 98000,
          tokensSaida: 21000,
          latenciaP95Ms: 3200.4,
        },
      ],
    },
    ...extra,
  };
}

function servir(dados: ResumoDashboard) {
  servidor.use(http.get('/api/dashboard/resumo', () => HttpResponse.json(dados)));
}

describe('Dashboard', () => {
  it('enquanto carrega, mostra o esqueleto', async () => {
    // A resposta fica segura só até o fim do teste. Uma resposta que nunca termina (delay('infinite')) deixa o
    // interceptador do MSW num estado em que requisições de testes seguintes escapam dele ("fetch failed").
    let liberar = () => {};
    const segura = new Promise<void>((resolver) => (liberar = resolver));
    servidor.use(
      http.get('/api/dashboard/resumo', async () => {
        await segura;
        return HttpResponse.json(resumo());
      }),
    );
    await renderizarApp('/dashboard');

    expect(await screen.findByLabelText('Carregando o dashboard')).toHaveAttribute(
      'aria-busy',
      'true',
    );
    liberar();
    expect(await screen.findByRole('region', { name: 'Total de chamados' })).toBeInTheDocument();
  });

  it('mostra os cartões e os números de cada gráfico (tabelas para leitor de tela)', async () => {
    servir(resumo());
    await renderizarApp('/dashboard');

    const total = await screen.findByRole('region', { name: 'Total de chamados' });
    expect(within(total).getByText('200')).toBeInTheDocument();
    // Em aberto = Aberto + Em andamento.
    expect(
      within(screen.getByRole('region', { name: 'Em aberto' })).getByText('80'),
    ).toBeInTheDocument();
    const aceitacao = screen.getByRole('region', { name: 'Aceitação da IA' });
    expect(within(aceitacao).getByText('71,4%')).toBeInTheDocument();
    expect(within(aceitacao).getByText('60 aceitas, 24 rejeitadas')).toBeInTheDocument();
    expect(screen.getByText('3 na fila')).toBeInTheDocument();
    expect(screen.getByText('5 com falha')).toBeInTheDocument();

    const status = screen.getByRole('table', { name: 'Chamados por status' });
    expect(within(status).getByRole('rowheader', { name: 'Em andamento' })).toBeInTheDocument();
    const prioridade = screen.getByRole('table', { name: 'Chamados por prioridade' });
    expect(within(prioridade).getByRole('rowheader', { name: 'Crítica' })).toBeInTheDocument();
    const tempos = screen.getByRole('table', { name: 'Tempo médio de resolução por categoria' });
    expect(within(tempos).getByText('6,4')).toBeInTheDocument();
    expect(within(tempos).getByText('Sem resolvidos')).toBeInTheDocument();
    const decisoes = screen.getByRole('table', { name: 'Sugestões da IA por categoria' });
    expect(within(decisoes).getByText('82,4%')).toBeInTheDocument();
  });

  it('mostra o consumo de IA dos últimos 30 dias', async () => {
    servir(resumo());
    await renderizarApp('/dashboard');

    const consumo = await screen.findByRole('region', {
      name: 'Consumo de IA nos últimos 30 dias',
    });
    expect(within(consumo).getByText('gemini-3.5-flash-lite')).toBeInTheDocument();
    expect(within(consumo).getByText('98.000 / 21.000')).toBeInTheDocument();
    expect(within(consumo).getByText('3.200 ms')).toBeInTheDocument();
  });

  it('sem decisões da IA, não inventa taxa nem gráfico', async () => {
    servir(
      resumo({
        ia: {
          ...resumo().ia,
          taxaAceitacao: null,
          aceitas: 0,
          rejeitadas: 0,
          porCategoria: [],
          consumo30d: [],
        },
      }),
    );
    await renderizarApp('/dashboard');

    const aceitacao = await screen.findByRole('region', { name: 'Aceitação da IA' });
    expect(within(aceitacao).getByText('—')).toBeInTheDocument();
    expect(within(aceitacao).getByText('Nenhuma sugestão decidida ainda')).toBeInTheDocument();
    expect(screen.getByText('Nenhuma sugestão decidida ainda.')).toBeInTheDocument();
    expect(
      screen.getByText('Nenhuma chamada ao provedor de IA nos últimos 30 dias.'),
    ).toBeInTheDocument();
  });

  it('sem chamados, mostra o estado vazio', async () => {
    servir(resumo({ totalChamados: 0 }));
    await renderizarApp('/dashboard');

    expect(
      await screen.findByText(
        'Ainda não há chamados. Os números aparecem aqui assim que o primeiro for aberto.',
      ),
    ).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Total de chamados' })).not.toBeInTheDocument();
  });

  it('em erro, mostra o código de rastreio e permite tentar novamente', async () => {
    // O último handler registrado vence: o sucesso fica por baixo do erro de uma vez só.
    servir(resumo());
    servidor.use(
      http.get(
        '/api/dashboard/resumo',
        () =>
          HttpResponse.json(
            { detail: 'Erro inesperado.', codigo: 'erro_interno', correlationId: 'rastreio-789' },
            { status: 500, headers: { 'Content-Type': 'application/problem+json' } },
          ),
        { once: true },
      ),
    );
    await renderizarApp('/dashboard');

    expect(await screen.findByText('Não foi possível carregar o dashboard')).toBeInTheDocument();
    expect(screen.getByText('Código de rastreio: rastreio-789')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Tentar novamente' }));

    expect(await screen.findByRole('region', { name: 'Total de chamados' })).toBeInTheDocument();
  });

  it('aparece no menu de navegação', async () => {
    servir(resumo());
    await renderizarApp('/chamados');

    expect(await screen.findByRole('link', { name: 'Dashboard' })).toHaveAttribute(
      'href',
      '/dashboard',
    );
  });
});
