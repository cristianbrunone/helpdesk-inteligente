import { Navigate, type RouteObject } from 'react-router';
import { LayoutBase } from './componentes/LayoutBase';
import { ListaChamados } from './paginas/ListaChamados';
import { NaoEncontrada } from './paginas/NaoEncontrada';
import { NovoChamado } from './paginas/NovoChamado';

export const rotas: RouteObject[] = [
  {
    path: '/',
    element: <LayoutBase />,
    children: [
      { index: true, element: <Navigate to="/chamados" replace /> },
      { path: 'chamados', element: <ListaChamados /> },
      { path: 'chamados/novo', element: <NovoChamado /> },
      { path: '*', element: <NaoEncontrada /> },
    ],
  },
];
