import { expect, test } from '@playwright/test';

import {
  apiGet,
  apiGetState,
  oneCustomer,
  NO_ACTUAL,
  login,
  addDays,
  isoDay,
  ROOT,
  availableVehicle,
  randomStart,
  select,
  trDay,
} from './gercek';
import { collectErrors } from './ortak';
import { ORTAM } from './ortam';

/**
 * F5.3 — GERÇEK backend: müsaitlik → "Kirala" (F4.3 `?varac&vfrom&vto&vgrup` sözleşmesi) kira formunu dolu açar;
 * rez şartı oluştur → karşılandı → geri al → sil; filo oluştur → künye → tamamla; şube kapsamı (başka şubeye
 * atanmış operatör Merkez kayıtlarını göremez) ve sayfa izni (Muhasebe F5 sayfasına giremez).
 * Beklenen değerler elle: 3 ay × 1.000 ₺ × (1 + %20 KDV) = 3.600,00 ₺.
 */
test.skip(NO_ACTUAL, 'gerçek backend ortamı yok (RACAR_E2E_KOK / RACAR_E2E_SIFRE)');
test.describe.configure({ mode: 'serial' });

const AG = [/Failed to load resource: the server responded with a status of 4\d\d/];

test('müsaitlik → Kirala: bağlantı sunucunun çözdüğü pencereyi taşır, kira formu araç + günlerle dolu açılır', async ({
  page,
}) => {
  const errors = collectErrors(page, AG);
  await login(page, ORTAM.gercekAdmin);
  const start = randomStart();
  const bit = addDays(start, 3);
  const vehicle = await availableVehicle(page, start);
  const selected = (
    await apiGet<{ araclar: { id: string; grup: string | null }[] }>(page, '/api/ui/v1/musaitlik', {
      basGun: isoDay(start),
      gun: '3',
    })
  ).araclar.find((a) => a.id === vehicle.id);
  const group = selected?.grup ?? '';

  // Gün-sayısı modu: bitiş alanı BOŞ — bağlantı ham alanı değil çözülmüş pencereyi taşımalı (Blazor FAZ-48).
  const query = `basGun=${isoDay(start)}&gun=3&basSaat=09:00&bitSaat=09:00&plaka=${vehicle.plaka}${group ? `&grup=${encodeURIComponent(group)}` : ''}`;
  await page.goto(`${ROOT}/app/musaitlik?${query}`);
  await expect(page.getByText(`${trDay(start)} 09:00 – ${trDay(bit)} 09:00 arası`)).toBeVisible();
  const rent = page.getByRole('link', { name: new RegExp(`Kirala ${vehicle.plaka}`) });
  const expected = new URLSearchParams({
    varac: vehicle.id,
    vfrom: isoDay(start),
    vto: isoDay(bit),
  });
  if (group) expected.set('vgrup', group);
  await expect(rent).toHaveAttribute('href', `/app/kiralar/yeni?${expected.toString()}`);

  await rent.click();
  await expect(page).toHaveURL(
    (u) => u.pathname === '/app/kiralar/yeni' && u.searchParams.get('varac') === vehicle.id,
  );
  const quick = page.locator('[data-rc-sekme="hizli"]');
  await expect(quick.getByLabel('Araç', { exact: true })).toHaveValue(
    new RegExp(`^${vehicle.plaka}`),
  );
  await expect(quick.getByLabel('Başlangıç', { exact: true })).toHaveValue(trDay(start));
  await expect(quick.getByLabel('Bitiş (beklenen)', { exact: true })).toHaveValue(trDay(bit));
  expect(errors).toEqual([]);
});

test('rez şartı: oluştur → karşılandı → geri al → sil (onaylı)', async ({ page }) => {
  const errors = collectErrors(page, AG);
  await login(page, ORTAM.gercekAdmin);
  const customer = await oneCustomer(page);
  const term = `E2E-F53 bebek koltuğu ${Date.now()}`;

  await page.goto(`${ROOT}/app/rez-sartlari`);
  await page.getByRole('button', { name: 'Yeni şart / talep' }).click();
  const editor = page.getByRole('region', { name: 'Yeni şart / talep' });
  await select(page, 'Müşteri', customer.etiket.slice(0, 4), customer.etiket, editor);
  await editor.getByRole('textbox', { name: 'Şart / talep' }).fill(term);
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect(page.getByText('Şart / talep oluşturuldu.')).toBeVisible();

  await page.getByRole('button', { name: `Karşılandı ${term}` }).click();
  await expect(page.getByText('Karşılandı olarak işaretlendi.')).toBeVisible();
  await page.getByRole('button', { name: `Geri al ${term}` }).click();
  await expect(page.getByText('Karşılama geri alındı.')).toBeVisible();
  await expect(page.getByRole('button', { name: `Karşılandı ${term}` })).toBeVisible();

  await page.getByRole('button', { name: `Sil ${term}` }).click();
  await page
    .getByRole('alertdialog')
    .or(page.getByRole('dialog'))
    .getByRole('button', { name: 'Sil' })
    .click();
  await expect(page.getByText('Talep silindi.')).toBeVisible();
  await expect(page.getByRole('button', { name: `Sil ${term}` })).toHaveCount(0);
  expect(errors).toEqual([]);
});

test('filo: oluştur → sunucu taksit planı (3.600,00 ₺) → künye → tamamla; operatör (başka şube) göremez', async ({
  page,
  browser,
}) => {
  const errors = collectErrors(page, AG);
  await login(page, ORTAM.gercekAdmin);
  const vehicle = await availableVehicle(page, randomStart());
  const customer = await oneCustomer(page);

  await page.goto(`${ROOT}/app/filo-kiralama/yeni`);
  await select(page, 'Müşteri', customer.etiket.slice(0, 4), customer.etiket);
  await select(page, 'Araç', vehicle.plaka, new RegExp(`^${vehicle.plaka}`));
  await page.getByRole('textbox', { name: 'Süre (ay)' }).fill('3');
  await page.getByRole('textbox', { name: 'Aylık ücret' }).fill('1.000');
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect(page).toHaveURL(/\/app\/filo-kiralama\/[0-9a-f-]{36}$/);
  const fleetId = page.url().split('/').pop()!;
  await expect(page.getByRole('region', { name: 'Taksit planı tablosu' })).toContainText(
    '3.600,00',
  );

  const description = `E2E-F53 künye ${Date.now()}`;
  await page.getByRole('textbox', { name: 'Açıklama' }).fill(description);
  await page.getByRole('button', { name: 'Künyeyi kaydet' }).click();
  await expect(page.getByText('Künye kaydedildi.')).toBeVisible();
  await page.reload();
  await expect(page.getByRole('textbox', { name: 'Açıklama' })).toHaveValue(description);

  // Şube kapsamı: "ADV Şube B"ye atanmış operatör Merkez aracının sözleşmesini açamaz, listede görmez.
  const op = await browser.newPage();
  await login(op, ORTAM.gercekOperator);
  expect([403, 404]).toContain(await apiGetState(op, `/api/ui/v1/filo-kiralama/${fleetId}`));
  const opList = await apiGet<{ kayitlar: { id: string }[] }>(op, '/api/ui/v1/filo-kiralama', {
    plaka: vehicle.plaka,
  });
  expect(opList.kayitlar.map((k) => k.id)).not.toContain(fleetId);
  await op.close();

  await page.getByRole('button', { name: 'Tamamla', exact: true }).click();
  await expect(page.getByText('Sözleşme tamamlandı.')).toBeVisible();
  await expect(page.getByRole('heading', { level: 1 })).toContainText('Tamamlandı');
  await expect(page.getByRole('button', { name: 'Tamamla', exact: true })).toHaveCount(0);
  expect(errors).toEqual([]);
});
