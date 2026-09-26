import { expect, test, type Page } from '@playwright/test';

import { seriousViolations, collectErrors, logIn } from './ortak';

// Ana sayfa oturum ister (oturumGuard): `ben` sahte API'den gelir.
test.beforeEach(async ({ page }) => logIn(page));

const backgroundColor = (page: Page) =>
  page.evaluate(() => getComputedStyle(document.body).backgroundColor);

test('yer tutucu sayfa /app/ altında açılır, CSP ihlali yok, axe ciddi/kritik ihlal 0', async ({
  page,
}) => {
  const errors = collectErrors(page);
  const fonts: string[] = [];
  page.on('response', (yanit) => {
    if (yanit.url().endsWith('.woff2')) fonts.push(`${yanit.status()} ${yanit.url()}`);
  });

  const response = await page.goto('/app/');
  expect(response?.status()).toBe(200);

  await expect(page.locator('html')).toHaveAttribute('lang', 'tr');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Yeni arayüz yapım aşamasında');

  // Self-host IBM Plex (ağırlık başına ayrı dosya: sayfadaki ağırlık × alt küme kadar istek). Başlıktaki
  // "ş" latin-ext alt kümesini de çeker; hepsi kendi sunucumuzdan.
  await page.evaluate(() => document.fonts.ready);
  expect(fonts.length).toBeGreaterThanOrEqual(2);
  expect(fonts.some((f) => /ibm-plex-sans-latin-ext-/.test(f))).toBe(true);
  for (const font of fonts) expect(font).toMatch(/^200 http:\/\/127\.0\.0\.1:\d+\/app\/media\//);

  expect(await seriousViolations(page)).toEqual([]);

  // CSP (script-src 'self') ihlali ya da çalışma zamanı hatası konsola düşer.
  expect(errors).toEqual([]);
});

test('tema: sistem izlenir, açık/koyu seçimi uygulanır ve saklanır; iki temada axe temiz', async ({
  page,
}) => {
  const errors = collectErrors(page);
  const LIGHT_BACKGROUND = 'rgb(244, 242, 236)';
  const DARK_BACKGROUND = 'rgb(20, 19, 16)';

  await page.emulateMedia({ colorScheme: 'light' });
  await page.goto('/app/');
  await expect(page.getByRole('button', { name: 'Sistem teması' })).toHaveAttribute(
    'aria-pressed',
    'true',
  );
  expect(await backgroundColor(page)).toBe(LIGHT_BACKGROUND);

  // Sistem koyu → data-theme yazılmadan koyu token'lar.
  await page.emulateMedia({ colorScheme: 'dark' });
  expect(await page.locator('html').getAttribute('data-theme')).toBeNull();
  expect(await backgroundColor(page)).toBe(DARK_BACKGROUND);
  expect(await seriousViolations(page)).toEqual([]);

  // "Açık" seçilirse sistem koyu olsa da açık kalır.
  await page.getByRole('button', { name: 'Açık tema' }).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  await expect(page.getByRole('button', { name: 'Açık tema' })).toHaveAttribute(
    'aria-pressed',
    'true',
  );
  expect(await backgroundColor(page)).toBe(LIGHT_BACKGROUND);

  // "Koyu" sistem açık olsa da koyu; yeniden yüklemede korunur.
  await page.emulateMedia({ colorScheme: 'light' });
  await page.getByRole('button', { name: 'Koyu tema' }).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  expect(await backgroundColor(page)).toBe(DARK_BACKGROUND);
  expect(await seriousViolations(page)).toEqual([]);

  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await expect(page.getByRole('button', { name: 'Koyu tema' })).toHaveAttribute(
    'aria-pressed',
    'true',
  );
  expect(await backgroundColor(page)).toBe(DARK_BACKGROUND);

  expect(errors).toEqual([]);
});
