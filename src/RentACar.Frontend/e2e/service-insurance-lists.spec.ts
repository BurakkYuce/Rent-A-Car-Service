import { expect, test } from '@playwright/test';

import { BEN, ciddiIhlaller, hatalariTopla, oturumAc } from './ortak';
import { POLICY_1, serviceInsuranceEndpoints } from './service-insurance-fakes';

/**
 * #301 eksik uçların SPA bağları: servis listesinde durum sekmesi sayaçları (`/servisler/sayaclar`) ve satır KDV / genel
 * toplam / fatura no; tüm poliçelerin zeyilleri (`/app/regulasyon/zeyiller`). Değerler elle kurulmuş sahtelerden.
 */
const AG_HATASI = [/Failed to load resource: the server responded with a status of \d\d\d/];

test.beforeEach(async ({ page }) => {
  await oturumAc(page, BEN);
});

test('servis listesi: sekme sayaçları sunucudan (durum süzgeci sayaca gitmez), satırda KDV / genel toplam / fatura no', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  const countUrls: string[] = [];
  page.on('request', (r) => {
    if (r.url().includes('/api/ui/v1/servisler/sayaclar')) countUrls.push(r.url());
  });
  await serviceInsuranceEndpoints(page);
  await page.goto('/app/servisler?durum=Serviste&tip=Periyodik');
  const tabs = page.getByRole('group', { name: 'Durum' });
  await expect(tabs.getByRole('button', { name: 'Tümü (3)' })).toBeVisible();
  await expect(tabs.getByRole('button', { name: 'Açık (2)' })).toBeVisible();
  await expect(tabs.getByRole('button', { name: 'Serviste (1)' })).toHaveAttribute(
    'aria-pressed',
    'true',
  );
  await expect(page.getByRole('gridcell', { name: 'F-77' })).toBeVisible();
  await expect(page.getByRole('gridcell', { name: '240,00 ₺' })).toBeVisible();
  const url = new URL(countUrls.at(-1) ?? 'http://x');
  expect(url.searchParams.get('tip')).toBe('Periyodik');
  expect(url.searchParams.has('durum')).toBe(false);
  expect(hatalar).toEqual([]);
});

test('zeyiller: tüm poliçelerin zeyilleri tek listede, poliçe bağlantısı + süzgeç; axe temiz', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  const listUrls: string[] = [];
  page.on('request', (r) => {
    if (r.url().includes('/api/ui/v1/regulasyon/zeyiller')) listUrls.push(r.url());
  });
  await serviceInsuranceEndpoints(page);
  await page.goto('/app/regulasyon');
  await page.getByRole('link', { name: 'Zeyiller' }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Zeyiller');
  await expect(page.getByRole('gridcell', { name: 'Z-7' })).toBeVisible();
  const link = page.getByRole('link', { name: '34ABC123 — P-100' });
  await expect(link).toHaveAttribute('href', `/app/regulasyon/sigortalar/${POLICY_1}`);
  await page.getByRole('textbox', { name: 'Tipi' }).fill('Zam');
  await page.getByRole('button', { name: 'Filtrele' }).click();
  await expect
    .poll(() => new URL(listUrls.at(-1) ?? 'http://x').searchParams.get('tipi'))
    .toBe('Zam');
  expect(await ciddiIhlaller(page)).toEqual([]);
  expect(hatalar).toEqual([]);
});
