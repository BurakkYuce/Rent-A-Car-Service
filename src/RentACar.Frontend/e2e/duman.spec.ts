import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';

/** Konsol hatalarını (CSP ihlali dahil) ve çalışma zamanı hatalarını toplar. */
function hatalariTopla(page: Page): string[] {
  const hatalar: string[] = [];
  page.on('console', (mesaj) => {
    if (mesaj.type() === 'error') hatalar.push(mesaj.text());
  });
  page.on('pageerror', (hata) => hatalar.push(hata.message));
  return hatalar;
}

async function ciddiIhlaller(page: Page): Promise<string[]> {
  const sonuc = await new AxeBuilder({ page }).analyze();
  return sonuc.violations
    .filter((ihlal) => ihlal.impact === 'serious' || ihlal.impact === 'critical')
    .map(
      (ihlal) =>
        `${ihlal.id}: ${ihlal.help} → ` +
        ihlal.nodes.map((n) => `${n.target.join(' ')} (${n.any[0]?.message ?? ''})`).join('; '),
    );
}

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
  await expect(page.getByRole('button', { name: 'Sistem' })).toHaveAttribute(
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
  await page.getByRole('button', { name: 'Açık' }).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  await expect(page.getByRole('button', { name: 'Açık' })).toHaveAttribute('aria-pressed', 'true');
  expect(await zeminRengi(page)).toBe(ACIK_ZEMIN);

  // "Koyu" sistem açık olsa da koyu; yeniden yüklemede korunur.
  await page.emulateMedia({ colorScheme: 'light' });
  await page.getByRole('button', { name: 'Koyu' }).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  expect(await zeminRengi(page)).toBe(KOYU_ZEMIN);
  expect(await ciddiIhlaller(page)).toEqual([]);

  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await expect(page.getByRole('button', { name: 'Koyu' })).toHaveAttribute('aria-pressed', 'true');
  expect(await zeminRengi(page)).toBe(KOYU_ZEMIN);

  expect(hatalar).toEqual([]);
});
