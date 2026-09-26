import { expect, test, type Page } from '@playwright/test';

import { apiGet, apiGetState, apiPost, NO_ACTUAL, login, ROOT } from './gercek';
import { ORTAM } from './ortam';

/**
 * F6.3 — GERÇEK backend: araç kartı yaşam döngüsü ve şube kapsamı.
 * Araç SPA formundan açılır (Merkez), kart düzenlenir (surum; bayat sürüm 409 `cakisma`), iki fotoğraf yüklenip
 * sıralanır, durum panosunda görünür ve "Tahsis" BAF formunu araç + KM + şube dolu açar. "ADV Şube B"
 * operatörü aracı hiçbir yoldan göremez/değiştiremez; Muhasebe okur ama yazamaz. Koşum sonunda fotoğraflar ve
 * araç silinir (mali kayıt değil; kira/BAF bağı kurulmadı).
 */
test.skip(NO_ACTUAL, 'gerçek backend ortamı yok (RACAR_E2E_KOK / RACAR_E2E_SIFRE)');
test.describe.configure({ mode: 'serial' });

/**
 * e2e tsconfig Node tiplerini içermez (`ortam.ts` deseni); yalnız gereken imza bildirilir. Playwright yükü
 * Node `Buffer`'ı olarak ister (base64'e `toString` ile çevirir — Uint8Array yanlış kodlanır).
 */
declare const Buffer: { from(data: string, encoding: 'base64'): never };

const VEHICLES = '/api/ui/v1/araclar';
/** Geçerli 1×1 PNG'ler (sunucu türü içerikten okur, küçük resim üretir). */
const PNG_A =
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==';
const PNG_B =
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==';

interface Card {
  readonly id: string;
  readonly plaka: string;
  readonly marka: string | null;
  readonly sube: string | null;
  readonly km: number | string;
  readonly surum: string;
}
interface Photo {
  readonly id: string;
}

const plaka = `99EF${String(1000 + Math.floor(Math.random() * 9000))}`;
let vehicleIdValue = '';

async function apiSend(page: Page, method: 'put' | 'delete', path: string, body?: unknown) {
  const c = (await page.context().cookies(ROOT)).find((x) => x.name === 'XSRF-TOKEN');
  return page.context().request[method](`${ROOT}${path}`, {
    headers: {
      'X-XSRF-TOKEN': c ? decodeURIComponent(c.value) : '',
      'Idempotency-Key': crypto.randomUUID(),
    },
    ...(body === undefined ? {} : { data: body }),
  });
}

test('araç: oluştur → düzenle (surum, bayat 409) → foto yükle + sırala → durum panosu → Tahsis formu dolu', async ({
  page,
}) => {
  await login(page, ORTAM.gercekAdmin);

  // Oluştur (SPA formu): plaka + şube.
  await page.goto(`${ROOT}/app/araclar/yeni`);
  await page.getByRole('textbox', { name: 'Plaka' }).fill(plaka);
  await page.getByRole('combobox', { name: 'Şube' }).fill('Merkez');
  await page.getByRole('button', { name: 'Oluştur' }).click();
  await expect(page).toHaveURL(/\/app\/araclar\/[0-9a-f-]{36}$/);
  await expect(page.getByText(`${plaka} plakalı araç oluşturuldu.`)).toBeVisible();
  vehicleIdValue = page.url().split('/').pop()!;
  const first = await apiGet<Card>(page, `${VEHICLES}/${vehicleIdValue}`);
  expect(first).toMatchObject({ plaka, sube: 'Merkez', marka: null });

  // Düzenle: marka + KM → kayıt; sürüm ilerler.
  await page.getByRole('combobox', { name: 'Marka' }).fill('E2E Marka');
  await page.getByRole('textbox', { name: 'KM', exact: true }).fill('12500');
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect(page.getByText(`${plaka} plakalı araç kaydedildi.`)).toBeVisible();
  const second = await apiGet<Card>(page, `${VEHICLES}/${vehicleIdValue}`);
  expect(second).toMatchObject({ marka: 'E2E Marka', sube: 'Merkez' });
  expect(Number(second.km)).toBe(12500);
  expect(second.surum).not.toBe(first.surum);

  // Bayat sürümle tam değiştirme reddedilir, kayıt değişmez.
  const stale = await apiSend(page, 'put', `${VEHICLES}/${vehicleIdValue}`, {
    ...second,
    marka: 'Bayat Yazim',
    surum: first.surum,
  });
  expect(stale.status()).toBe(409);
  expect(((await stale.json()) as { kod?: string }).kod).toBe('cakisma');
  expect((await apiGet<Card>(page, `${VEHICLES}/${vehicleIdValue}`)).marka).toBe('E2E Marka');

  // Fotoğraflar: iki yükleme (ilki kapak) → ikinciyi yukarı taşı → sunucu sırası değişir.
  await page.goto(`${ROOT}/app/araclar/${vehicleIdValue}#sekme=fotograflar`);
  const file = page.getByLabel('Fotoğraf seç (PNG/JPEG/WebP, ≤ 2 MB)');
  for (const [name, png] of [
    ['a.png', PNG_A],
    ['b.png', PNG_B],
  ] as const) {
    await file.setInputFiles({
      name: name,
      mimeType: 'image/png',
      buffer: Buffer.from(png, 'base64'),
    });
    await page.getByRole('button', { name: 'Yükle', exact: true }).click();
    await expect(page.getByText('Fotoğraf yüklendi.').last()).toBeVisible();
  }
  await expect(page.getByText('2/20 fotoğraf')).toBeVisible();
  const once = await apiGet<Photo[]>(page, `${VEHICLES}/${vehicleIdValue}/fotograflar`);
  expect(once).toHaveLength(2);
  await page.getByRole('button', { name: '2. fotoğrafı yukarı taşı' }).click();
  await expect
    .poll(async () =>
      (await apiGet<Photo[]>(page, `${VEHICLES}/${vehicleIdValue}/fotograflar`)).map((p) => p.id),
    )
    .toEqual([once[1]!.id, once[0]!.id]);

  // Durum panosu: araç görünür, "Tahsis" BAF formunu araç + KM + şube dolu açar (F6.3 parite).
  await page.goto(`${ROOT}/app/arac-durum?q=${plaka}`);
  const row = page.getByRole('row').filter({ hasText: plaka });
  await expect(row).toHaveCount(1);
  await row.getByRole('link', { name: 'Tahsis' }).click();
  await expect(page).toHaveURL(/\/app\/baf(\?|$)/);
  await expect(page.getByRole('combobox', { name: 'Araç', exact: true })).toHaveValue(
    new RegExp(`^${plaka}`),
  );
  await expect(page.getByRole('textbox', { name: 'Çıkış KM' })).toHaveValue(/^12[.,]?500$/);
  await expect(page.getByRole('combobox', { name: 'Şube (çıkış)' })).toHaveValue('Merkez');
});

test('şube kapsamı: "ADV Şube B" operatörü Merkez aracını göremez, değiştiremez, Merkez\'e araç açamaz', async ({
  page,
}) => {
  expect(vehicleIdValue, 'önceki test aracı oluşturmalı').not.toBe('');
  await login(page, ORTAM.gercekOperator);
  expect(await apiGetState(page, `${VEHICLES}/${vehicleIdValue}`)).toBe(403);
  expect(await apiGetState(page, `${VEHICLES}/${vehicleIdValue}/fotograflar`)).toBe(403);
  const list = await apiGet<{ kayitlar: { id: string }[] }>(page, VEHICLES, { q: plaka });
  expect(list.kayitlar.map((k) => k.id)).not.toContain(vehicleIdValue);
  const dashboard = await apiGet<{ liste: { kayitlar: { vehicleId: string }[] } }>(
    page,
    `${VEHICLES}/durum`,
    { q: plaka },
  );
  expect(dashboard.liste.kayitlar.map((k) => k.vehicleId)).not.toContain(vehicleIdValue);
  expect((await apiPost(page, `${VEHICLES}/${vehicleIdValue}/km`, { km: 99999 })).status()).toBe(
    403,
  );
  const newItem = await apiPost(page, VEHICLES, {
    plaka: `${plaka}X`,
    sube: 'Merkez',
    durum: 'Musait',
    km: 0,
  });
  expect(newItem.ok(), `başka şubeye araç açılmamalı: ${newItem.status()}`).toBe(false);
  // Silme OperationsDelete ister (operatörde yok).
  expect((await apiSend(page, 'delete', `${VEHICLES}/${vehicleIdValue}`)).status()).toBe(403);

  await page.goto(`${ROOT}/app/araclar/${vehicleIdValue}`);
  await expect(page.getByRole('button', { name: 'Kaydet', exact: true })).toHaveCount(0);
});

test('izin: Muhasebe aracı okur (ViewReports) ama yazamaz; durum panosu ve tanımlar kapalı', async ({
  page,
}) => {
  await login(page, ORTAM.gercekMuhasebe);
  const card = await apiGet<Card>(page, `${VEHICLES}/${vehicleIdValue}`);
  expect(card.plaka).toBe(plaka);
  expect((await apiSend(page, 'put', `${VEHICLES}/${vehicleIdValue}`, card)).status()).toBe(403);
  expect(await apiGetState(page, `${VEHICLES}/durum`)).toBe(403);
  expect(await apiGetState(page, `${VEHICLES}/detayli`)).toBe(200);

  await page.goto(`${ROOT}/app/araclar/${vehicleIdValue}`);
  await expect(page.getByText('Araç kartını yalnız görüntüleme yetkiniz var.')).toBeVisible();
  for (const path of ['/app/arac-durum', '/app/arac-sahipleri', '/app/araclar/yeni']) {
    await page.goto(`${ROOT}${path}`);
    await expect(page.getByText('Bu sayfayı görüntüleme yetkiniz yok.')).toBeVisible();
    await expect(page).toHaveURL(`${ROOT}/app/`);
  }
});

test('izin: Operatör detaylı listeyi (ViewReports) açamaz', async ({ page }) => {
  await login(page, ORTAM.gercekOperator);
  expect(await apiGetState(page, `${VEHICLES}/detayli`)).toBe(403);
  await page.goto(`${ROOT}/app/araclar/detayli`);
  await expect(page.getByText('Bu sayfayı görüntüleme yetkiniz yok.')).toBeVisible();
});

test.afterAll(async ({ browser }) => {
  if (!vehicleIdValue) return;
  const page = await browser.newPage();
  await login(page, ORTAM.gercekAdmin);
  for (const p of await apiGet<Photo[]>(page, `${VEHICLES}/${vehicleIdValue}/fotograflar`)) {
    expect(
      (await apiSend(page, 'delete', `${VEHICLES}/${vehicleIdValue}/fotograflar/${p.id}`)).ok(),
    ).toBe(true);
  }
  const remove = await apiSend(page, 'delete', `${VEHICLES}/${vehicleIdValue}`);
  expect(remove.ok(), `araç silme: ${remove.status()}`).toBe(true);
  expect(await apiGetState(page, `${VEHICLES}/${vehicleIdValue}`)).toBe(404);
  await page.close();
});
