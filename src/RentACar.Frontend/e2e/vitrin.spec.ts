import { expect, test } from '@playwright/test';

import { seriousViolations, collectErrors, logIn } from './ortak';
import { waitReady, measureOverflow, SHOWCASE_PAGES } from './vitrin-sayfalari';

/**
 * F3.7 kapıları (sahte arka uçla, üretim derlemesi + CSP): vitrin eksiksiz, her sayfa iki temada axe
 * ciddi/kritik 0 ve konsol hatası yok, 320/390/768 (telefon/tablet öykünmesi) ve 1440 px'te gövde
 * yatay taşması 0. Görsel regresyon ayrı projede (`gorsel.spec.ts`, Linux imajı).
 */
test.beforeEach(async ({ page }) => logIn(page));

test('vitrin dizini her çekirdek vitrinine bağlanır (eksiksiz)', async ({ page }) => {
  await page.goto('/app/vitrin');
  const list = page.getByRole('list', { name: 'Vitrin sayfaları' });
  const expected = SHOWCASE_PAGES.map((s) => s.yol).filter((y) => y.startsWith('/app/vitrin/'));
  await expect(list.getByRole('link')).toHaveCount(expected.length);
  const hrefs = await list
    .getByRole('link')
    .evaluateAll((a) => a.map((e) => e.getAttribute('href')));
  expect([...hrefs].sort()).toEqual([...expected].sort());

  // Ana sayfadan da ulaşılır.
  await page.goto('/app/');
  await page.getByRole('link', { name: 'Tüm vitrin' }).click();
  await expect(page).toHaveURL(/\/app\/vitrin$/);
});

for (const pageRef of SHOWCASE_PAGES) {
  test(`${pageRef.yol}: açık ve koyu temada axe ciddi/kritik 0, konsol hatası yok`, async ({
    page,
  }) => {
    const errors = collectErrors(page);
    await pageRef.hazirla?.(page);
    await page.emulateMedia({ colorScheme: 'light' });
    await page.goto(pageRef.yol);
    await waitReady(page, pageRef);
    expect(await seriousViolations(page), 'açık tema').toEqual([]);

    await page.emulateMedia({ colorScheme: 'dark' });
    await expect
      .poll(() => page.evaluate(() => getComputedStyle(document.body).backgroundColor))
      .toBe('rgb(20, 19, 16)');
    expect(await seriousViolations(page), 'koyu tema').toEqual([]);
    expect(errors).toEqual([]);
  });
}

test.describe('mobil taşma (dokunmatik öykünme)', () => {
  test.use({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });

  for (const pageRef of SHOWCASE_PAGES) {
    test(`${pageRef.yol}: 320/390/768 px gövde yatay taşması yok`, async ({ page }) => {
      await pageRef.hazirla?.(page);
      for (const width of [320, 390, 768]) {
        await page.setViewportSize({ width: width, height: 844 });
        await page.goto(pageRef.yol);
        await waitReady(page, pageRef);
        expect(await measureOverflow(page), `${width}px`).toEqual({ tasma: 0, suclular: [] });
      }
    });
  }
});

test('1440 px: hiçbir vitrin sayfasında gövde yatay taşması yok', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  for (const pageRef of SHOWCASE_PAGES) {
    await pageRef.hazirla?.(page);
    await page.goto(pageRef.yol);
    await waitReady(page, pageRef);
    expect(await measureOverflow(page), pageRef.yol).toEqual({ tasma: 0, suclular: [] });
  }
});
