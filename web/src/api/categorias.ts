import { useQuery } from '@tanstack/react-query';
import { requisitar } from './cliente';

export interface Categoria {
  id: number;
  nome: string;
}

export const chavesCategorias = {
  todas: ['categorias'] as const,
};

export function listarCategorias(signal?: AbortSignal): Promise<Categoria[]> {
  return requisitar<Categoria[]>('/api/categorias', { signal });
}

/** Categorias mudam raramente: ficam em cache por 5 minutos. */
export function useCategorias() {
  return useQuery({
    queryKey: chavesCategorias.todas,
    queryFn: ({ signal }) => listarCategorias(signal),
    staleTime: 5 * 60_000,
  });
}
