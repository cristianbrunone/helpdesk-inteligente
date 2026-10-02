import { defineConfig, devices } from '@playwright/test';

/**
 * E2E contra o ambiente de pé (`docker compose up`, IA fake): o navegador fala com o Nginx, que serve o front e
 * encaminha /api, como em produção. Nada é simulado.
 *
 * - `E2E_BASE_URL`: onde está o front (padrão: a porta do compose).
 * - `E2E_NAVEGADOR`: canal de um navegador já instalado (ex.: `msedge`), para máquinas que não conseguem baixar o
 *   Chromium do Playwright (rede com inspeção TLS). Sem ele, usa o Chromium do Playwright, como no CI.
 */
export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
  expect: { timeout: 15_000 },
  // Sem novas tentativas: um E2E instável precisa aparecer, não ser escondido.
  retries: 0,
  workers: 1,
  forbidOnly: !!process.env.CI,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:8080',
    locale: 'pt-BR',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'setup',
      testMatch: /.*\.setup\.ts/,
      use: {
        ...devices['Desktop Chrome'],
        channel: process.env.E2E_NAVEGADOR || undefined,
      },
    },
    {
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome'],
        channel: process.env.E2E_NAVEGADOR || undefined,
        storageState: 'playwright/.auth/usuario.json',
      },
      dependencies: ['setup'],
    },
  ],
});
