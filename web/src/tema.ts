import { createTheme, type CSSVariablesResolver } from '@mantine/core';

/**
 * Ajustes mínimos sobre o tema padrão da Mantine (ADR-0017), mais o contraste do WCAG AA (≥ 4,5:1 em texto
 * normal), conferido com o axe-core na Sprint 5. Os tons 6 da paleta padrão não passam sobre branco.
 */
export const tema = createTheme({
  primaryColor: 'indigo',
  // indigo.6 dá 4,32:1 em links e no texto branco dos botões; indigo.7 dá 4,98:1.
  primaryShade: 7,
  // Texto preto ou branco conforme o fundo nas variantes "filled" (badges de status): um dos dois sempre passa.
  autoContrast: true,
  defaultRadius: 'md',
});

/**
 * Largura máxima do conteúdo das páginas de trabalho (lista, detalhe e dashboard), alinhado à esquerda junto ao
 * menu: a mesma em todas, para as bordas e o botão do topo não mudarem de lugar ao navegar. Formulários ficam mais
 * estreitos (Sprint 7, consistência visual).
 */
export const LARGURA_CONTEUDO = 1200;

/** Texto secundário ("dimmed"): o gray.6 padrão dá 3,32:1 sobre branco; o gray.7 dá 8,18:1. */
export const variaveisCss: CSSVariablesResolver = () => ({
  variables: {},
  light: { '--mantine-color-dimmed': 'var(--mantine-color-gray-7)' },
  dark: {},
});
