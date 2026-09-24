import { expect, test } from '@playwright/test';

import {
  EXPENSE_1,
  INCOMING_1,
  PENALTY_1,
  documentEndpoints,
  incomingRow,
  penaltyDetail,
} from './finance-document-fakes';
import { BEN, ciddiIhlaller, hatalariTopla, oturumAc, problem, xsrfYaz } from './ortak';
import { hazirBekle, tasmaOlc, type VitrinSayfasi } from './vitrin-sayfalari';

/**
 * F8.2b finans belge ekranları: faturalar (+ detay listesi), cezalar, giderler, gelen e-fatura, araç satışları.
 * Üç zorunlu senaryo (doğrulama hatasında form korunur, oturum düşünce form kaybolmaz ve AYNI anahtarla gider,
 * `cakisma` formu silmez) + para (kaybolan yanıttan sonra aynı anahtar → `mukerrer` + mevcut; farklı içerikte form
 * korunur) + axe iki tema + 320/390/768/1440 taşma.
 */
const AG_HATASI = [
  /Failed to load resource: the server responded with a status of 4\d\d/,
  /Failed to load resource: net::ERR_FAILED/,
];

const cell = (name: string) => async (page: import('@playwright/test').Page) => {
  await expect(page.getByRole('gridcell', { name })).toBeVisible();
};

const INVOICES: VitrinSayfasi = {
  ad: 'faturalar',
  yol: '/app/faturalar',
  baslik: 'Faturalar',
  hazir: cell('RNT2026000000001'),
};
const LINES: VitrinSayfasi = {
  ad: 'fatura-detay',
  yol: '/app/faturalar/detay-listesi',
  baslik: 'Fatura Detay Listesi',
  hazir: cell('Kira 3 gün'),
};
const PENALTIES: VitrinSayfasi = {
  ad: 'cezalar',
  yol: '/app/cezalar',
  baslik: 'Trafik Cezaları',
  hazir: cell('CZ-000001'),
};
const EXPENSES: VitrinSayfasi = {
  ad: 'giderler',
  yol: '/app/giderler',
  baslik: 'Giderler',
  hazir: cell('GD-000001'),
};
const INCOMING: VitrinSayfasi = {
  ad: 'gelen-efatura',
  yol: '/app/gelen-efatura',
  baslik: 'Gelen e-Fatura',
  hazir: cell('ETTN-0001'),
};
const SALES: VitrinSayfasi = {
  ad: 'satislar',
  yol: '/app/satislar',
  baslik: 'Araç Satışları',
  hazir: cell('AS-000001'),
};
const PAGES = [INVOICES, LINES, PENALTIES, EXPENSES, INCOMING, SALES];

test.beforeEach(async ({ page }) => {
  await oturumAc(page, {
    ...BEN,
    izinler: [...BEN.izinler, 'OperationsDelete', 'FinanceReverse'],
  });
});

test('tüm sayfalar: içerik + axe iki tema, konsol hatası yok', async ({ page }) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  await documentEndpoints(page);
  for (const s of PAGES) {
    await page.emulateMedia({ colorScheme: 'light' });
    await page.goto(s.yol);
    await hazirBekle(page, s);
    expect(await ciddiIhlaller(page), `${s.ad} açık`).toEqual([]);
    await page.emulateMedia({ colorScheme: 'dark' });
    expect(await ciddiIhlaller(page), `${s.ad} koyu`).toEqual([]);
  }
  expect(hatalar).toEqual([]);
});

test('manuel fatura: doğrulama hatasında form korunur; "1.500,50" → 1500.50; 3 ondalık reddedilir; tekrar AYNI anahtar', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  let n = 0;
  const written = await documentEndpoints(page, {
    write: async (r, path) => {
      if (path !== '/api/ui/v1/faturalar/manuel') return false;
      if (++n === 1)
        await problem(r, 400, 'dogrulama', 'Doğrulama hatası.', {
          errors: { tarih: ['Fatura tarihi kilitli döneme düşüyor.'] },
        });
      else await r.fulfill({ json: { id: 'm1', no: 'RNT2026000000002' } });
      return true;
    },
  });
  await page.goto(INVOICES.yol);
  await hazirBekle(page, INVOICES);
  const form = page.getByRole('region', { name: 'Manuel Fatura' });
  await form.getByRole('combobox', { name: 'Cari' }).fill('Ay');
  await page.getByRole('option', { name: 'Ayşe Yılmaz' }).click();
  const amount = form.getByRole('textbox', { name: 'Net Tutar' });
  await amount.fill('10,555');
  await form.getByRole('button', { name: 'Manuel Fatura Kes' }).click();
  await expect(form.getByText('En fazla 2 ondalık hane girilebilir.')).toBeVisible();
  expect(written).toHaveLength(0);

  await amount.fill('1.500,50');
  await form.getByRole('textbox', { name: 'Fatura Tarihi' }).fill('01.09.2026');
  await form.getByRole('button', { name: 'Manuel Fatura Kes' }).click();
  await expect(form.getByText('Fatura tarihi kilitli döneme düşüyor.')).toBeVisible();
  await expect(amount).toHaveValue('1.500,50');
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({
    cariId: 'c0c0c0c0-0000-4000-8000-000000000001',
    netTutar: '1500.50',
    kdvOrani: '0.20',
    tarih: '2026-08-31T21:00:00.000Z',
  });
  expect(JSON.parse(written[0]?.govde ?? '{}')).not.toHaveProperty('kdvTutar');
  expect(await ciddiIhlaller(page)).toEqual([]);

  await form.getByRole('textbox', { name: 'Fatura Tarihi' }).fill('');
  await form.getByRole('button', { name: 'Manuel Fatura Kes' }).click();
  await expect(page.getByText('Fatura kesildi (RNT2026000000002).')).toBeVisible();
  expect(written[1]?.anahtar).toBe(written[0]?.anahtar); // ilk istek yazılmadı: aynı işlem
  expect(written[1]?.anahtar).toMatch(/^[0-9a-f-]{36}$/);
  expect(hatalar).toEqual([]);
});

test('manuel fatura: oturum düşünce form kaybolmaz — yerinde giriş, AYNI istek (aynı anahtar + gövde)', async ({
  page,
}) => {
  let n = 0;
  const written = await documentEndpoints(page, {
    write: async (r, path) => {
      if (path !== '/api/ui/v1/faturalar/manuel') return false;
      if (++n === 1) await problem(r, 401, 'oturum_yok', 'Oturum açık değil.');
      else await r.fulfill({ json: { id: 'm1', no: 'RNT2026000000003' } });
      return true;
    },
  });
  await page.route('**/api/ui/v1/oturum/xsrf', async (route) => {
    await xsrfYaz(page, 'anonim-belirtec');
    return route.fulfill({ status: 204 });
  });
  await page.route('**/api/ui/v1/oturum/giris', async (route) => {
    await xsrfYaz(page, 'yeni-belirtec');
    return route.fulfill({ json: BEN });
  });
  await page.goto(INVOICES.yol);
  await hazirBekle(page, INVOICES);
  const form = page.getByRole('region', { name: 'Manuel Fatura' });
  await form.getByRole('combobox', { name: 'Cari' }).fill('Ay');
  await page.getByRole('option', { name: 'Ayşe Yılmaz' }).click();
  await form.getByRole('textbox', { name: 'Net Tutar' }).fill('2.400,10');
  await form.getByRole('button', { name: 'Manuel Fatura Kes' }).click();

  const dialog = page.getByRole('dialog', { name: 'Oturumunuz sona erdi' });
  await expect(dialog).toBeVisible();
  await expect(page.getByRole('textbox', { name: 'Net Tutar', includeHidden: true })).toHaveValue(
    '2.400,10',
  );
  await dialog.getByLabel('Parola').fill('rastgele-e2e-parolasi');
  await dialog.getByRole('button', { name: 'Giriş yap ve devam et' }).click();
  await expect(page.getByText('Fatura kesildi (RNT2026000000003).')).toBeVisible();
  expect(written).toHaveLength(2);
  expect(written[1]?.anahtar).toBe(written[0]?.anahtar);
  expect(written[1]?.govde).toBe(written[0]?.govde);
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({ netTutar: '2400.10' });
});

test('gelen e-fatura bağlama: cakisma formu silmez — güncel kayıt birleşir, sonraki PUT yeni sürümle', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  let surum = 'v-1';
  let giderTipi: string | null = null;
  let put = 0;
  const written = await documentEndpoints(page, {
    incoming: () => ({ fatura: incomingRow({ giderTipi }), surum }),
    write: async (r) => {
      if (r.request().method() !== 'PUT') return false;
      if (++put === 1) {
        surum = 'v-2';
        giderTipi = 'Arac'; // başka oturum gider türünü değiştirdi
        await problem(r, 409, 'cakisma', 'Kayıt siz düzenlerken değişti; güncel hâli yükleyin.');
      } else await r.fulfill({ json: { id: INCOMING_1, durum: 'Onaylandi', surum: 'v-3' } });
      return true;
    },
  });
  await page.goto(INCOMING.yol);
  await hazirBekle(page, INCOMING);
  await page.getByRole('button', { name: 'KDV Kırılımı / Bağla' }).click();
  const form = page.getByRole('region', { name: 'KDV Kırılımı / Bağla — ETTN-0001' });
  await expect(form.getByRole('textbox', { name: '%20 KDV' })).toHaveValue('200,00');
  const zero = form.getByRole('textbox', { name: '%0 Matrah' });
  await zero.fill('150,25');
  await form.getByRole('button', { name: 'Kaydet' }).click();

  await expect(page.locator('rc-uyari-bandi')).toContainText('Kayıt siz düzenlerken değişti');
  await expect(zero).toHaveValue('150,25'); // form SİLİNMEDİ
  await expect(
    form.getByRole('combobox', { name: 'Gider Türü' }).locator('option:checked'),
  ).toHaveText('Araç');
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({
    surum: 'v-1',
    kdv20Matrah: '1000',
    kdv20: '200',
    kdv0Matrah: '150.25',
    giderTipi: null,
  });
  await form.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.getByText('Kırılım ve bağlar kaydedildi.')).toBeVisible();
  expect(JSON.parse(written[1]?.govde ?? '{}')).toMatchObject({
    surum: 'v-2',
    kdv0Matrah: '150.25',
    giderTipi: 'Arac',
  });
  expect(hatalar).toEqual([]);
});

test('ceza ödemesi: kaybolan yanıt → form korunur, tekrar AYNI anahtar + gövde → mukerrer + mevcut, tek ödeme', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  let payments = 0;
  const written = await documentEndpoints(page, {
    penalty: () => penaltyDetail(payments === 0 ? 0 : 900),
    write: async (r, path) => {
      if (path !== `/api/ui/v1/cezalar/${PENALTY_1}/odeme`) return false;
      if (payments === 0) {
        payments = 1; // sunucu YAZDI, yanıt kayboldu
        await r.abort('failed');
        return true;
      }
      await problem(
        r,
        409,
        'mukerrer',
        'Bu ceza ödemesi zaten kaydedildi (900,00 TL, kalem 1); yeni ödeme yazılmadı.',
        {
          mevcut: { id: 'o1', belgeNo: '1', tutar: 900, doviz: 'TRY', ayniIcerik: true },
        },
      );
      return true;
    },
  });
  await page.goto(PENALTIES.yol);
  await hazirBekle(page, PENALTIES);
  await page.getByRole('button', { name: 'Detay' }).click();
  const form = page.getByRole('region', { name: 'Kalem Ödemesi' });
  await form.getByRole('combobox', { name: 'Kalem' }).selectOption({ index: 1 });
  await form.getByRole('combobox', { name: 'Hesap' }).selectOption('Banka');
  await form.getByRole('button', { name: 'Öde' }).click();

  await expect(form.getByText('İşlemin sonucu bilinmiyor')).toBeVisible();
  await expect(form.getByRole('combobox', { name: 'Hesap' }).locator('option:checked')).toHaveText(
    'Banka',
  );
  await form.getByRole('button', { name: 'Öde' }).click();

  await expect(
    page.getByText('Bu işlem zaten kayıtlı: 1 · 900,00 ₺. Yeni kayıt yazılmadı.'),
  ).toBeVisible();
  expect(written).toHaveLength(2);
  expect(written[1]?.anahtar).toBe(written[0]?.anahtar);
  expect(written[1]?.govde).toBe(written[0]?.govde);
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({ hesap: 'Banka', tutar: null });
  expect(payments).toBe(1);
  expect(hatalar.filter((h) => !/Failed to load resource/.test(h))).toEqual([]);
});

test('gider: başka içerikle mukerrer → girilen YAZILMADI notu, form korunur; sonraki gönderim yeni anahtar', async ({
  page,
}) => {
  let n = 0;
  const written = await documentEndpoints(page, {
    write: async (r, path) => {
      if (path !== '/api/ui/v1/giderler') return false;
      if (++n === 1)
        await problem(r, 409, 'mukerrer', 'Bu işlem anahtarıyla başka bir gider yazılmış.', {
          mevcut: {
            id: EXPENSE_1,
            belgeNo: 'GD-000001',
            tutar: 600,
            doviz: 'TRY',
            ayniIcerik: false,
          },
        });
      else await r.fulfill({ json: { id: 'g2', no: 'GD-000002' } });
      return true;
    },
  });
  await page.goto(EXPENSES.yol);
  await hazirBekle(page, EXPENSES);
  await page.getByRole('button', { name: 'Yeni Gider' }).click();
  const form = page.getByRole('region', { name: 'Yeni Gider' });
  const amount = form.getByRole('textbox', { name: 'Net Tutar' });
  await amount.fill('750');
  await form.getByRole('button', { name: 'Kaydet' }).click();
  await expect(form.getByText('Girdiğiniz kayıt YAZILMADI')).toBeVisible();
  await expect(amount).toHaveValue('750,00');
  await form.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.getByText('Gider kaydedildi (GD-000002).')).toBeVisible();
  expect(written[1]?.anahtar).not.toBe(written[0]?.anahtar);
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({
    netTutar: '750.00',
    kdvOrani: '0.20',
    odemeYontemi: 'Nakit',
    doviz: 'TRY',
    kur: null,
  });
});

for (const s of PAGES) {
  test.describe(`${s.ad}: mobil taşma (dokunmatik öykünme)`, () => {
    test.use({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });
    test(`${s.yol}: 320/390/768 px gövde yatay taşması yok`, async ({ page }) => {
      await documentEndpoints(page);
      for (const width of [320, 390, 768]) {
        await page.setViewportSize({ width, height: 844 });
        await page.goto(s.yol);
        await hazirBekle(page, s);
        expect(await tasmaOlc(page), `${width}px`).toEqual({ tasma: 0, suclular: [] });
      }
    });
  });
  test(`${s.yol}: 1440 px gövde yatay taşması yok`, async ({ page }) => {
    await documentEndpoints(page);
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(s.yol);
    await hazirBekle(page, s);
    expect(await tasmaOlc(page)).toEqual({ tasma: 0, suclular: [] });
  });
}
