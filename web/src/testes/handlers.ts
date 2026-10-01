import { http, HttpResponse } from 'msw';
import type { Categoria } from '../api/categorias';
import type { ChamadoResumo, ResultadoPaginado } from '../api/chamados';

export const categoriasPadrao: Categoria[] = [
  { id: 1, nome: 'Acesso/Login' },
  { id: 3, nome: 'Bug no sistema' },
  { id: 4, nome: 'Dúvida' },
  { id: 2, nome: 'Financeiro' },
  { id: 5, nome: 'Infraestrutura' },
];

export const chamadosPadrao: ChamadoResumo[] = [
  {
    id: '0192f0c1-0000-7000-8000-000000000001',
    numero: 1042,
    titulo: 'Não consigo acessar o portal financeiro',
    status: 'EmAndamento',
    prioridade: 'Critica',
    categoria: { id: 2, nome: 'Financeiro' },
    solicitanteNome: 'Maria Exemplo',
    criadoEm: '2026-09-30T14:03:00Z',
    atualizadoEm: '2026-09-30T15:00:00Z',
    triagemStatus: 'Concluida',
  },
  {
    id: '0192f0c1-0000-7000-8000-000000000002',
    numero: 1041,
    titulo: 'Impressora da recepção não imprime',
    status: 'Aberto',
    prioridade: 'Media',
    categoria: null,
    solicitanteNome: 'João Exemplo',
    criadoEm: '2026-09-29T10:00:00Z',
    atualizadoEm: '2026-09-29T10:00:00Z',
    triagemStatus: null,
  },
];

export function paginaDe(
  itens: ChamadoResumo[],
  extra: Partial<ResultadoPaginado<ChamadoResumo>> = {},
): ResultadoPaginado<ChamadoResumo> {
  return {
    itens,
    pagina: 1,
    tamanhoPagina: 20,
    totalItens: itens.length,
    totalPaginas: 1,
    ...extra,
  };
}

/** Respostas padrão da API nos testes; cada teste pode sobrescrever com servidor.use(...). */
export const handlers = [
  http.get('/api/categorias', () => HttpResponse.json(categoriasPadrao)),
  http.get('/api/chamados', () => HttpResponse.json(paginaDe(chamadosPadrao))),
  http.get('/api/config/ia', () => HttpResponse.json({ triagem: true, copiloto: true })),
];
