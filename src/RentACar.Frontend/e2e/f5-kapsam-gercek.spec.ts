import { expect, test } from '@playwright/test';

import {
  apiGet,
  apiGetDurum,
  apiPost,
  birMusteri,
  GERCEK_YOK,
  gir,
  gunEkle,
  gunYaz,
  isoAy,
  KOK,
  musaitArac,
  rastgeleBaslangic,
  sec,
} from './gercek';
import { ORTAM } from './ortam';

/**
 * F5.3 — GERÇEK backend: şube kapsamı ve sayfa izni.
 * - Admin Merkez ofisinden rezervasyon açar; "ADV Şube B"ye atanmış operatör onu API'de (detay 403/404,
 *   listede yok), SPA'da (kayıt açılmaz) ve takvimde (Merkez aracı görünmez) göremez.
 * - Muhasebe (OperationsWrite yok) F5 sayfalarına giremez: Blazor `izin:OperationsWrite` paritesi —
 *   uyarı bandı + ana sayfa (API 403'üne düşen boş sayfa değil).
 */
test.skip(GERCEK_YOK, 'gerçek backend ortamı yok (RACAR_E2E_KOK / RACAR_E2E_SIFRE)');
test.describe.configure({ mode: 'serial' });

test('şube kapsamı: başka şubeye atanmış operatör Merkez rezervasyonunu ve aracını göremez', async ({
  page,
  browser,
}) => {
  await gir(page, ORTAM.gercekAdmin);
  const bas = rastgeleBaslangic();
  const arac = await musaitArac(page, bas);
  const musteri = await birMusteri(page);

  await page.goto(`${KOK}/app/rezervasyonlar/yeni`);
  await sec(page, 'Müşteri', musteri.etiket.slice(0, 4), musteri.etiket);
  await sec(page, 'Araç', arac.plaka, new RegExp(`^${arac.plaka}`));
  await gunYaz(page, 'Başlangıç', bas);
  await gunYaz(page, 'Bitiş', gunEkle(bas, 3));
  await page.getByLabel('Günlük ücret').fill('1000');
  await sec(page, 'Çıkış ofisi', 'İstanbul Merkez', 'İstanbul Merkez Ofis');
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect(page).toHaveURL(/\/app\/rezervasyonlar\/[0-9a-f-]{36}$/);
  const rezId = page.url().split('/').pop()!;

  const op = await browser.newPage();
  const ben = await gir(op, ORTAM.gercekOperator);
  expect(ben).toMatchObject({ subeKapsami: { tumSubeler: false, subeAd: 'ADV Şube B' } });
  expect([403, 404]).toContain(await apiGetDurum(op, `/api/ui/v1/rezervasyonlar/${rezId}`));
  const liste = await apiGet<{ kayitlar: { id: string }[] }>(op, '/api/ui/v1/rezervasyonlar', {
    q: arac.plaka,
  });
  expect(liste.kayitlar.map((k) => k.id)).not.toContain(rezId);
  // Yazma da kapsamdan geçer: onaylama/kiraya çevirme reddedilir, durum değişmez.
  expect((await apiPost(op, `/api/ui/v1/rezervasyonlar/${rezId}/onayla`)).ok()).toBe(false);

  await op.goto(`${KOK}/app/rezervasyonlar/${rezId}`);
  await expect(op.getByRole('button', { name: 'Onayla', exact: true })).toHaveCount(0);
  await expect(op.getByRole('button', { name: 'Kaydet', exact: true })).toHaveCount(0);

  await op.goto(`${KOK}/app/takvim?ay=${isoAy(bas)}&plaka=${arac.plaka}`);
  await expect(op.getByText('Araç: 0')).toBeVisible();
  await op.close();

  const rez = await apiGet<{ rezervasyon: { durum: string } }>(page, `/api/ui/v1/rezervasyonlar/${rezId}`);
  expect(rez.rezervasyon.durum).toBe('Rezerv');
  const iptal = await apiPost(page, `/api/ui/v1/rezervasyonlar/${rezId}/iptal`);
  expect(iptal.ok(), `rez iptal: ${iptal.status()}`).toBe(true);
});

for (const yol of ['/app/takvim', '/app/musaitlik', '/app/rez-sartlari', '/app/filo-kiralama', '/app/teklifler']) {
  test(`sayfa izni: Muhasebe ${yol} açamaz (uyarı bandı + ana sayfa)`, async ({ page }) => {
    await gir(page, ORTAM.gercekMuhasebe);
    await page.goto(`${KOK}${yol}`);
    await expect(page.getByText('Bu sayfayı görüntüleme yetkiniz yok.')).toBeVisible();
    await expect(page).toHaveURL(`${KOK}/app/`);
  });
}
