import { createTheme, NavLink, type CSSVariablesResolver } from '@mantine/core';

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
  components: {
    // O item do menu lateral (ativo e em hover) segue o raio dos cartões e botões, em vez de um retângulo reto.
    NavLink: NavLink.extend({ styles: { root: { borderRadius: 'var(--mantine-radius-md)' } } }),
  },
});

/**
 * Largura máxima do conteúdo das páginas de trabalho (lista, detalhe e dashboard), alinhado à esquerda junto ao
 * menu: a mesma em todas, para as bordas e o botão do topo não mudarem de lugar ao navegar. Formulários ficam mais
 * estreitos (Sprint 7, consistência visual).
 */
export const LARGURA_CONTEUDO = 1200;

/**
 * Ajustes de contraste do WCAG AA (≥ 4,5:1) no esquema claro:
 * - Texto secundário ("dimmed"): o gray.6 padrão dá 3,32:1 sobre branco; o gray.7 dá 8,18:1.
 * - Erro dos campos (texto e mensagem): o red.6 padrão dá 3,28:1 sobre branco; o red.9 dá 5,46:1. Achado na
 *   auditoria da Sprint 7, que passou a cobrir os formulários com erro.
 * - Variante "light" (botões e badges): o texto é o tom 9, e o fundo de hover padrão (tom 2) deixava indigo em
 *   4,13:1 e red em 3,76:1. Fundo no tom 0 e hover no tom 1: indigo 6,05 e 5,34; red 5,10 e 4,51; violet 6,34 e 5,39.
 *   Verde, amarelo, laranja e lima não passam nem assim: não use essas cores na variante "light".
 */
export const variaveisCss: CSSVariablesResolver = (theme) => ({
  variables: {},
  light: {
    '--mantine-color-dimmed': 'var(--mantine-color-gray-7)',
    '--mantine-color-error': 'var(--mantine-color-red-9)',
    ...Object.fromEntries(
      Object.keys(theme.colors).flatMap((cor) => [
        [`--mantine-color-${cor}-light`, `var(--mantine-color-${cor}-0)`],
        [`--mantine-color-${cor}-light-hover`, `var(--mantine-color-${cor}-1)`],
      ]),
    ),
  },
  dark: {},
});
