import { expect, test } from '@playwright/test';

import {
  apiGet,
  apiGetState,
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
} from './gercek';
import { ORTAM } from './ortam';

/**
 * F5.3 — GERÇEK backend: şube kapsamı ve sayfa izni.
 * - Admin Merkez ofisinden rezervasyon açar; "ADV Şube B"ye atanmış operatör onu API'de (detay 403/404,
 *   listede yok), SPA'da (kayıt açılmaz) ve takvimde (Merkez aracı görünmez) göremez.
 * - Muhasebe (OperationsWrite yok) F5 sayfalarına giremez: Blazor `izin:OperationsWrite` paritesi —
 *   uyarı bandı + ana sayfa (API 403'üne düşen boş sayfa değil).
 */
test.skip(NO_ACTUAL, 'gerçek backend ortamı yok (RACAR_E2E_KOK / RACAR_E2E_SIFRE)');
test.describe.configure({ mode: 'serial' });

test('şube kapsamı: başka şubeye atanmış operatör Merkez rezervasyonunu ve aracını göremez', async ({
  page,
  browser,
}) => {
  await login(page, ORTAM.gercekAdmin);
  const start = randomStart();
  const vehicle = await availableVehicle(page, start);
  const customer = await oneCustomer(page);

  await page.goto(`${ROOT}/app/rezervasyonlar/yeni`);
  await select(page, 'Müşteri', customer.etiket.slice(0, 4), customer.etiket);
  await select(page, 'Araç', vehicle.plaka, new RegExp(`^${vehicle.plaka}`));
  await writeDay(page, 'Başlangıç', start);
  await writeDay(page, 'Bitiş', addDays(start, 3));
  await page.getByLabel('Günlük ücret').fill('1000');
  await select(page, 'Çıkış ofisi', 'İstanbul Merkez', 'İstanbul Merkez Ofis');
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect(page).toHaveURL(/\/app\/rezervasyonlar\/[0-9a-f-]{36}$/);
  const resId = page.url().split('/').pop()!;

  const op = await browser.newPage();
  const ben = await login(op, ORTAM.gercekOperator);
  expect(ben).toMatchObject({ subeKapsami: { tumSubeler: false, subeAd: 'ADV Şube B' } });
  expect([403, 404]).toContain(await apiGetState(op, `/api/ui/v1/rezervasyonlar/${resId}`));
  const list = await apiGet<{ kayitlar: { id: string }[] }>(op, '/api/ui/v1/rezervasyonlar', {
    q: vehicle.plaka,
  });
  expect(list.kayitlar.map((k) => k.id)).not.toContain(resId);
  // Yazma da kapsamdan geçer: onaylama/kiraya çevirme reddedilir, durum değişmez.
  expect((await apiPost(op, `/api/ui/v1/rezervasyonlar/${resId}/onayla`)).ok()).toBe(false);

  await op.goto(`${ROOT}/app/rezervasyonlar/${resId}`);
  await expect(op.getByRole('button', { name: 'Onayla', exact: true })).toHaveCount(0);
  await expect(op.getByRole('button', { name: 'Kaydet', exact: true })).toHaveCount(0);

  await op.goto(`${ROOT}/app/takvim?ay=${isoMonth(start)}&plaka=${vehicle.plaka}`);
  await expect(op.getByText('Araç: 0')).toBeVisible();
  await op.close();

  const res = await apiGet<{ rezervasyon: { durum: string } }>(
    page,
    `/api/ui/v1/rezervasyonlar/${resId}`,
  );
  expect(res.rezervasyon.durum).toBe('Rezerv');
  const cancel = await apiPost(page, `/api/ui/v1/rezervasyonlar/${resId}/iptal`);
  expect(cancel.ok(), `rez iptal: ${cancel.status()}`).toBe(true);
});

for (const path of [
  '/app/takvim',
  '/app/musaitlik',
  '/app/rez-sartlari',
  '/app/filo-kiralama',
  '/app/teklifler',
]) {
  test(`sayfa izni: Muhasebe ${path} açamaz (uyarı bandı + ana sayfa)`, async ({ page }) => {
    await login(page, ORTAM.gercekMuhasebe);
    await page.goto(`${ROOT}${path}`);
    await expect(page.getByText('Bu sayfayı görüntüleme yetkiniz yok.')).toBeVisible();
    await expect(page).toHaveURL(`${ROOT}/app/`);
  });
}
