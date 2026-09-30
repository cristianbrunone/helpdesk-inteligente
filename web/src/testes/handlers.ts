import { http, HttpResponse } from 'msw';
import type { Categoria } from '../api/categorias';

export const categoriasPadrao: Categoria[] = [
  { id: 1, nome: 'Acesso/Login' },
  { id: 3, nome: 'Bug no sistema' },
  { id: 4, nome: 'Dúvida' },
  { id: 2, nome: 'Financeiro' },
  { id: 5, nome: 'Infraestrutura' },
];

/** Respostas padrão da API nos testes; cada teste pode sobrescrever com servidor.use(...). */
export const handlers = [http.get('/api/categorias', () => HttpResponse.json(categoriasPadrao))];
