import { createTheme } from '@mantine/core';

/** Ajustes mínimos sobre o tema padrão da Mantine (ADR-0017). */
export const tema = createTheme({
  primaryColor: 'indigo',
  defaultRadius: 'md',
});
