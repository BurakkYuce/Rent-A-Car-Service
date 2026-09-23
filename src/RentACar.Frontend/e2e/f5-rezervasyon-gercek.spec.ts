import { expect, test, type Page } from '@playwright/test';

import {
  apiGet,
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
  trGun,
} from './gercek';
import { hatalariTopla } from './ortak';
import { ORTAM } from './ortam';

/**
 * F5.3 — GERÇEK backend uçtan uca (sahte API yok): rezervasyon → onay → takvim → kiraya çevir → kira formu;
 * teklif → gönder → kabul → rezervasyon. Beklenen değerler elle kurulmuş senaryodan: 09:00 → +3 gün 09:00
 * penceresi takvimde 4 güne (başlangıç + 2 ara gün + dönüş sabahı) değer; kira formu aynı aracı ve günleri taşır.
 * Koşum: `RACAR_E2E_KOK` + `RACAR_E2E_SIFRE` (bkz. `ortam.ts`); yoksa atlanır.
 */
test.skip(GERCEK_YOK, 'gerçek backend ortamı yok (RACAR_E2E_KOK / RACAR_E2E_SIFRE)');
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
  return (
    await apiGet<{ rezervasyon: RezOzeti }>(page, `/api/ui/v1/rezervasyonlar/${id}`)
  ).rezervasyon;
}

async function takvimHucreleri(page: Page, ay: string, plaka: string, tur: 'Rezervasyon' | 'Kira') {
  await page.goto(`${KOK}/app/takvim?ay=${ay}&plaka=${encodeURIComponent(plaka)}`);
  const izgara = page.locator('.izgara-kap');
  await expect(izgara).toHaveAttribute('aria-busy', 'false');
  await expect(page.getByRole('link', { name: new RegExp(`^${plaka}`) })).toBeVisible();
  return page.getByTitle(`${plaka} — ${tur}`, { exact: true });
}

test('rezervasyon: oluştur → onayla → takvimde R (4 gün) → kiraya çevir → SPA kira formu aynı araç ve günlerle', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, AG);
  await gir(page, ORTAM.gercekAdmin);
  const bas = rastgeleBaslangic();
  const bit = gunEkle(bas, 3);
  const arac = await musaitArac(page, bas);
  const musteri = await birMusteri(page);
  const proje = `E2E-F53-${Date.now()}`;

  await page.goto(`${KOK}/app/rezervasyonlar/yeni`);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Yeni Rezervasyon');
  await sec(page, 'Müşteri', musteri.etiket.slice(0, 4), musteri.etiket);
  await sec(page, 'Araç', arac.plaka, new RegExp(`^${arac.plaka}`));
  await gunYaz(page, 'Başlangıç', bas);
  await gunYaz(page, 'Bitiş', bit);
  await page.getByLabel('Günlük ücret').fill('1000');
  await page.getByLabel('Proje adı').fill(proje);
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();

  await expect(page).toHaveURL(/\/app\/rezervasyonlar\/[0-9a-f-]{36}$/);
  const rezId = page.url().split('/').pop()!;
  await expect(page.getByTestId('rez-durum')).toHaveText('Rezerv');
  const detay = await rezervasyon(page, rezId);
  expect(detay).toMatchObject({ vehicleId: arac.id, musteriId: musteri.id });
  expect(Number(detay.gun)).toBe(3); // 09:00 → +3 gün 09:00

  // Liste: Blazor ile aynı süzgeç (ara = proje adı değil; Rez No/müşteri/plaka) — plaka ile bulunur, durum Rezerv.
  await page.goto(`${KOK}/app/rezervasyonlar?q=${arac.plaka}`);
  await expect(page.getByRole('gridcell', { name: proje })).toBeVisible();

  await page.goto(`${KOK}/app/rezervasyonlar/${rezId}`);
  await page.getByRole('button', { name: 'Onayla', exact: true }).click();
  await expect(page.getByTestId('rez-durum')).toHaveText('Onaylı');

  // Takvim: sunucu doluluğu — pencere 4 takvim gününe dokunur (bas 09:00 … bit 09:00, yarı açık gün kesişimi).
  const rHucreleri = await takvimHucreleri(page, isoAy(bas), arac.plaka, 'Rezervasyon');
  await expect(rHucreleri).toHaveCount(4);
  // Plaka bağlantısı → kira formu ?varac= (F4.3 sözleşmesi) ve form aracı dolu açar.
  const plakaBag = page.getByRole('link', { name: new RegExp(`^${arac.plaka}`) });
  await expect(plakaBag).toHaveAttribute('href', `/app/kiralar/yeni?varac=${arac.id}`);
  await plakaBag.click();
  await expect(page).toHaveURL(new RegExp(`/app/kiralar/yeni\\?varac=${arac.id}$`));
  const hizli = page.locator('[data-rc-sekme="hizli"]');
  await expect(hizli.getByLabel('Araç', { exact: true })).toHaveValue(new RegExp(`^${arac.plaka}`));

  // Kiraya çevir (onaylı) → SPA kira formu; kira aynı araç + aynı günler.
  await page.goto(`${KOK}/app/rezervasyonlar/${rezId}`);
  await page.getByRole('button', { name: 'Kiraya çevir', exact: true }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Kiraya çevir' }).click();
  await expect(page).toHaveURL(/\/app\/kiralar\/[0-9a-f-]{36}$/);
  const kiraId = page.url().split('/').pop()!;
  await expect(hizli.getByLabel('Araç', { exact: true })).toHaveValue(new RegExp(`^${arac.plaka}`));
  await expect(hizli.getByLabel('Başlangıç', { exact: true })).toHaveValue(trGun(bas));
  await expect(hizli.getByLabel('Bitiş (beklenen)', { exact: true })).toHaveValue(trGun(bit));

  // Rezervasyon kiraya bağlandı; takvimde artık R yok (kira teslim edilmeden K da yok — Kirada değil).
  const rez = await rezervasyon(page, rezId);
  expect(rez).toMatchObject({ durum: 'KirayaCevrildi', kiraId });
  await expect(await takvimHucreleri(page, isoAy(bas), arac.plaka, 'Rezervasyon')).toHaveCount(0);

  // Temizlik: kira iptal (silme yok).
  const iptal = await apiPost(page, `/api/ui/v1/kiralar/${kiraId}/iptal`, {});
  expect(iptal.ok(), `kira iptal: ${iptal.status()} ${await iptal.text()}`).toBe(true);
  expect(hatalar).toEqual([]);
});

test('teklif: oluştur → gönder → kabul → rezervasyon (aynı müşteri/araç/pencere), kabul sonrası eylem yok', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, AG);
  await gir(page, ORTAM.gercekAdmin);
  const bas = rastgeleBaslangic();
  const bit = gunEkle(bas, 3);
  const arac = await musaitArac(page, bas);
  const musteri = await birMusteri(page);

  await page.goto(`${KOK}/app/teklifler/yeni`);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Yeni Teklif');
  await sec(page, 'Müşteri', musteri.etiket.slice(0, 4), musteri.etiket);
  await sec(page, 'Araç', arac.plaka, new RegExp(`^${arac.plaka}`));
  await gunYaz(page, 'Başlangıç', bas);
  await gunYaz(page, 'Bitiş', bit);
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
  const rezId = page.url().split('/').pop()!;
  const rez = await rezervasyon(page, rezId);
  expect(rez).toMatchObject({ vehicleId: arac.id, musteriId: musteri.id });
  expect(Number(rez.gun)).toBe(3);

  // Temizlik: rezervasyon iptal (OperationsDelete — Admin).
  const iptal = await apiPost(page, `/api/ui/v1/rezervasyonlar/${rezId}/iptal`);
  expect(iptal.ok(), `rez iptal: ${iptal.status()} ${await iptal.text()}`).toBe(true);
  expect(hatalar).toEqual([]);
});
