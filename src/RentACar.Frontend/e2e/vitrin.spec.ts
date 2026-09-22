import { expect, test } from '@playwright/test';

import { ciddiIhlaller, hatalariTopla, oturumAc } from './ortak';
import { hazirBekle, tasmaOlc, VITRIN_SAYFALARI } from './vitrin-sayfalari';

/**
 * F3.7 kapıları (sahte arka uçla, üretim derlemesi + CSP): vitrin eksiksiz, her sayfa iki temada axe
 * ciddi/kritik 0 ve konsol hatası yok, 320/390/768 (telefon/tablet öykünmesi) ve 1440 px'te gövde
 * yatay taşması 0. Görsel regresyon ayrı projede (`gorsel.spec.ts`, Linux imajı).
 */
test.beforeEach(async ({ page }) => oturumAc(page));

test('vitrin dizini her çekirdek vitrinine bağlanır (eksiksiz)', async ({ page }) => {
  await page.goto('/app/vitrin');
  const liste = page.getByRole('list', { name: 'Vitrin sayfaları' });
  const beklenen = VITRIN_SAYFALARI.map((s) => s.yol).filter((y) => y.startsWith('/app/vitrin/'));
  await expect(liste.getByRole('link')).toHaveCount(beklenen.length);
  const hrefler = await liste
    .getByRole('link')
    .evaluateAll((a) => a.map((e) => e.getAttribute('href')));
  expect([...hrefler].sort()).toEqual([...beklenen].sort());

  // Ana sayfadan da ulaşılır.
  await page.goto('/app/');
  await page.getByRole('link', { name: 'Tüm vitrin' }).click();
  await expect(page).toHaveURL(/\/app\/vitrin$/);
});

for (const sayfa of VITRIN_SAYFALARI) {
  test(`${sayfa.yol}: açık ve koyu temada axe ciddi/kritik 0, konsol hatası yok`, async ({
    page,
  }) => {
    const hatalar = hatalariTopla(page);
    await sayfa.hazirla?.(page);
    await page.emulateMedia({ colorScheme: 'light' });
    await page.goto(sayfa.yol);
    await hazirBekle(page, sayfa);
    expect(await ciddiIhlaller(page), 'açık tema').toEqual([]);

    await page.emulateMedia({ colorScheme: 'dark' });
    await expect
      .poll(() => page.evaluate(() => getComputedStyle(document.body).backgroundColor))
      .toBe('rgb(13, 19, 28)');
    expect(await ciddiIhlaller(page), 'koyu tema').toEqual([]);
    expect(hatalar).toEqual([]);
  });
}

test.describe('mobil taşma (dokunmatik öykünme)', () => {
  test.use({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });

  for (const sayfa of VITRIN_SAYFALARI) {
    test(`${sayfa.yol}: 320/390/768 px gövde yatay taşması yok`, async ({ page }) => {
      await sayfa.hazirla?.(page);
      for (const genislik of [320, 390, 768]) {
        await page.setViewportSize({ width: genislik, height: 844 });
        await page.goto(sayfa.yol);
        await hazirBekle(page, sayfa);
        expect(await tasmaOlc(page), `${genislik}px`).toEqual({ tasma: 0, suclular: [] });
      }
    });
  }
});

test('1440 px: hiçbir vitrin sayfasında gövde yatay taşması yok', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  for (const sayfa of VITRIN_SAYFALARI) {
    await sayfa.hazirla?.(page);
    await page.goto(sayfa.yol);
    await hazirBekle(page, sayfa);
    expect(await tasmaOlc(page), sayfa.yol).toEqual({ tasma: 0, suclular: [] });
  }
});
