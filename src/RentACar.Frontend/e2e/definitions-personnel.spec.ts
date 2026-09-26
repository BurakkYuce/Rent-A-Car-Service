import { expect, test, type Page } from '@playwright/test';

import { BEN, seriousViolations, collectErrors, kaydet, logIn, problem, writeXsrf } from './ortak';
import { pagedEndpoints } from './definition-remaining-fakes';
import { waitReady, measureOverflow, type VitrinSayfasi } from './vitrin-sayfalari';

/**
 * F11.2d KVKK ekranları: personel (TC yazma-yalnız, maaş yalnız tekil detayda) ve veri içe aktar (sayaç + mesaj
 * özeti). Üç zorunlu senaryo personelde; axe + taşma; hiçbir TC/maaş değeri tarayıcı deposuna yazılmaz.
 */
declare const Buffer: { from(data: string, encoding: 'base64'): never };

const NETWORK_ERROR = [/Failed to load resource: the server responded with a status of 4\d\d/];
const NATIONAL_ID = '10000000146';
const P_1 = '66666666-0000-4000-8000-00000000a001';

const listRow = {
  id: P_1,
  kod: 'P1',
  ad: 'Ali',
  soyad: 'Veli',
  sube: 'Merkez',
  gorevTanimi: 'Teslimat',
  cepTel: '05320000000',
  mailAdresi: null,
  iseGiris: '2026-01-04T21:00:00+00:00',
  iseCikis: null,
  aktif: true,
};

const detail = (extra: Record<string, unknown> = {}) => ({
  ...listRow,
  tcKimlikTanimli: true,
  surucuBelgeNo: null,
  maas: 45000.75,
  subeId: null,
  adres: null,
  evTelefonu: null,
  isTelefonu: null,
  referans: null,
  aciklama: null,
  sSinifi: 'B',
  sVerilisTarihi: null,
  sVerilisYeri: null,
  dogumTarihi: '1990-05-09T21:00:00+00:00',
  dogumYeri: null,
  babaAdi: null,
  anaAdi: null,
  il: null,
  ilce: null,
  mahalle: null,
  ciltNo: null,
  aileSiraNo: null,
  siraNo: null,
  kanGrubu: null,
  racTabletNo: null,
  surum: 'p-1',
  ...extra,
});

const PERSONNEL: VitrinSayfasi = {
  ad: 'personel',
  yol: '/app/personel',
  baslik: 'Personel',
  hazir: async (page) => {
    await expect(page.getByRole('cell', { name: 'Teslimat' })).toBeVisible();
  },
};
const IMPORT: VitrinSayfasi = {
  ad: 'ice-aktar',
  yol: '/app/ice-aktar',
  baslik: 'Veri İçe Aktar (Göç)',
  hazir: async (page) => {
    await expect(page.getByRole('button', { name: 'Araçları Aktar' })).toBeVisible();
  },
};

async function storageText(page: Page): Promise<string> {
  return page.evaluate(() => JSON.stringify({ ...localStorage, ...sessionStorage }));
}

test.beforeEach(async ({ page }) => {
  await logIn(page, { ...BEN, izinler: [...BEN.izinler, 'ManageUsers'] });
  await page.route('**/api/ui/v1/secim/sube?*', (r) =>
    r.fulfill({ json: [{ id: 's1', etiket: 'Merkez' }] }),
  );
});

test('personel: liste PII taşımaz; düzenle → TC boş açılır, sil işaretiyle "" gider; axe iki tema', async ({
  page,
}) => {
  const errors = collectErrors(page);
  const { writes } = await pagedEndpoints(page, 'personel', {
    rows: () => [listRow],
    one: () => detail(),
  });
  await page.goto(PERSONNEL.yol);
  await waitReady(page, PERSONNEL);
  await expect(page.getByRole('cell', { name: '05.01.2026' })).toBeVisible();
  expect(await seriousViolations(page), 'açık').toEqual([]);
  await page.emulateMedia({ colorScheme: 'dark' });
  expect(await seriousViolations(page), 'koyu').toEqual([]);

  await page.getByRole('button', { name: 'Düzenle' }).click();
  await expect(page.getByRole('textbox', { name: 'TC Kimlik No (yeni değer)' })).toHaveValue('');
  await expect(page.getByRole('textbox', { name: 'Maaş' })).toHaveValue('45.000,75');
  await page.getByRole('checkbox', { name: "Kayıtlı TC'yi sil" }).check();
  expect(await seriousViolations(page), 'panel').toEqual([]);
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect.poll(() => writes.length).toBe(1);
  const put = JSON.parse(writes[0]?.govde ?? '{}') as Record<string, unknown>;
  expect(put).toMatchObject({
    kod: 'P1',
    tcKimlik: '',
    maasTemizle: false,
    iseGiris: '2026-01-04T21:00:00.000Z',
    dogumTarihi: '1990-05-09T21:00:00.000Z',
    surum: 'p-1',
  });
  expect('tcTemizle' in put).toBe(false);
  expect(errors).toEqual([]);
});

test('personel: doğrulama hatasında form korunur; yazılan TC depoya düşmez', async ({ page }) => {
  collectErrors(page, NETWORK_ERROR);
  await pagedEndpoints(page, 'personel', {
    rows: () => [listRow],
    one: () => detail(),
    write: (r) =>
      problem(r, 400, 'dogrulama', 'Doğrulama hatası.', {
        errors: { tcKimlik: ['TC kimlik no 11 haneli rakam olmalıdır.'] },
      }),
  });
  await page.goto(PERSONNEL.yol);
  await waitReady(page, PERSONNEL);
  await page.getByRole('button', { name: 'Düzenle' }).click();
  const nationalId = page.getByRole('textbox', { name: 'TC Kimlik No (yeni değer)' });
  await nationalId.fill('1234567890X');
  await page.getByRole('textbox', { name: 'Soyad' }).fill('Kaya');
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.getByText('TC kimlik no 11 haneli rakam olmalıdır.')).toBeVisible();
  await expect(nationalId).toHaveValue('1234567890X');
  await expect(page.getByRole('textbox', { name: 'Soyad' })).toHaveValue('Kaya');
  expect(await storageText(page)).not.toContain('1234567890X');
});

test('personel: oturum düşünce aynı istek aynı anahtar ve gövdeyle; TC depoya yazılmaz', async ({
  page,
}) => {
  collectErrors(page, [...NETWORK_ERROR, /401/]);
  let n = 0;
  const { writes } = await pagedEndpoints(page, 'personel', {
    rows: () => [listRow],
    one: () => detail(),
    write: (r) =>
      ++n === 1
        ? problem(r, 401, 'oturum_yok', 'Oturum açık değil.')
        : r.fulfill({ json: detail() }),
  });
  await page.route('**/api/ui/v1/oturum/xsrf', async (route) => {
    await writeXsrf(page, 'anonim-belirtec');
    return route.fulfill({ status: 204 });
  });
  await page.route('**/api/ui/v1/oturum/giris', async (route) => {
    await writeXsrf(page, 'yeni-belirtec');
    return route.fulfill({ json: BEN });
  });
  await page.goto(PERSONNEL.yol);
  await waitReady(page, PERSONNEL);
  await page.getByRole('button', { name: 'Düzenle' }).click();
  await page.getByRole('textbox', { name: 'TC Kimlik No (yeni değer)' }).fill(NATIONAL_ID);
  await page.getByRole('button', { name: 'Kaydet' }).click();
  const dialog = page.getByRole('dialog', { name: 'Oturumunuz sona erdi' });
  await expect(dialog).toBeVisible();
  await dialog.getByLabel('Parola').fill('rastgele-e2e-parolasi');
  await dialog.getByRole('button', { name: 'Giriş yap ve devam et' }).click();
  await expect.poll(() => writes.length).toBe(2);
  expect(writes[1]?.govde).toBe(writes[0]?.govde);
  expect(writes[1]?.anahtar).toBe(writes[0]?.anahtar);
  expect(JSON.parse(writes[1]?.govde ?? '{}')).toMatchObject({
    tcKimlik: NATIONAL_ID,
    surum: 'p-1',
  });
  expect(await storageText(page)).not.toContain(NATIONAL_ID);
});

test('personel: cakisma formu silmez — dokunulmayan alan sunucuya çekilir, sonraki PUT yeni sürümle', async ({
  page,
}) => {
  collectErrors(page, [...NETWORK_ERROR, /409/]);
  let current = detail();
  let n = 0;
  const { writes } = await pagedEndpoints(page, 'personel', {
    rows: () => [listRow],
    one: () => current,
    write: (r) => {
      if (++n === 1) {
        current = detail({ gorevTanimi: 'Ofis', maas: 50000, surum: 'p-2' });
        return problem(r, 409, 'cakisma', 'Kayıt siz düzenlerken değişti.');
      }
      return r.fulfill({ json: current });
    },
  });
  await page.goto(PERSONNEL.yol);
  await waitReady(page, PERSONNEL);
  await page.getByRole('button', { name: 'Düzenle' }).click();
  await page.getByRole('textbox', { name: 'Soyad' }).fill('Benim');
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.getByRole('textbox', { name: 'Görev Tanımı' })).toHaveValue('Ofis');
  await expect(page.getByRole('textbox', { name: 'Soyad' })).toHaveValue('Benim');
  expect(writes).toHaveLength(1);
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect.poll(() => writes.length).toBe(2);
  expect(JSON.parse(writes[1]?.govde ?? '{}')).toMatchObject({
    soyad: 'Benim',
    gorevTanimi: 'Ofis',
    maas: 50000,
    surum: 'p-2',
  });
});

test('içe aktar: dosya multipart gider, sonuç sayaç + mesaj özeti; dosya hatası bölümde', async ({
  page,
}) => {
  const errors = collectErrors(page, NETWORK_ERROR);
  const uploads: { path: string; body: string }[] = [];
  await page.route('**/api/ui/v1/ice-aktar/*', (r) => {
    const req = r.request();
    uploads.push({ path: new URL(req.url()).pathname, body: kaydet(req).govde ?? '' });
    return req.url().endsWith('/cari')
      ? problem(r, 400, 'dogrulama', 'Doğrulama hatası.', {
          errors: { dosya: ['Dosya okunamadı (biçim bozuk ya da desteklenmiyor).'] },
        })
      : r.fulfill({
          json: {
            eklenen: 3,
            atlanan: 1,
            hatali: 2,
            hataOzeti: [{ mesaj: 'Plaka zorunludur.', adet: 2 }],
          },
        });
  });
  await page.goto(IMPORT.yol);
  await waitReady(page, IMPORT);
  expect(await seriousViolations(page), 'açık').toEqual([]);

  await page.getByRole('button', { name: 'Araçları Aktar' }).click();
  await expect(page.getByText('Dosya seçilmedi.')).toBeVisible();
  expect(uploads).toHaveLength(0);

  const file = {
    name: 'araclar.csv',
    mimeType: 'text/csv',
    // e2e tsconfig'inde Node tipleri yok (f6-araclar-gercek deseni); Playwright Node Buffer'ı ister.
    buffer: Buffer.from(btoa('Plaka\n34ABC1\n'), 'base64'),
  };
  await page.locator('#ia-arac-dosya').setInputFiles(file);
  await page.getByRole('button', { name: 'Araçları Aktar' }).click();
  await expect(page.getByText('Eklenen: 3 · Atlanan (tekrar): 1 · Hatalı: 2')).toBeVisible();
  await expect(page.getByText('Plaka zorunludur. (2)')).toBeVisible();
  expect(uploads[0]?.path).toBe('/api/ui/v1/ice-aktar/arac');

  await page.locator('#ia-cari-dosya').setInputFiles({ ...file, name: 'musteriler.csv' });
  await page.getByRole('button', { name: 'Müşterileri Aktar' }).click();
  await expect(page.getByText('Dosya okunamadı (biçim bozuk ya da desteklenmiyor).')).toBeVisible();
  expect(await seriousViolations(page), 'sonuç').toEqual([]);
  expect(errors).toEqual([]);
});

for (const s of [PERSONNEL, IMPORT]) {
  const fakes = async (page: Page) => {
    await pagedEndpoints(page, 'personel', { rows: () => [listRow], one: () => detail() });
  };
  test.describe(`${s.ad}: mobil taşma (dokunmatik öykünme)`, () => {
    test.use({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });
    test(`${s.yol}: 320/390/768 px gövde yatay taşması yok`, async ({ page }) => {
      await fakes(page);
      for (const width of [320, 390, 768]) {
        await page.setViewportSize({ width, height: 844 });
        await page.goto(s.yol);
        await waitReady(page, s);
        expect(await measureOverflow(page), `${width}px`).toEqual({ tasma: 0, suclular: [] });
      }
    });
  });
  test(`${s.yol}: 1440 px gövde yatay taşması yok`, async ({ page }) => {
    await fakes(page);
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(s.yol);
    await waitReady(page, s);
    expect(await measureOverflow(page)).toEqual({ tasma: 0, suclular: [] });
  });
}
