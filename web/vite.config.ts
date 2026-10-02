import react from '@vitejs/plugin-react';
import { configDefaults, defineConfig } from 'vitest/config';

// Em desenvolvimento, /api vai para a API local: mesma origem, como o Nginx faz no Compose (sem CORS).
const alvoApi = process.env.VITE_API_PROXY ?? 'http://localhost:5080';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: { '/api': alvoApi },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/testes/setup.ts'],
    restoreMocks: true,
    // e2e/*.spec.ts são do Playwright (rodam contra o compose), não do Vitest.
    exclude: [...configDefaults.exclude, 'e2e/**'],
  },
});
