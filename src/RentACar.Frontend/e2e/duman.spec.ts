import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';

test('yer tutucu sayfa /app/ altında açılır, CSP ihlali yok, axe ciddi/kritik ihlal 0', async ({
  page,
}) => {
  const hatalar: string[] = [];
  page.on('console', (mesaj) => {
    if (mesaj.type() === 'error') hatalar.push(mesaj.text());
  });
  page.on('pageerror', (hata) => hatalar.push(hata.message));

  const yanit = await page.goto('/app/');
  expect(yanit?.status()).toBe(200);

  await expect(page.locator('html')).toHaveAttribute('lang', 'tr');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Yeni arayüz yapım aşamasında');

  const sonuc = await new AxeBuilder({ page }).analyze();
  const ciddi = sonuc.violations
    .filter((ihlal) => ihlal.impact === 'serious' || ihlal.impact === 'critical')
    .map((ihlal) => `${ihlal.id}: ${ihlal.help}`);
  expect(ciddi).toEqual([]);

  // CSP (script-src 'self') ihlali ya da çalışma zamanı hatası konsola düşer.
  expect(hatalar).toEqual([]);
});
