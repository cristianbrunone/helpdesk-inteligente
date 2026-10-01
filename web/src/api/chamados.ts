import { keepPreviousData, useQuery } from '@tanstack/react-query';
import type { Categoria } from './categorias';
import { requisitar } from './cliente';
import type { Prioridade, StatusChamado } from '../dominio/chamado';
import { paraQueryString, type FiltrosChamados } from './filtrosChamados';

export interface ChamadoResumo {
  id: string;
  numero: number;
  titulo: string;
  status: StatusChamado;
  prioridade: Prioridade;
  categoria: Categoria | null;
  solicitanteNome: string;
  criadoEm: string;
  atualizadoEm: string;
}

export interface ResultadoPaginado<T> {
  itens: T[];
  pagina: number;
  tamanhoPagina: number;
  totalItens: number;
  totalPaginas: number;
}

export const chavesChamados = {
  todos: ['chamados'] as const,
  lista: (filtros: FiltrosChamados) => ['chamados', 'lista', filtros] as const,
};

export function listarChamados(
  filtros: FiltrosChamados,
  signal?: AbortSignal,
): Promise<ResultadoPaginado<ChamadoResumo>> {
  const query = paraQueryString(filtros).toString();
  return requisitar<ResultadoPaginado<ChamadoResumo>>(`/api/chamados${query ? `?${query}` : ''}`, {
    signal,
  });
}

/** Lista paginada. Ao trocar de filtro ou página, mantém a página anterior na tela até a nova chegar. */
export function useChamados(filtros: FiltrosChamados) {
  return useQuery({
    queryKey: chavesChamados.lista(filtros),
    queryFn: ({ signal }) => listarChamados(filtros, signal),
    placeholderData: keepPreviousData,
  });
}
