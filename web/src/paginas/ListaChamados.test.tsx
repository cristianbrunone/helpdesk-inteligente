import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ChamadoResumo, ResultadoPaginado } from '../api/chamados';
import { chamadosPadrao, paginaDe } from '../testes/handlers';
import { simularCelular } from '../testes/celular';
import { renderizarApp } from '../testes/renderizar';
import { servidor } from '../testes/servidor';

/** Responde a listagem e guarda a query string de cada requisição recebida pela "API". */
function capturarListagem(resposta: ResultadoPaginado<ChamadoResumo> = paginaDe(chamadosPadrao)) {
  const consultas: URLSearchParams[] = [];
  servidor.use(
    http.get('/api/chamados', ({ request }) => {
      consultas.push(new URL(request.url).searchParams);
      return HttpResponse.json(resposta);
    }),
  );
  return consultas;
}

const urlAtual = (roteador: Awaited<ReturnType<typeof renderizarApp>>['roteador']) =>
  new URLSearchParams(roteador.state.location.search);

describe('ListaChamados', () => {
  it('lista os chamados com badges em pt-BR, categoria e link para o detalhe', async () => {
    await renderizarApp('/chamados');

    const link = await screen.findByRole('link', {
      name: '#1042 · Não consigo acessar o portal financeiro',
    });
    expect(link).toHaveAttribute('href', '/chamados/0192f0c1-0000-7000-8000-000000000001');
    const item = link.closest('li');
    if (!item) throw new Error('O link do chamado deveria estar dentro de um item da lista.');
    expect(within(item).getByText('Em andamento')).toBeInTheDocument();
    expect(within(item).getByText('Crítica')).toBeInTheDocument();
    expect(within(item).getByText('Financeiro')).toBeInTheDocument();
    expect(screen.getByText('Sem categoria', { selector: 'p' })).toBeInTheDocument();
    expect(screen.getByText('2 chamados')).toBeInTheDocument();
  });

  it('abre com os filtros da URL aplicados na tela e na consulta à API (recarregar mantém)', async () => {
    const consultas = capturarListagem();

    await renderizarApp('/chamados?status=Resolvido&q=boleto&ordenarPor=prioridade&pagina=2');

    await waitFor(() => expect(consultas).toHaveLength(1));
    expect(consultas[0]?.toString()).toBe(
      'status=Resolvido&q=boleto&ordenarPor=prioridade&pagina=2',
    );
    expect(screen.getByRole('checkbox', { name: 'Resolvido' })).toBeChecked();
    expect(screen.getByRole('searchbox', { name: 'Buscar no título e na descrição' })).toHaveValue(
      'boleto',
    );
    expect(screen.getByRole('combobox', { name: 'Ordenar por' })).toHaveValue('prioridade-desc');
  });

  it('reflete na URL o filtro escolhido e volta para a página 1', async () => {
    const consultas = capturarListagem();
    const { roteador } = await renderizarApp('/chamados?pagina=2');
    await screen.findByText('2 chamados');

    await userEvent.click(screen.getByRole('checkbox', { name: 'Aberto' }));
    await userEvent.click(screen.getByRole('checkbox', { name: 'Crítica' }));
    await userEvent.click(screen.getByRole('checkbox', { name: 'Sem categoria' }));

    await waitFor(() =>
      expect(urlAtual(roteador).toString()).toBe(
        'status=Aberto&prioridade=Critica&semCategoria=true',
      ),
    );
    await waitFor(() =>
      expect(consultas.at(-1)?.toString()).toBe(
        'status=Aberto&prioridade=Critica&semCategoria=true',
      ),
    );
  });

  it('busca com debounce, só a partir de 3 caracteres', async () => {
    const { roteador } = await renderizarApp('/chamados');
    const busca = await screen.findByRole('searchbox', { name: 'Buscar no título e na descrição' });

    await userEvent.type(busca, 'co');
    expect(screen.getByText('Digite ao menos 3 caracteres.')).toBeInTheDocument();
    await new Promise((resolver) => setTimeout(resolver, 500));
    expect(urlAtual(roteador).has('q')).toBe(false);

    await userEvent.type(busca, 'nfiguração');
    await waitFor(() => expect(urlAtual(roteador).get('q')).toBe('configuração'));
  });

  it('troca de página pela paginação e grava a página na URL', async () => {
    capturarListagem(paginaDe(chamadosPadrao, { totalItens: 45, totalPaginas: 3 }));
    const { roteador } = await renderizarApp('/chamados?status=Aberto');

    await userEvent.click(await screen.findByRole('button', { name: 'Página 2' }));

    await waitFor(() => expect(urlAtual(roteador).toString()).toBe('status=Aberto&pagina=2'));
  });

  it('mostra o estado vazio e limpa os filtros', async () => {
    capturarListagem(paginaDe([]));
    const { roteador } = await renderizarApp('/chamados?status=Fechado&q=inexistente');

    expect(
      await screen.findByText('Nenhum chamado encontrado com esses filtros.'),
    ).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Limpar filtros' }));

    await waitFor(() => expect(urlAtual(roteador).toString()).toBe(''));
  });

  it('sem chamados e sem filtros, convida o atendente a abrir o primeiro', async () => {
    capturarListagem(paginaDe([]));
    await renderizarApp('/chamados');

    expect(await screen.findByText('Ainda não há chamados.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Abrir chamado' })).toHaveAttribute(
      'href',
      '/chamados/novo',
    );
    expect(
      screen.queryByText('Nenhum chamado encontrado com esses filtros.'),
    ).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Limpar filtros' })).not.toBeInTheDocument();
  });

  it('para o solicitante, a lista se chama "Meus chamados" e o vazio fala com ele', async () => {
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
    capturarListagem(paginaDe([]));
    await renderizarApp('/chamados');

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Meus chamados' }),
    ).toBeInTheDocument();
    expect(await screen.findByText('Você ainda não abriu nenhum chamado.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Abrir chamado' })).toHaveAttribute(
      'href',
      '/chamados/novo',
    );
    const navegacao = within(screen.getByRole('navigation', { name: 'Navegação' }));
    expect(
      navegacao.getByRole('link', { name: 'Meus chamados', current: 'page' }),
    ).toBeInTheDocument();
  });

  it('mostra o erro com o código de rastreio e permite tentar novamente', async () => {
    servidor.use(
      http.get(
        '/api/chamados',
        () =>
          HttpResponse.json(
            {
              detail: 'Ocorreu um erro inesperado. Informe o correlationId ao suporte.',
              codigo: 'erro_interno',
              correlationId: 'rastreio-456',
            },
            { status: 500, headers: { 'Content-Type': 'application/problem+json' } },
          ),
        { once: true },
      ),
    );
    await renderizarApp('/chamados');

    expect(await screen.findByText('Não foi possível carregar os chamados')).toBeInTheDocument();
    expect(screen.getByText('Código de rastreio: rastreio-456')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Tentar novamente' }));

    expect(await screen.findByText('2 chamados')).toBeInTheDocument();
  });
});

describe('ListaChamados no celular', () => {
  it('mostra só a busca e recolhe os demais filtros, que abrem pelo botão', async () => {
    simularCelular();
    await renderizarApp('/chamados');
    await screen.findByText('2 chamados');

    expect(
      screen.getByRole('searchbox', { name: 'Buscar no título e na descrição' }),
    ).toBeVisible();
    expect(screen.queryByRole('checkbox', { name: 'Aberto' })).not.toBeInTheDocument();
    const botao = screen.getByRole('button', { name: 'Filtros' });
    expect(botao).toHaveAttribute('aria-expanded', 'false');

    await userEvent.click(botao);

    expect(screen.getByRole('checkbox', { name: 'Aberto' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Ocultar filtros' })).toHaveAttribute(
      'aria-expanded',
      'true',
    );
  });

  it('com filtros na URL, abre o painel e mostra quantos estão ativos (a busca não conta)', async () => {
    simularCelular();
    await renderizarApp('/chamados?status=Aberto&prioridade=Alta&q=boleto');
    await screen.findByText('2 chamados');

    expect(screen.getByRole('checkbox', { name: 'Aberto' })).toBeChecked();
    expect(screen.getByRole('button', { name: 'Ocultar filtros (2)' })).toBeInTheDocument();
  });
});
