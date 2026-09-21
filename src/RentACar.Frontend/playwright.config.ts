import { defineConfig, devices } from '@playwright/test';

/** Yerel duman testi: production derlemesi /app/ altında, üretimdeki CSP ile servis edilir. */
const PORT = 4321;

export default defineConfig({
  testDir: './e2e',
  forbidOnly: true,
  fullyParallel: true,
  retries: 0,
  reporter: 'list',
  use: {
    baseURL: `http://127.0.0.1:${PORT}`,
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: `node e2e/sunucu.mjs ${PORT}`,
    url: `http://127.0.0.1:${PORT}/app/`,
    reuseExistingServer: false,
    timeout: 30_000,
  },
});
