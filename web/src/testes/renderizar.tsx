import { MantineProvider } from '@mantine/core';
import { Notifications } from '@mantine/notifications';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { criarQueryClient } from '../api/queryClient';
import { render, waitFor } from '@testing-library/react';
import type { ReactElement } from 'react';
import { createMemoryRouter, MemoryRouter } from 'react-router';
import { RouterProvider } from 'react-router/dom';
import { rotas } from '../rotas';
import { tema, variaveisCss } from '../tema';

function criarQueryClientDeTeste(): QueryClient {
  // O mesmo da aplicação (com o 401 que encerra a sessão), mas sem novas tentativas: o teste de erro vê o erro na
  // primeira resposta.
  const cliente = criarQueryClient();
  cliente.setDefaultOptions({ queries: { ...cliente.getDefaultOptions().queries, retry: false } });
  return cliente;
}

/** Renderiza um componente com os mesmos providers da aplicação. */
export function renderizar(ui: ReactElement, { rota = '/' }: { rota?: string } = {}) {
  return render(
    <MantineProvider theme={tema} cssVariablesResolver={variaveisCss} env="test">
      <Notifications />
      <QueryClientProvider client={criarQueryClientDeTeste()}>
        <MemoryRouter initialEntries={[rota]}>{ui}</MemoryRouter>
      </QueryClientProvider>
    </MantineProvider>,
  );
}

/**
 * Renderiza a aplicação inteira (rotas reais) a partir de uma URL; o roteador expõe a URL atual. As páginas são
 * carregadas sob demanda (`lazy` nas rotas): só devolve depois que a página da URL carregou, como no navegador.
 */
export async function renderizarApp(rota = '/') {
  const roteador = createMemoryRouter(rotas, { initialEntries: [rota] });
  const resultado = render(
    <MantineProvider theme={tema} cssVariablesResolver={variaveisCss} env="test">
      <Notifications />
      <QueryClientProvider client={criarQueryClientDeTeste()}>
        <RouterProvider router={roteador} />
      </QueryClientProvider>
    </MantineProvider>,
  );
  await waitFor(
    () => {
      if (
        !roteador.state.initialized ||
        roteador.state.navigation.state !== 'idle' ||
        resultado.queryByLabelText('Carregando a página')
      ) {
        throw new Error('A página ainda está carregando.');
      }
    },
    // Abaixo do testTimeout (vite.config.ts), para a falha dizer que a página não carregou, e não só "timeout".
    { timeout: 10_000 },
  );
  return { ...resultado, roteador };
}
