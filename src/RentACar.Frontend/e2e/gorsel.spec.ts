import { expect, test } from '@playwright/test';

import { oturumAc } from './ortak';
import { ORTAM } from './ortam';
import { hazirBekle, VITRIN_SAYFALARI } from './vitrin-sayfalari';

/**
 * F3.7 görsel regresyon: her vitrin sayfası açık/koyu × 320/390/768/1440 px, tam sayfa. Tabanlar
 * (`e2e/gorsel-tabanlari/`) YALNIZ CI'ın kullandığı Playwright Linux imajında üretilir
 * (`mcr.microsoft.com/playwright:v<sürüm>-noble`, x86_64): macOS/Windows çizimi piksel düzeyinde farklı.
 *   Doğrula:  npm run e2e:gorsel            (Docker; imaj @playwright/test sürümünden)
 *   Güncelle: npm run e2e:gorsel:guncelle   (ya da Actions → "Görsel tabanlar" elle tetikle → artifact)
 * Animasyon kapalı, imleç gizli, fontlar self-host; eşik playwright.config.ts'te.
 */
test.skip(
  !ORTAM.linux,
  'Görsel tabanlar Linux imajında üretilir: npm run e2e:gorsel (Docker) kullanın.',
);

const GENISLIKLER = [
  { genislik: 320, yukseklik: 640 },
  { genislik: 390, yukseklik: 844 },
  { genislik: 768, yukseklik: 1024 },
  { genislik: 1440, yukseklik: 900 },
] as const;

test.beforeEach(async ({ page }) => oturumAc(page));

for (const sayfa of VITRIN_SAYFALARI) {
  for (const tema of ['light', 'dark'] as const) {
    test(`${sayfa.ad} (${tema === 'light' ? 'açık' : 'koyu'})`, async ({ page }) => {
      await sayfa.hazirla?.(page);
      await page.emulateMedia({ colorScheme: tema, reducedMotion: 'reduce' });
      for (const { genislik, yukseklik } of GENISLIKLER) {
        await page.setViewportSize({ width: genislik, height: yukseklik });
        await page.goto(sayfa.yol);
        await hazirBekle(page, sayfa);
        await expect(page).toHaveScreenshot(`${sayfa.ad}-${tema}-${genislik}.png`, {
          fullPage: true,
        });
      }
    });
  }
}
