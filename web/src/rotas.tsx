import type { RouteObject } from 'react-router';
import { LayoutBase } from './componentes/LayoutBase';
import { Inicio } from './paginas/Inicio';
import { NaoEncontrada } from './paginas/NaoEncontrada';

export const rotas: RouteObject[] = [
  {
    path: '/',
    element: <LayoutBase />,
    children: [
      { index: true, element: <Inicio /> },
      { path: '*', element: <NaoEncontrada /> },
    ],
  },
];
