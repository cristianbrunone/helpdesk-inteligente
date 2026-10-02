import { useMediaQuery } from '@mantine/hooks';

/** Abaixo do breakpoint `sm` da Mantine (48em), o mesmo do menu hambúrguer. */
export const CONSULTA_CELULAR = '(max-width: 47.99em)';

/**
 * Se a tela é de celular, para as trocas que o CSS sozinho não resolve (componentes diferentes, e não só
 * posições). Lido já na primeira renderização, sem piscar o layout do desktop.
 */
export function useCelular(): boolean {
  return useMediaQuery(CONSULTA_CELULAR, false, { getInitialValueInEffect: false });
}
