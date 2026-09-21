import { expect, test, type Page } from '@playwright/test';

import { ciddiIhlaller, hatalariTopla, oturumAc } from './ortak';

// Ana sayfa oturum ister (oturumGuard): `ben` sahte API'den gelir.
test.beforeEach(async ({ page }) => oturumAc(page));

const zeminRengi = (page: Page) =>
  page.evaluate(() => getComputedStyle(document.body).backgroundColor);

test('yer tutucu sayfa /app/ altında açılır, CSP ihlali yok, axe ciddi/kritik ihlal 0', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page);
  const fontlar: string[] = [];
  page.on('response', (yanit) => {
    if (yanit.url().endsWith('.woff2')) fontlar.push(`${yanit.status()} ${yanit.url()}`);
  });

  const yanit = await page.goto('/app/');
  expect(yanit?.status()).toBe(200);

  await expect(page.locator('html')).toHaveAttribute('lang', 'tr');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Yeni arayüz yapım aşamasında');

  // Self-host Inter: başlıktaki "ş" latin-ext alt kümesini de çeker; ikisi de kendi sunucumuzdan.
  await page.evaluate(() => document.fonts.ready);
  expect(fontlar).toHaveLength(2);
  for (const font of fontlar) expect(font).toMatch(/^200 http:\/\/127\.0\.0\.1:\d+\/app\/media\//);

  expect(await ciddiIhlaller(page)).toEqual([]);

  // CSP (script-src 'self') ihlali ya da çalışma zamanı hatası konsola düşer.
  expect(hatalar).toEqual([]);
});

test('tema: sistem izlenir, açık/koyu seçimi uygulanır ve saklanır; iki temada axe temiz', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page);
  const ACIK_ZEMIN = 'rgb(245, 247, 250)';
  const KOYU_ZEMIN = 'rgb(13, 19, 28)';

  await page.emulateMedia({ colorScheme: 'light' });
  await page.goto('/app/');
  await expect(page.getByRole('button', { name: 'Sistem teması' })).toHaveAttribute(
    'aria-pressed',
    'true',
  );
  expect(await zeminRengi(page)).toBe(ACIK_ZEMIN);

  // Sistem koyu → data-theme yazılmadan koyu token'lar.
  await page.emulateMedia({ colorScheme: 'dark' });
  expect(await page.locator('html').getAttribute('data-theme')).toBeNull();
  expect(await zeminRengi(page)).toBe(KOYU_ZEMIN);
  expect(await ciddiIhlaller(page)).toEqual([]);

  // "Açık" seçilirse sistem koyu olsa da açık kalır.
  await page.getByRole('button', { name: 'Açık tema' }).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  await expect(page.getByRole('button', { name: 'Açık tema' })).toHaveAttribute(
    'aria-pressed',
    'true',
  );
  expect(await zeminRengi(page)).toBe(ACIK_ZEMIN);

  // "Koyu" sistem açık olsa da koyu; yeniden yüklemede korunur.
  await page.emulateMedia({ colorScheme: 'light' });
  await page.getByRole('button', { name: 'Koyu tema' }).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  expect(await zeminRengi(page)).toBe(KOYU_ZEMIN);
  expect(await ciddiIhlaller(page)).toEqual([]);

  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await expect(page.getByRole('button', { name: 'Koyu tema' })).toHaveAttribute(
    'aria-pressed',
    'true',
  );
  expect(await zeminRengi(page)).toBe(KOYU_ZEMIN);

  expect(hatalar).toEqual([]);
});
