import { MantineProvider } from '@mantine/core';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render } from '@testing-library/react';
import type { ReactElement } from 'react';
import { createMemoryRouter, MemoryRouter } from 'react-router';
import { RouterProvider } from 'react-router/dom';
import { rotas } from '../rotas';
import { tema } from '../tema';

function criarQueryClientDeTeste(): QueryClient {
  // Sem novas tentativas: o teste de erro vê o erro na primeira resposta.
  return new QueryClient({ defaultOptions: { queries: { retry: false } } });
}

/** Renderiza um componente com os mesmos providers da aplicação. */
export function renderizar(ui: ReactElement, { rota = '/' }: { rota?: string } = {}) {
  return render(
    <MantineProvider theme={tema}>
      <QueryClientProvider client={criarQueryClientDeTeste()}>
        <MemoryRouter initialEntries={[rota]}>{ui}</MemoryRouter>
      </QueryClientProvider>
    </MantineProvider>,
  );
}

/** Renderiza a aplicação inteira (rotas reais) a partir de uma URL. */
export function renderizarApp(rota = '/') {
  const roteador = createMemoryRouter(rotas, { initialEntries: [rota] });
  return render(
    <MantineProvider theme={tema}>
      <QueryClientProvider client={criarQueryClientDeTeste()}>
        <RouterProvider router={roteador} />
      </QueryClientProvider>
    </MantineProvider>,
  );
}
