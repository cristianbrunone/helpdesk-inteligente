import { Navigate, type RouteObject } from 'react-router';
import { LayoutBase } from './componentes/LayoutBase';
import { ListaChamados } from './paginas/ListaChamados';
import { NaoEncontrada } from './paginas/NaoEncontrada';

export const rotas: RouteObject[] = [
  {
    path: '/',
    element: <LayoutBase />,
    children: [
      { index: true, element: <Navigate to="/chamados" replace /> },
      { path: 'chamados', element: <ListaChamados /> },
      { path: '*', element: <NaoEncontrada /> },
    ],
  },
];
