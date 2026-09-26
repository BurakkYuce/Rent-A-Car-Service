import { expect, test, type Page } from '@playwright/test';

import {
  apiGet,
  apiPost,
  oneCustomer,
  NO_ACTUAL,
  login,
  addDays,
  writeDay,
  isoMonth,
  ROOT,
  availableVehicle,
  randomStart,
  select,
  trDay,
} from './gercek';
import { collectErrors } from './ortak';
import { ORTAM } from './ortam';

/**
 * F5.3 — GERÇEK backend uçtan uca (sahte API yok): rezervasyon → onay → takvim → kiraya çevir → kira formu;
 * teklif → gönder → kabul → rezervasyon. Beklenen değerler elle kurulmuş senaryodan: 09:00 → +3 gün 09:00
 * penceresi takvimde 4 güne (başlangıç + 2 ara gün + dönüş sabahı) değer; kira formu aynı aracı ve günleri taşır.
 * Koşum: `RACAR_E2E_KOK` + `RACAR_E2E_SIFRE` (bkz. `ortam.ts`); yoksa atlanır.
 */
test.skip(NO_ACTUAL, 'gerçek backend ortamı yok (RACAR_E2E_KOK / RACAR_E2E_SIFRE)');
test.describe.configure({ mode: 'serial' });

const AG = [/Failed to load resource: the server responded with a status of 4\d\d/];

interface RezOzeti {
  readonly durum: string;
  readonly vehicleId: string;
  readonly musteriId: string;
  readonly gun: number | string;
  readonly kiraId: string | null;
}

async function rezervasyon(page: Page, id: string): Promise<RezOzeti> {
  return (await apiGet<{ rezervasyon: RezOzeti }>(page, `/api/ui/v1/rezervasyonlar/${id}`))
    .rezervasyon;
}

async function calendarCells(
  page: Page,
  month: string,
  plate: string,
  type: 'Rezervasyon' | 'Kira',
) {
  await page.goto(`${ROOT}/app/takvim?ay=${month}&plaka=${encodeURIComponent(plate)}`);
  const grid = page.locator('.izgara-kap');
  await expect(grid).toHaveAttribute('aria-busy', 'false');
  await expect(page.getByRole('link', { name: new RegExp(`^${plate}`) })).toBeVisible();
  return page.getByTitle(`${plate} — ${type}`, { exact: true });
}

test('rezervasyon: oluştur → onayla → takvimde R (4 gün) → kiraya çevir → SPA kira formu aynı araç ve günlerle', async ({
  page,
}) => {
  const errors = collectErrors(page, AG);
  await login(page, ORTAM.gercekAdmin);
  const start = randomStart();
  const bit = addDays(start, 3);
  const vehicle = await availableVehicle(page, start);
  const customer = await oneCustomer(page);
  const project = `E2E-F53-${Date.now()}`;

  await page.goto(`${ROOT}/app/rezervasyonlar/yeni`);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Yeni Rezervasyon');
  await select(page, 'Müşteri', customer.etiket.slice(0, 4), customer.etiket);
  await select(page, 'Araç', vehicle.plaka, new RegExp(`^${vehicle.plaka}`));
  await writeDay(page, 'Başlangıç', start);
  await writeDay(page, 'Bitiş', bit);
  await page.getByLabel('Günlük ücret').fill('1000');
  await page.getByLabel('Proje adı').fill(project);
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();

  await expect(page).toHaveURL(/\/app\/rezervasyonlar\/[0-9a-f-]{36}$/);
  const resId = page.url().split('/').pop()!;
  await expect(page.getByTestId('rez-durum')).toHaveText('Rezerv');
  const detail = await rezervasyon(page, resId);
  expect(detail).toMatchObject({ vehicleId: vehicle.id, musteriId: customer.id });
  expect(Number(detail.gun)).toBe(3); // 09:00 → +3 gün 09:00

  // Liste: Blazor ile aynı süzgeç (ara = proje adı değil; Rez No/müşteri/plaka) — plaka ile bulunur, durum Rezerv.
  await page.goto(`${ROOT}/app/rezervasyonlar?q=${vehicle.plaka}`);
  await expect(page.getByRole('gridcell', { name: project })).toBeVisible();

  await page.goto(`${ROOT}/app/rezervasyonlar/${resId}`);
  await page.getByRole('button', { name: 'Onayla', exact: true }).click();
  await expect(page.getByTestId('rez-durum')).toHaveText('Onaylı');

  // Takvim: sunucu doluluğu — pencere 4 takvim gününe dokunur (bas 09:00 … bit 09:00, yarı açık gün kesişimi).
  const rCells = await calendarCells(page, isoMonth(start), vehicle.plaka, 'Rezervasyon');
  await expect(rCells).toHaveCount(4);
  // Plaka bağlantısı → kira formu ?varac= (F4.3 sözleşmesi) ve form aracı dolu açar.
  const plateLink = page.getByRole('link', { name: new RegExp(`^${vehicle.plaka}`) });
  await expect(plateLink).toHaveAttribute('href', `/app/kiralar/yeni?varac=${vehicle.id}`);
  await plateLink.click();
  await expect(page).toHaveURL(new RegExp(`/app/kiralar/yeni\\?varac=${vehicle.id}$`));
  const quick = page.locator('[data-rc-sekme="hizli"]');
  await expect(quick.getByLabel('Araç', { exact: true })).toHaveValue(
    new RegExp(`^${vehicle.plaka}`),
  );

  // Kiraya çevir (onaylı) → SPA kira formu; kira aynı araç + aynı günler.
  await page.goto(`${ROOT}/app/rezervasyonlar/${resId}`);
  await page.getByRole('button', { name: 'Kiraya çevir', exact: true }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Kiraya çevir' }).click();
  await expect(page).toHaveURL(/\/app\/kiralar\/[0-9a-f-]{36}$/);
  const rentalId = page.url().split('/').pop()!;
  await expect(quick.getByLabel('Araç', { exact: true })).toHaveValue(
    new RegExp(`^${vehicle.plaka}`),
  );
  await expect(quick.getByLabel('Başlangıç', { exact: true })).toHaveValue(trDay(start));
  await expect(quick.getByLabel('Bitiş (beklenen)', { exact: true })).toHaveValue(trDay(bit));

  // Rezervasyon kiraya bağlandı; takvimde artık R yok (kira teslim edilmeden K da yok — Kirada değil).
  const res = await rezervasyon(page, resId);
  expect(res).toMatchObject({ durum: 'KirayaCevrildi', kiraId: rentalId });
  await expect(
    await calendarCells(page, isoMonth(start), vehicle.plaka, 'Rezervasyon'),
  ).toHaveCount(0);

  // Temizlik: kira iptal (silme yok).
  const cancel = await apiPost(page, `/api/ui/v1/kiralar/${rentalId}/iptal`, {});
  expect(cancel.ok(), `kira iptal: ${cancel.status()} ${await cancel.text()}`).toBe(true);
  expect(errors).toEqual([]);
});

test('teklif: oluştur → gönder → kabul → rezervasyon (aynı müşteri/araç/pencere), kabul sonrası eylem yok', async ({
  page,
}) => {
  const errors = collectErrors(page, AG);
  await login(page, ORTAM.gercekAdmin);
  const start = randomStart();
  const bit = addDays(start, 3);
  const vehicle = await availableVehicle(page, start);
  const customer = await oneCustomer(page);

  await page.goto(`${ROOT}/app/teklifler/yeni`);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Yeni Teklif');
  await select(page, 'Müşteri', customer.etiket.slice(0, 4), customer.etiket);
  await select(page, 'Araç', vehicle.plaka, new RegExp(`^${vehicle.plaka}`));
  await writeDay(page, 'Başlangıç', start);
  await writeDay(page, 'Bitiş', bit);
  await page.getByLabel('Günlük ücret').fill('900');
  await page.getByRole('button', { name: 'Teklif oluştur' }).click();
  await expect(page).toHaveURL(/\/app\/teklifler\/[0-9a-f-]{36}$/);
  await expect(page.getByTestId('teklif-durum')).toHaveText('Taslak');

  await page.getByRole('button', { name: 'Gönder', exact: true }).click();
  await expect(page.getByTestId('teklif-durum')).toHaveText('Gönderildi');
  await page.getByRole('button', { name: 'Kabul → Rezervasyon' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Kabul et' }).click();
  await expect(page.getByTestId('teklif-durum')).toHaveText('Kabul');
  await expect(page.getByRole('button', { name: 'Kabul → Rezervasyon' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Reddet' })).toHaveCount(0);

  await page.getByRole('link', { name: 'Rezervasyonu aç' }).click();
  await expect(page).toHaveURL(/\/app\/rezervasyonlar\/[0-9a-f-]{36}$/);
  const resId = page.url().split('/').pop()!;
  const res = await rezervasyon(page, resId);
  expect(res).toMatchObject({ vehicleId: vehicle.id, musteriId: customer.id });
  expect(Number(res.gun)).toBe(3);

  // Temizlik: rezervasyon iptal (OperationsDelete — Admin).
  const cancel = await apiPost(page, `/api/ui/v1/rezervasyonlar/${resId}/iptal`);
  expect(cancel.ok(), `rez iptal: ${cancel.status()} ${await cancel.text()}`).toBe(true);
  expect(errors).toEqual([]);
});
