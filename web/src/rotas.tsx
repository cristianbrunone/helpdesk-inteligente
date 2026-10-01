import { Navigate, type RouteObject } from 'react-router';
import { LayoutBase } from './componentes/LayoutBase';
import { Dashboard } from './paginas/Dashboard';
import { DetalheChamado } from './paginas/DetalheChamado';
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
      { path: 'chamados/:id', element: <DetalheChamado /> },
      { path: 'dashboard', element: <Dashboard /> },
      { path: '*', element: <NaoEncontrada /> },
    ],
  },
];
