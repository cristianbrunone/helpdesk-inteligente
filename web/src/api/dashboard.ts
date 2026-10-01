import { useQuery } from '@tanstack/react-query';
import { requisitar } from './cliente';
import type { Prioridade, StatusChamado } from '../dominio/chamado';

/** Contrato: GET /api/dashboard/resumo. Tudo agregado no banco (ADR-0009). */
export interface ResumoDashboard {
  totalChamados: number;
  /** Todos os status, na ordem de negócio, mesmo com zero. */
  porStatus: { status: StatusChamado; total: number }[];
  porPrioridade: { prioridade: Prioridade; total: number }[];
  tempoMedioResolucaoPorCategoria: {
    categoriaId: number;
    categoria: string;
    resolvidos: number;
    /** Nulo quando a categoria não tem resolvidos. */
    tempoMedioHoras: number | null;
  }[];
  ia: {
    /** Nula enquanto não houver decisão (RN-12). */
    taxaAceitacao: number | null;
    aceitas: number;
    rejeitadas: number;
    pendentes: number;
    falhas: number;
    porCategoria: {
      categoria: string;
      aceitas: number;
      rejeitadas: number;
      taxaAceitacao: number | null;
    }[];
    consumo30d: {
      operacao: string;
      modelo: string;
      chamadas: number;
      falhas: number;
      tokensEntrada: number | null;
      tokensSaida: number | null;
      latenciaP95Ms: number | null;
    }[];
  };
}

/** Os números mudam a cada chamado: um minuto de cache basta, e voltar à aba atualiza. */
export function useResumoDashboard() {
  return useQuery({
    queryKey: ['dashboard', 'resumo'] as const,
    queryFn: ({ signal }) => requisitar<ResumoDashboard>('/api/dashboard/resumo', { signal }),
    staleTime: 60_000,
  });
}
