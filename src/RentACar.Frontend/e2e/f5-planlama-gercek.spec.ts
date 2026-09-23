import { expect, test } from '@playwright/test';

import {
  apiGet,
  apiGetDurum,
  birMusteri,
  GERCEK_YOK,
  gir,
  gunEkle,
  isoGun,
  KOK,
  musaitArac,
  rastgeleBaslangic,
  sec,
  trGun,
} from './gercek';
import { hatalariTopla } from './ortak';
import { ORTAM } from './ortam';

/**
 * F5.3 — GERÇEK backend: müsaitlik → "Kirala" (F4.3 `?varac&vfrom&vto&vgrup` sözleşmesi) kira formunu dolu açar;
 * rez şartı oluştur → karşılandı → geri al → sil; filo oluştur → künye → tamamla; şube kapsamı (başka şubeye
 * atanmış operatör Merkez kayıtlarını göremez) ve sayfa izni (Muhasebe F5 sayfasına giremez).
 * Beklenen değerler elle: 3 ay × 1.000 ₺ × (1 + %20 KDV) = 3.600,00 ₺.
 */
test.skip(GERCEK_YOK, 'gerçek backend ortamı yok (RACAR_E2E_KOK / RACAR_E2E_SIFRE)');
test.describe.configure({ mode: 'serial' });

const AG = [/Failed to load resource: the server responded with a status of 4\d\d/];

test('müsaitlik → Kirala: bağlantı sunucunun çözdüğü pencereyi taşır, kira formu araç + günlerle dolu açılır', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, AG);
  await gir(page, ORTAM.gercekAdmin);
  const bas = rastgeleBaslangic();
  const bit = gunEkle(bas, 3);
  const arac = await musaitArac(page, bas);
  const secilen = (
    await apiGet<{ araclar: { id: string; grup: string | null }[] }>(page, '/api/ui/v1/musaitlik', {
      basGun: isoGun(bas),
      gun: '3',
    })
  ).araclar.find((a) => a.id === arac.id);
  const grup = secilen?.grup ?? '';

  // Gün-sayısı modu: bitiş alanı BOŞ — bağlantı ham alanı değil çözülmüş pencereyi taşımalı (Blazor FAZ-48).
  const sorgu = `basGun=${isoGun(bas)}&gun=3&basSaat=09:00&bitSaat=09:00&plaka=${arac.plaka}${grup ? `&grup=${encodeURIComponent(grup)}` : ''}`;
  await page.goto(`${KOK}/app/musaitlik?${sorgu}`);
  await expect(page.getByText(`${trGun(bas)} 09:00 – ${trGun(bit)} 09:00 arası`)).toBeVisible();
  const kirala = page.getByRole('link', { name: new RegExp(`Kirala ${arac.plaka}`) });
  const beklenen = new URLSearchParams({ varac: arac.id, vfrom: isoGun(bas), vto: isoGun(bit) });
  if (grup) beklenen.set('vgrup', grup);
  await expect(kirala).toHaveAttribute('href', `/app/kiralar/yeni?${beklenen.toString()}`);

  await kirala.click();
  await expect(page).toHaveURL((u) => u.pathname === '/app/kiralar/yeni' && u.searchParams.get('varac') === arac.id);
  const hizli = page.locator('[data-rc-sekme="hizli"]');
  await expect(hizli.getByLabel('Araç', { exact: true })).toHaveValue(new RegExp(`^${arac.plaka}`));
  await expect(hizli.getByLabel('Başlangıç', { exact: true })).toHaveValue(trGun(bas));
  await expect(hizli.getByLabel('Bitiş (beklenen)', { exact: true })).toHaveValue(trGun(bit));
  expect(hatalar).toEqual([]);
});

test('rez şartı: oluştur → karşılandı → geri al → sil (onaylı)', async ({ page }) => {
  const hatalar = hatalariTopla(page, AG);
  await gir(page, ORTAM.gercekAdmin);
  const musteri = await birMusteri(page);
  const sart = `E2E-F53 bebek koltuğu ${Date.now()}`;

  await page.goto(`${KOK}/app/rez-sartlari`);
  await page.getByRole('button', { name: 'Yeni şart / talep' }).click();
  const duzenleyici = page.getByRole('region', { name: 'Yeni şart / talep' });
  await sec(page, 'Müşteri', musteri.etiket.slice(0, 4), musteri.etiket, duzenleyici);
  await duzenleyici.getByRole('textbox', { name: 'Şart / talep' }).fill(sart);
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect(page.getByText('Şart / talep oluşturuldu.')).toBeVisible();

  await page.getByRole('button', { name: `Karşılandı ${sart}` }).click();
  await expect(page.getByText('Karşılandı olarak işaretlendi.')).toBeVisible();
  await page.getByRole('button', { name: `Geri al ${sart}` }).click();
  await expect(page.getByText('Karşılama geri alındı.')).toBeVisible();
  await expect(page.getByRole('button', { name: `Karşılandı ${sart}` })).toBeVisible();

  await page.getByRole('button', { name: `Sil ${sart}` }).click();
  await page.getByRole('alertdialog').or(page.getByRole('dialog')).getByRole('button', { name: 'Sil' }).click();
  await expect(page.getByText('Talep silindi.')).toBeVisible();
  await expect(page.getByRole('button', { name: `Sil ${sart}` })).toHaveCount(0);
  expect(hatalar).toEqual([]);
});

test('filo: oluştur → sunucu taksit planı (3.600,00 ₺) → künye → tamamla; operatör (başka şube) göremez', async ({
  page,
  browser,
}) => {
  const hatalar = hatalariTopla(page, AG);
  await gir(page, ORTAM.gercekAdmin);
  const arac = await musaitArac(page, rastgeleBaslangic());
  const musteri = await birMusteri(page);

  await page.goto(`${KOK}/app/filo-kiralama/yeni`);
  await sec(page, 'Müşteri', musteri.etiket.slice(0, 4), musteri.etiket);
  await sec(page, 'Araç', arac.plaka, new RegExp(`^${arac.plaka}`));
  await page.getByRole('textbox', { name: 'Süre (ay)' }).fill('3');
  await page.getByRole('textbox', { name: 'Aylık ücret' }).fill('1.000');
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect(page).toHaveURL(/\/app\/filo-kiralama\/[0-9a-f-]{36}$/);
  const filoId = page.url().split('/').pop()!;
  await expect(page.getByRole('region', { name: 'Taksit planı tablosu' })).toContainText('3.600,00');

  const aciklama = `E2E-F53 künye ${Date.now()}`;
  await page.getByRole('textbox', { name: 'Açıklama' }).fill(aciklama);
  await page.getByRole('button', { name: 'Künyeyi kaydet' }).click();
  await expect(page.getByText('Künye kaydedildi.')).toBeVisible();
  await page.reload();
  await expect(page.getByRole('textbox', { name: 'Açıklama' })).toHaveValue(aciklama);

  // Şube kapsamı: "ADV Şube B"ye atanmış operatör Merkez aracının sözleşmesini açamaz, listede görmez.
  const op = await browser.newPage();
  await gir(op, ORTAM.gercekOperator);
  expect([403, 404]).toContain(await apiGetDurum(op, `/api/ui/v1/filo-kiralama/${filoId}`));
  const opListe = await apiGet<{ kayitlar: { id: string }[] }>(op, '/api/ui/v1/filo-kiralama', {
    plaka: arac.plaka,
  });
  expect(opListe.kayitlar.map((k) => k.id)).not.toContain(filoId);
  await op.close();

  await page.getByRole('button', { name: 'Tamamla', exact: true }).click();
  await expect(page.getByText('Sözleşme tamamlandı.')).toBeVisible();
  await expect(page.getByRole('heading', { level: 1 })).toContainText('Tamamlandı');
  await expect(page.getByRole('button', { name: 'Tamamla', exact: true })).toHaveCount(0);
  expect(hatalar).toEqual([]);
});
