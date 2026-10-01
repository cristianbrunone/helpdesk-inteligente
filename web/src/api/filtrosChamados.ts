import {
  PRIORIDADES,
  STATUS_CHAMADO,
  type Prioridade,
  type StatusChamado,
} from '../dominio/chamado';

export type Ordenacao = 'criadoEm' | 'prioridade';
export type Direcao = 'asc' | 'desc';

/**
 * Filtros da lista. A URL da página usa exatamente os nomes e valores da query da API (contrato §3):
 * recarregar ou compartilhar o link reproduz a mesma consulta.
 */
export interface FiltrosChamados {
  status: StatusChamado[];
  prioridade: Prioridade[];
  categoriaId: number[];
  semCategoria: boolean;
  q: string;
  criadoDe: string;
  criadoAte: string;
  ordenarPor: Ordenacao;
  direcao: Direcao;
  pagina: number;
}

/** A API recusa busca com menos de 3 caracteres (ADR-0008): abaixo disso, o termo não é enviado. */
export const BUSCA_TAMANHO_MINIMO = 3;

export const FILTROS_PADRAO: FiltrosChamados = {
  status: [],
  prioridade: [],
  categoriaId: [],
  semCategoria: false,
  q: '',
  criadoDe: '',
  criadoAte: '',
  ordenarPor: 'criadoEm',
  direcao: 'desc',
  pagina: 1,
};

const DATA_ISO = /^\d{4}-\d{2}-\d{2}$/;

/** Lê os filtros da URL ignorando o que não for válido (link editado à mão nunca quebra a tela). */
export function deQueryString(params: URLSearchParams): FiltrosChamados {
  const pagina = Number(params.get('pagina'));
  const data = (nome: string) => {
    const valor = params.get(nome) ?? '';
    return DATA_ISO.test(valor) ? valor : '';
  };

  return {
    status: unicos(params.getAll('status').filter(ehUm(STATUS_CHAMADO))),
    prioridade: unicos(params.getAll('prioridade').filter(ehUm(PRIORIDADES))),
    categoriaId: unicos(
      params
        .getAll('categoriaId')
        .map(Number)
        .filter((id) => Number.isInteger(id) && id > 0),
    ),
    semCategoria: params.get('semCategoria') === 'true',
    q: (params.get('q') ?? '').trim(),
    criadoDe: data('criadoDe'),
    criadoAte: data('criadoAte'),
    ordenarPor: params.get('ordenarPor') === 'prioridade' ? 'prioridade' : 'criadoEm',
    direcao: params.get('direcao') === 'asc' ? 'asc' : 'desc',
    pagina: Number.isInteger(pagina) && pagina > 1 ? pagina : 1,
  };
}

/** Só os valores diferentes do padrão, para a URL ficar curta e legível. */
export function paraQueryString(filtros: FiltrosChamados): URLSearchParams {
  const params = new URLSearchParams();
  filtros.status.forEach((s) => params.append('status', s));
  filtros.prioridade.forEach((p) => params.append('prioridade', p));
  filtros.categoriaId.forEach((id) => params.append('categoriaId', String(id)));
  if (filtros.semCategoria) params.set('semCategoria', 'true');
  if (filtros.q.trim().length >= BUSCA_TAMANHO_MINIMO) params.set('q', filtros.q.trim());
  if (filtros.criadoDe) params.set('criadoDe', filtros.criadoDe);
  if (filtros.criadoAte) params.set('criadoAte', filtros.criadoAte);
  if (filtros.ordenarPor !== FILTROS_PADRAO.ordenarPor)
    params.set('ordenarPor', filtros.ordenarPor);
  if (filtros.direcao !== FILTROS_PADRAO.direcao) params.set('direcao', filtros.direcao);
  if (filtros.pagina > 1) params.set('pagina', String(filtros.pagina));
  return params;
}

/** Quantos filtros (não contando ordenação e página) estão ativos. */
export function contarFiltrosAtivos(filtros: FiltrosChamados): number {
  return (
    filtros.status.length +
    filtros.prioridade.length +
    filtros.categoriaId.length +
    (filtros.semCategoria ? 1 : 0) +
    (filtros.q ? 1 : 0) +
    (filtros.criadoDe ? 1 : 0) +
    (filtros.criadoAte ? 1 : 0)
  );
}

function ehUm<T extends string>(valores: readonly T[]) {
  return (valor: string): valor is T => (valores as readonly string[]).includes(valor);
}

function unicos<T>(valores: T[]): T[] {
  return [...new Set(valores)];
}
