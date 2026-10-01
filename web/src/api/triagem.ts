import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { Categoria } from './categorias';
import type { ChamadoDetalhe, ChamadoVersionado } from './chamados';
import { requisitar, requisitarComResposta } from './cliente';
import type { Prioridade, StatusTriagem } from '../dominio/chamado';

/** Documento que o RAG usou na sugestão: chamado resolvido (com número) ou artigo da base de conhecimento. */
export interface FonteTriagem {
  tipo: 'chamado' | 'artigo';
  id: string;
  numero: number | null;
  titulo: string;
  /** Similaridade de cosseno, de 0 a 1. */
  similaridade: number;
}

/** Triagem vigente no detalhe do chamado (contrato: GET /api/chamados/{id}). */
export interface TriagemDetalhe {
  id: string;
  status: StatusTriagem;
  categoriaSugerida: Categoria | null;
  prioridadeSugerida: Prioridade | null;
  resumo: string | null;
  respostaSugerida: string | null;
  confianca: number | null;
  modelo: string | null;
  promptVersao: string | null;
  fontes: FonteTriagem[];
  /** Mensagem amigável quando a triagem falhou (nunca o detalhe técnico). */
  erro: string | null;
  criadoEm: string;
  concluidaEm: string | null;
  decididaPor: string | null;
  decididaEm: string | null;
  totalTriagens: number;
}

/** Kill switches de IA (ADR-0021): a interface se adapta ao que está ativo. */
export interface ConfiguracaoIA {
  triagem: boolean;
  copiloto: boolean;
}

export function useConfiguracaoIA() {
  return useQuery({
    queryKey: ['config', 'ia'] as const,
    queryFn: ({ signal }) => requisitar<ConfiguracaoIA>('/api/config/ia', { signal }),
    // As flags só mudam reiniciando os contêineres.
    staleTime: 5 * 60_000,
  });
}

/**
 * Polling com backoff enquanto a triagem está pendente: a cada 2 s nos primeiros 10 s, 5 s até 1 min e 15 s
 * depois (o provedor real pode demorar; o fake responde em milissegundos). Fora de "Pendente", para.
 */
export function intervaloDePolling(
  versionado: ChamadoVersionado | undefined,
  agora: number = Date.now(),
): number | false {
  const triagem = versionado?.chamado.triagem;
  if (triagem?.status !== 'Pendente') return false;
  const idade = agora - new Date(triagem.criadoEm).getTime();
  return idade < 10_000 ? 2_000 : idade < 60_000 ? 5_000 : 15_000;
}

const caminho = (chamadoId: string) => `/api/chamados/${encodeURIComponent(chamadoId)}/triagem`;

/** "Refazer" (ou a primeira triagem): cria uma pendente; o detalhe é recarregado e passa a fazer polling. */
export function useRefazerTriagem(chamadoId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => requisitar<TriagemDetalhe>(caminho(chamadoId), { method: 'POST' }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['chamados'] }),
  });
}

interface Decisao {
  acao: 'aceitar' | 'rejeitar';
  decididaPor: string;
  motivo?: string;
  etag: string;
}

/** Aceitar ou rejeitar a triagem vigente, com If-Match (o aceite altera o chamado). */
export function useDecidirTriagem(chamadoId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ acao, etag, ...corpo }: Decisao): Promise<ChamadoVersionado> => {
      const { dados, headers } = await requisitarComResposta<ChamadoDetalhe>(
        `${caminho(chamadoId)}/${acao}`,
        { method: 'POST', corpo, headers: { 'If-Match': etag } },
      );
      return { chamado: dados, etag: headers.get('ETag') ?? '' };
    },
    onSuccess: (atualizado) => {
      queryClient.setQueryData(['chamados', 'detalhe', chamadoId], atualizado);
      void queryClient.invalidateQueries({ queryKey: ['chamados', 'lista'] });
    },
  });
}
