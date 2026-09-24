import { expect, test, type Page } from '@playwright/test';

import { apiGet, apiGetDurum, apiPost, GERCEK_YOK, gir, KOK } from './gercek';
import { ORTAM } from './ortam';

/**
 * F6.3 — GERÇEK backend: araç kartı yaşam döngüsü ve şube kapsamı.
 * Araç SPA formundan açılır (Merkez), kart düzenlenir (surum; bayat sürüm 409 `cakisma`), iki fotoğraf yüklenip
 * sıralanır, durum panosunda görünür ve "Tahsis" BAF formunu araç + KM + şube dolu açar. "ADV Şube B"
 * operatörü aracı hiçbir yoldan göremez/değiştiremez; Muhasebe okur ama yazamaz. Koşum sonunda fotoğraflar ve
 * araç silinir (mali kayıt değil; kira/BAF bağı kurulmadı).
 */
test.skip(GERCEK_YOK, 'gerçek backend ortamı yok (RACAR_E2E_KOK / RACAR_E2E_SIFRE)');
test.describe.configure({ mode: 'serial' });

/**
 * e2e tsconfig Node tiplerini içermez (`ortam.ts` deseni); yalnız gereken imza bildirilir. Playwright yükü
 * Node `Buffer`'ı olarak ister (base64'e `toString` ile çevirir — Uint8Array yanlış kodlanır).
 */
declare const Buffer: { from(data: string, encoding: 'base64'): never };

const ARACLAR = '/api/ui/v1/araclar';
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
let aracId = '';

async function apiSend(page: Page, method: 'put' | 'delete', yol: string, govde?: unknown) {
  const c = (await page.context().cookies(KOK)).find((x) => x.name === 'XSRF-TOKEN');
  return page.context().request[method](`${KOK}${yol}`, {
    headers: {
      'X-XSRF-TOKEN': c ? decodeURIComponent(c.value) : '',
      'Idempotency-Key': crypto.randomUUID(),
    },
    ...(govde === undefined ? {} : { data: govde }),
  });
}

test('araç: oluştur → düzenle (surum, bayat 409) → foto yükle + sırala → durum panosu → Tahsis formu dolu', async ({
  page,
}) => {
  await gir(page, ORTAM.gercekAdmin);

  // Oluştur (SPA formu): plaka + şube.
  await page.goto(`${KOK}/app/araclar/yeni`);
  await page.getByRole('textbox', { name: 'Plaka' }).fill(plaka);
  await page.getByRole('combobox', { name: 'Şube' }).fill('Merkez');
  await page.getByRole('button', { name: 'Oluştur' }).click();
  await expect(page).toHaveURL(/\/app\/araclar\/[0-9a-f-]{36}$/);
  await expect(page.getByText(`${plaka} plakalı araç oluşturuldu.`)).toBeVisible();
  aracId = page.url().split('/').pop()!;
  const ilk = await apiGet<Card>(page, `${ARACLAR}/${aracId}`);
  expect(ilk).toMatchObject({ plaka, sube: 'Merkez', marka: null });

  // Düzenle: marka + KM → kayıt; sürüm ilerler.
  await page.getByRole('combobox', { name: 'Marka' }).fill('E2E Marka');
  await page.getByRole('textbox', { name: 'KM', exact: true }).fill('12500');
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect(page.getByText(`${plaka} plakalı araç kaydedildi.`)).toBeVisible();
  const ikinci = await apiGet<Card>(page, `${ARACLAR}/${aracId}`);
  expect(ikinci).toMatchObject({ marka: 'E2E Marka', sube: 'Merkez' });
  expect(Number(ikinci.km)).toBe(12500);
  expect(ikinci.surum).not.toBe(ilk.surum);

  // Bayat sürümle tam değiştirme reddedilir, kayıt değişmez.
  const bayat = await apiSend(page, 'put', `${ARACLAR}/${aracId}`, {
    ...ikinci,
    marka: 'Bayat Yazim',
    surum: ilk.surum,
  });
  expect(bayat.status()).toBe(409);
  expect(((await bayat.json()) as { kod?: string }).kod).toBe('cakisma');
  expect((await apiGet<Card>(page, `${ARACLAR}/${aracId}`)).marka).toBe('E2E Marka');

  // Fotoğraflar: iki yükleme (ilki kapak) → ikinciyi yukarı taşı → sunucu sırası değişir.
  await page.goto(`${KOK}/app/araclar/${aracId}#sekme=fotograflar`);
  const dosya = page.getByLabel('Fotoğraf seç (PNG/JPEG/WebP, ≤ 2 MB)');
  for (const [ad, png] of [
    ['a.png', PNG_A],
    ['b.png', PNG_B],
  ] as const) {
    await dosya.setInputFiles({
      name: ad,
      mimeType: 'image/png',
      buffer: Buffer.from(png, 'base64'),
    });
    await page.getByRole('button', { name: 'Yükle', exact: true }).click();
    await expect(page.getByText('Fotoğraf yüklendi.').last()).toBeVisible();
  }
  await expect(page.getByText('2/20 fotoğraf')).toBeVisible();
  const once = await apiGet<Photo[]>(page, `${ARACLAR}/${aracId}/fotograflar`);
  expect(once).toHaveLength(2);
  await page.getByRole('button', { name: '2. fotoğrafı yukarı taşı' }).click();
  await expect
    .poll(async () =>
      (await apiGet<Photo[]>(page, `${ARACLAR}/${aracId}/fotograflar`)).map((p) => p.id),
    )
    .toEqual([once[1]!.id, once[0]!.id]);

  // Durum panosu: araç görünür, "Tahsis" BAF formunu araç + KM + şube dolu açar (F6.3 parite).
  await page.goto(`${KOK}/app/arac-durum?q=${plaka}`);
  const satir = page.getByRole('row').filter({ hasText: plaka });
  await expect(satir).toHaveCount(1);
  await satir.getByRole('link', { name: 'Tahsis' }).click();
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
  expect(aracId, 'önceki test aracı oluşturmalı').not.toBe('');
  await gir(page, ORTAM.gercekOperator);
  expect(await apiGetDurum(page, `${ARACLAR}/${aracId}`)).toBe(403);
  expect(await apiGetDurum(page, `${ARACLAR}/${aracId}/fotograflar`)).toBe(403);
  const liste = await apiGet<{ kayitlar: { id: string }[] }>(page, ARACLAR, { q: plaka });
  expect(liste.kayitlar.map((k) => k.id)).not.toContain(aracId);
  const pano = await apiGet<{ liste: { kayitlar: { vehicleId: string }[] } }>(
    page,
    `${ARACLAR}/durum`,
    { q: plaka },
  );
  expect(pano.liste.kayitlar.map((k) => k.vehicleId)).not.toContain(aracId);
  expect((await apiPost(page, `${ARACLAR}/${aracId}/km`, { km: 99999 })).status()).toBe(403);
  const yeni = await apiPost(page, ARACLAR, {
    plaka: `${plaka}X`,
    sube: 'Merkez',
    durum: 'Musait',
    km: 0,
  });
  expect(yeni.ok(), `başka şubeye araç açılmamalı: ${yeni.status()}`).toBe(false);
  // Silme OperationsDelete ister (operatörde yok).
  expect((await apiSend(page, 'delete', `${ARACLAR}/${aracId}`)).status()).toBe(403);

  await page.goto(`${KOK}/app/araclar/${aracId}`);
  await expect(page.getByRole('button', { name: 'Kaydet', exact: true })).toHaveCount(0);
});

test('izin: Muhasebe aracı okur (ViewReports) ama yazamaz; durum panosu ve tanımlar kapalı', async ({
  page,
}) => {
  await gir(page, ORTAM.gercekMuhasebe);
  const kart = await apiGet<Card>(page, `${ARACLAR}/${aracId}`);
  expect(kart.plaka).toBe(plaka);
  expect((await apiSend(page, 'put', `${ARACLAR}/${aracId}`, kart)).status()).toBe(403);
  expect(await apiGetDurum(page, `${ARACLAR}/durum`)).toBe(403);
  expect(await apiGetDurum(page, `${ARACLAR}/detayli`)).toBe(200);

  await page.goto(`${KOK}/app/araclar/${aracId}`);
  await expect(page.getByText('Araç kartını yalnız görüntüleme yetkiniz var.')).toBeVisible();
  for (const yol of ['/app/arac-durum', '/app/arac-sahipleri', '/app/araclar/yeni']) {
    await page.goto(`${KOK}${yol}`);
    await expect(page.getByText('Bu sayfayı görüntüleme yetkiniz yok.')).toBeVisible();
    await expect(page).toHaveURL(`${KOK}/app/`);
  }
});

test('izin: Operatör detaylı listeyi (ViewReports) açamaz', async ({ page }) => {
  await gir(page, ORTAM.gercekOperator);
  expect(await apiGetDurum(page, `${ARACLAR}/detayli`)).toBe(403);
  await page.goto(`${KOK}/app/araclar/detayli`);
  await expect(page.getByText('Bu sayfayı görüntüleme yetkiniz yok.')).toBeVisible();
});

test.afterAll(async ({ browser }) => {
  if (!aracId) return;
  const page = await browser.newPage();
  await gir(page, ORTAM.gercekAdmin);
  for (const p of await apiGet<Photo[]>(page, `${ARACLAR}/${aracId}/fotograflar`)) {
    expect((await apiSend(page, 'delete', `${ARACLAR}/${aracId}/fotograflar/${p.id}`)).ok()).toBe(
      true,
    );
  }
  const sil = await apiSend(page, 'delete', `${ARACLAR}/${aracId}`);
  expect(sil.ok(), `araç silme: ${sil.status()}`).toBe(true);
  expect(await apiGetDurum(page, `${ARACLAR}/${aracId}`)).toBe(404);
  await page.close();
});
