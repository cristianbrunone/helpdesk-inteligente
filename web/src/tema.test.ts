import { DEFAULT_THEME, mergeMantineTheme } from '@mantine/core';
import { describe, expect, it } from 'vitest';
import { tema, variaveisCss } from './tema';

const claro = variaveisCss(mergeMantineTheme(DEFAULT_THEME, tema)).light;

/** Razão de contraste do WCAG entre duas cores #rrggbb. */
function contraste(a: string, b: string): number {
  const luminancia = (hex: string) => {
    const [r = 0, g = 0, b = 0] = [1, 3, 5].map((i) => {
      const c = parseInt(hex.slice(i, i + 2), 16) / 255;
      return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
    });
    return 0.2126 * r + 0.7152 * g + 0.0722 * b;
  };
  const [maior, menor] = [luminancia(a), luminancia(b)].sort((x, y) => y - x);
  return ((maior ?? 0) + 0.05) / ((menor ?? 0) + 0.05);
}

/** O tom N de uma cor, a partir de "var(--mantine-color-<cor>-N)". */
function tom(variavel: string | undefined): string {
  const [, cor = '', indice = ''] = /--mantine-color-(\w+)-(\d)\)$/.exec(variavel ?? '') ?? [];
  return DEFAULT_THEME.colors[cor]?.[Number(indice)] ?? '';
}

describe('Contraste do tema (WCAG AA, 4,5:1)', () => {
  it('o erro dos campos passa sobre branco', () => {
    expect(contraste(tom(claro['--mantine-color-error']), '#ffffff')).toBeGreaterThanOrEqual(4.5);
  });

  it.each(['indigo', 'red', 'violet'])(
    'a variante "light" de %s passa no fundo normal e no hover',
    (cor) => {
      const texto = DEFAULT_THEME.colors[cor]?.[9] ?? '';
      expect(contraste(texto, tom(claro[`--mantine-color-${cor}-light`]))).toBeGreaterThanOrEqual(
        4.5,
      );
      expect(
        contraste(texto, tom(claro[`--mantine-color-${cor}-light-hover`])),
      ).toBeGreaterThanOrEqual(4.5);
    },
  );
});
