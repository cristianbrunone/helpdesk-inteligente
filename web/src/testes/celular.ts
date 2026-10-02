import { vi } from 'vitest';
import { CONSULTA_CELULAR } from '../hooks/useCelular';

/** Simula uma tela de celular (abaixo de 48em). O `restoreMocks` do Vitest desfaz ao fim de cada teste. */
export function simularCelular() {
  const original = window.matchMedia;
  vi.spyOn(window, 'matchMedia').mockImplementation((consulta: string) => ({
    ...original(consulta),
    matches: consulta === CONSULTA_CELULAR,
  }));
}
