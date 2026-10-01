import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterAll, afterEach, beforeAll } from 'vitest';
import { servidor } from './servidor';

// O jsdom não implementa matchMedia nem ResizeObserver, que a Mantine usa (ADR-0017).
Object.defineProperty(window, 'matchMedia', {
  writable: true,
  value: (query: string): MediaQueryList =>
    ({
      matches: false,
      media: query,
      onchange: null,
      addListener: () => undefined,
      removeListener: () => undefined,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
      dispatchEvent: () => false,
    }) as MediaQueryList,
});

class ResizeObserverFalso {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
}
window.ResizeObserver = ResizeObserverFalso;

// O Textarea com autosize recalcula a altura quando as fontes carregam (document.fonts), também ausente no jsdom.
Object.defineProperty(document, 'fonts', {
  configurable: true,
  value: {
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    ready: Promise.resolve(),
  },
});

// Requisição sem handler é erro: nenhum teste depende de rede real.
beforeAll(() => servidor.listen({ onUnhandledFrame: 'error' }));
afterEach(() => {
  cleanup();
  servidor.resetHandlers();
});
afterAll(() => servidor.close());
