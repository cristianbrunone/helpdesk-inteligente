import { Navigate, type RouteObject } from 'react-router';
import { CarregandoPagina } from './componentes/CarregandoPagina';
import { LayoutBase } from './componentes/LayoutBase';
import { ListaChamados } from './paginas/ListaChamados';
import { NaoEncontrada } from './paginas/NaoEncontrada';

/**
 * A lista é a página de entrada e vai no pacote inicial. As outras são carregadas sob demanda (`lazy`): o dashboard
 * traz o Recharts, o formulário traz o Zod e o React Hook Form, e o detalhe traz o painel da triagem e o copiloto.
 */
export const rotas: RouteObject[] = [
  {
    path: '/',
    element: <LayoutBase />,
    hydrateFallbackElement: <CarregandoPagina />,
    children: [
      { index: true, element: <Navigate to="/chamados" replace /> },
      { path: 'chamados', element: <ListaChamados /> },
      {
        path: 'chamados/novo',
        lazy: async () => ({ Component: (await import('./paginas/NovoChamado')).NovoChamado }),
      },
      {
        path: 'chamados/:id',
        lazy: async () => ({
          Component: (await import('./paginas/DetalheChamado')).DetalheChamado,
        }),
      },
      {
        path: 'dashboard',
        lazy: async () => ({ Component: (await import('./paginas/Dashboard')).Dashboard }),
      },
      { path: '*', element: <NaoEncontrada /> },
    ],
  },
];
