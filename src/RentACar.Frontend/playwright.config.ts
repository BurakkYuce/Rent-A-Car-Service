import { defineConfig, devices } from '@playwright/test';

import { ORTAM } from './e2e/ortam';

/** Yerel duman testi: production derlemesi /app/ altında, üretimdeki CSP ile servis edilir. */
const PORT = 4321;

/**
 * İki proje:
 * - `chromium`: işlev, axe, taşma — her platformda (`npm run e2e`).
 * - `gorsel`: ekran görüntüsü karşılaştırması — YALNIZ Playwright Linux imajında (`npm run e2e:gorsel`,
 *   CI `e2e` işi o imajın içinde koşar). Tabanlar `e2e/gorsel-tabanlari/`.
 */
export default defineConfig({
  testDir: './e2e',
  forbidOnly: true,
  fullyParallel: true,
  retries: 0,
  reporter: 'list',
  // CI eksik tabanı YAZMAZ, kırmızı verir (taban yalnız bilinçli güncellemeyle gelir).
  updateSnapshots: ORTAM.ci ? 'none' : 'missing',
  snapshotPathTemplate: '{testDir}/gorsel-tabanlari/{arg}{ext}',
  expect: {
    toHaveScreenshot: {
      // Aynı imaj + aynı mimaride fark 0 beklenir; pay yalnız alt-piksel kenar yumuşatma içindir.
      // %0,5: 1440×900'de ~6.500 piksel — bir düğmenin kayması ya da renk token'ı değişimi bunu aşar.
      maxDiffPixelRatio: 0.005,
      threshold: 0.2,
      animations: 'disabled',
      caret: 'hide',
      scale: 'css',
    },
  },
  use: {
    baseURL: `http://127.0.0.1:${PORT}`,
    trace: 'retain-on-failure',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] }, testIgnore: /gorsel\.spec\.ts/ },
    { name: 'gorsel', use: { ...devices['Desktop Chrome'] }, testMatch: /gorsel\.spec\.ts/ },
  ],
  webServer: {
    command: `node e2e/sunucu.mjs ${PORT}`,
    url: `http://127.0.0.1:${PORT}/app/`,
    reuseExistingServer: false,
    timeout: 30_000,
  },
});
