import { expect, test } from '@playwright/test';

import { logIn } from './ortak';
import { ORTAM } from './ortam';
import { waitReady, SHOWCASE_PAGES } from './vitrin-sayfalari';

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

const WIDTHS = [
  { genislik: 320, yukseklik: 640 },
  { genislik: 390, yukseklik: 844 },
  { genislik: 768, yukseklik: 1024 },
  { genislik: 1440, yukseklik: 900 },
] as const;

test.beforeEach(async ({ page }) => logIn(page));

for (const pageRef of SHOWCASE_PAGES) {
  for (const theme of ['light', 'dark'] as const) {
    test(`${pageRef.ad} (${theme === 'light' ? 'açık' : 'koyu'})`, async ({ page }) => {
      await pageRef.hazirla?.(page);
      await page.emulateMedia({ colorScheme: theme, reducedMotion: 'reduce' });
      for (const { genislik: width, yukseklik: height } of WIDTHS) {
        await page.setViewportSize({ width: width, height: height });
        await page.goto(pageRef.yol);
        await waitReady(page, pageRef);
        await expect(page).toHaveScreenshot(`${pageRef.ad}-${theme}-${width}.png`, {
          fullPage: true,
        });
      }
    });
  }
}
