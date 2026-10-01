import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { Categoria } from './categorias';
import { requisitar, requisitarComResposta } from './cliente';
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

export interface Comentario {
  id: string;
  autor: string;
  texto: string;
  criadoEm: string;
}

export interface RegistroHistorico {
  statusAnterior: StatusChamado | null;
  statusNovo: StatusChamado;
  alteradoEm: string;
  alteradoPor: string;
}

/** Detalhe (contrato: GET /api/chamados/{id}). `transicoesPermitidas` e `podeComentar` vêm do domínio. */
export interface ChamadoDetalhe {
  id: string;
  numero: number;
  titulo: string;
  descricao: string;
  solicitanteNome: string;
  solicitanteEmail: string;
  categoria: Categoria | null;
  prioridade: Prioridade;
  status: StatusChamado;
  criadoEm: string;
  atualizadoEm: string;
  resolvidoEm: string | null;
  transicoesPermitidas: StatusChamado[];
  podeComentar: boolean;
  comentarios: Comentario[];
  historico: RegistroHistorico[];
}

/** Corpo de POST /api/chamados. Categoria e prioridade são opcionais (P-02): a triagem sugere. */
export interface NovoChamado {
  titulo: string;
  descricao: string;
  solicitanteNome: string;
  solicitanteEmail: string;
  categoriaId: number | null;
  prioridade: Prioridade | null;
}

export const chavesChamados = {
  todos: ['chamados'] as const,
  lista: (filtros: FiltrosChamados) => ['chamados', 'lista', filtros] as const,
  detalhe: (id: string) => ['chamados', 'detalhe', id] as const,
};

/** O detalhe e o ETag (versão) que ele tinha quando foi lido: vai no If-Match das escritas. */
export interface ChamadoVersionado {
  chamado: ChamadoDetalhe;
  etag: string;
}

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

export function criarChamado(dados: NovoChamado): Promise<ChamadoDetalhe> {
  return requisitar<ChamadoDetalhe>('/api/chamados', { method: 'POST', corpo: dados });
}

/** Cria o chamado; as listas em cache ficam desatualizadas e são recarregadas. */
export function useCriarChamado() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: criarChamado,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: chavesChamados.todos }),
  });
}

export async function obterChamado(id: string, signal?: AbortSignal): Promise<ChamadoVersionado> {
  const { dados, headers } = await requisitarComResposta<ChamadoDetalhe>(
    `/api/chamados/${encodeURIComponent(id)}`,
    { signal },
  );
  return { chamado: dados, etag: headers.get('ETag') ?? '' };
}

export function useChamado(id: string) {
  return useQuery({
    queryKey: chavesChamados.detalhe(id),
    queryFn: ({ signal }) => obterChamado(id, signal),
  });
}

export interface MudancaDeStatus {
  status: StatusChamado;
  alteradoPor: string;
  comentario?: string;
}

export interface NovoComentario {
  autor: string;
  texto: string;
}

/**
 * Escritas sobre o chamado mandam o ETag lido no If-Match: se outra pessoa alterou o chamado nesse meio-tempo,
 * a API responde 412 e a tela recarrega a versão atual em vez de sobrescrever.
 */
export function useMudarStatus(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ etag, ...corpo }: MudancaDeStatus & { etag: string }) => {
      const { dados, headers } = await requisitarComResposta<ChamadoDetalhe>(
        `/api/chamados/${encodeURIComponent(id)}/status`,
        { method: 'PATCH', corpo, headers: { 'If-Match': etag } },
      );
      return { chamado: dados, etag: headers.get('ETag') ?? '' };
    },
    onSuccess: (atualizado) => {
      queryClient.setQueryData(chavesChamados.detalhe(id), atualizado);
      void queryClient.invalidateQueries({ queryKey: chavesChamados.todos, refetchType: 'none' });
    },
  });
}

export function useComentar(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ etag, ...corpo }: NovoComentario & { etag: string }) =>
      requisitar<Comentario>(`/api/chamados/${encodeURIComponent(id)}/comentarios`, {
        method: 'POST',
        corpo,
        headers: { 'If-Match': etag },
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: chavesChamados.detalhe(id) }),
  });
}
