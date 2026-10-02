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

export const STATUS_TRIAGEM = ['Pendente', 'Concluida', 'Falhou', 'Aceita', 'Rejeitada'] as const;
export type StatusTriagem = (typeof STATUS_TRIAGEM)[number];

export const ROTULO_STATUS: Record<StatusChamado, string> = {
  Aberto: 'Aberto',
  EmAndamento: 'Em andamento',
  Resolvido: 'Resolvido',
  Fechado: 'Fechado',
  Cancelado: 'Cancelado',
};

/**
 * Cores dos badges de status (preenchidos). Os tons escuros garantem texto branco com contraste ≥ 5:1 (WCAG AA);
 * no amarelo, o autoContrast do tema usa texto escuro.
 */
export const COR_STATUS: Record<StatusChamado, string> = {
  Aberto: 'blue.8',
  EmAndamento: 'yellow',
  Resolvido: 'teal.9',
  Fechado: 'gray.7',
  Cancelado: 'red.9',
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

/** Texto do botão de cada destino. Só rótulo: quais destinos existem quem diz é a API (`transicoesPermitidas`). */
export function rotuloDaAcao(atual: StatusChamado, destino: StatusChamado): string {
  switch (destino) {
    case 'EmAndamento':
      return atual === 'Resolvido' ? 'Reabrir' : 'Iniciar atendimento';
    case 'Resolvido':
      return 'Marcar como resolvido';
    case 'Fechado':
      return 'Fechar';
    case 'Cancelado':
      return 'Cancelar chamado';
    case 'Aberto':
      return 'Voltar para aberto';
  }
}
