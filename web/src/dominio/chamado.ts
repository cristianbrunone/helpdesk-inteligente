/**
 * Valores do contrato (ASCII, PascalCase) e seus rótulos em pt-BR. Só apresentação: a máquina de estados fica no
 * backend, e a tela de detalhe usa as `transicoesPermitidas` devolvidas pela API.
 */
export const STATUS_CHAMADO = [
  'Aberto',
  'EmAndamento',
  'Resolvido',
  'Fechado',
  'Cancelado',
] as const;
export type StatusChamado = (typeof STATUS_CHAMADO)[number];

/** Na ordem de negócio (P-07). */
export const PRIORIDADES = ['Baixa', 'Media', 'Alta', 'Critica'] as const;
export type Prioridade = (typeof PRIORIDADES)[number];

export const ROTULO_STATUS: Record<StatusChamado, string> = {
  Aberto: 'Aberto',
  EmAndamento: 'Em andamento',
  Resolvido: 'Resolvido',
  Fechado: 'Fechado',
  Cancelado: 'Cancelado',
};

export const COR_STATUS: Record<StatusChamado, string> = {
  Aberto: 'blue',
  EmAndamento: 'yellow',
  Resolvido: 'teal',
  Fechado: 'gray',
  Cancelado: 'red',
};

export const ROTULO_PRIORIDADE: Record<Prioridade, string> = {
  Baixa: 'Baixa',
  Media: 'Média',
  Alta: 'Alta',
  Critica: 'Crítica',
};

export const COR_PRIORIDADE: Record<Prioridade, string> = {
  Baixa: 'gray',
  Media: 'blue',
  Alta: 'orange',
  Critica: 'red',
};

const FORMATO_DATA_HORA = new Intl.DateTimeFormat('pt-BR', {
  dateStyle: 'short',
  timeStyle: 'short',
});

/** A API devolve UTC; a tela mostra no fuso do navegador. */
export function formatarDataHora(iso: string): string {
  return FORMATO_DATA_HORA.format(new Date(iso));
}
