import { useDocumentTitle } from '@mantine/hooks';

export const NOME_DA_APLICACAO = 'HelpDesk Inteligente';

/**
 * Título da aba do navegador por página: com vários chamados abertos em abas, dá para distinguir cada um (Sprint 7,
 * item M5 da análise de experiência).
 */
export function useTituloDaPagina(titulo: string): void {
  useDocumentTitle(`${titulo} — ${NOME_DA_APLICACAO}`);
}
