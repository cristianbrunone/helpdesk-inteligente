import { MantineProvider } from '@mantine/core';
import { QueryClientProvider } from '@tanstack/react-query';
import { createBrowserRouter } from 'react-router';
import { RouterProvider } from 'react-router/dom';
import { criarQueryClient } from './api/queryClient';
import { rotas } from './rotas';
import { tema } from './tema';

const queryClient = criarQueryClient();
const roteador = createBrowserRouter(rotas);

export function App() {
  return (
    <MantineProvider theme={tema}>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={roteador} />
      </QueryClientProvider>
    </MantineProvider>
  );
}
