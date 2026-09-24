import { expect, test, type Route } from '@playwright/test';

import {
  EXPENSE_1,
  INCOMING_1,
  PENALTY_1,
  documentEndpoints,
  incomingRow,
  invoiceDetail,
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

// Sayfa başına ayrı test: tek testte tüm sayfalar × 2 tema axe taraması CI'da 30 sn sınırına dayanıyordu.
for (const s of PAGES) {
  test(`${s.ad}: içerik + axe iki tema, konsol hatası yok`, async ({ page }) => {
    const hatalar = hatalariTopla(page, AG_HATASI);
    await documentEndpoints(page);
    await page.emulateMedia({ colorScheme: 'light' });
    await page.goto(s.yol);
    await hazirBekle(page, s);
    expect(await ciddiIhlaller(page), `${s.ad} açık`).toEqual([]);
    await page.emulateMedia({ colorScheme: 'dark' });
    expect(await ciddiIhlaller(page), `${s.ad} koyu`).toEqual([]);
    expect(hatalar).toEqual([]);
  });
}

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
  // Gövde DONDU: form kilitli, düğme aynı işlemi tekrar gönderir.
  const account = form.getByRole('combobox', { name: 'Hesap' });
  await expect(account.locator('option:checked')).toHaveText('Banka');
  await expect(account).toBeDisabled();
  await form.getByRole('button', { name: 'Aynı işlemi tekrar gönder' }).click();

  // r300b N3: aynı içerik → yalnız "kaydedildi" (iade/iptal çağrısı yok).
  await expect(form.getByText('Önceki denemeniz kaydedildi (1, 900,00 ₺).')).toBeVisible();
  await expect(form.getByText('iade/iptal')).toHaveCount(0);
  expect(written).toHaveLength(2);
  expect(written[1]?.anahtar).toBe(written[0]?.anahtar);
  expect(written[1]?.govde).toBe(written[0]?.govde);
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({ hesap: 'Banka', tutar: null });
  expect(payments).toBe(1);
  expect(hatalar.filter((h) => !/Failed to load resource/.test(h))).toEqual([]);
});

test('gider: mukerrer + mevcut (farklı içerik) → "önceki denemeniz kaydedildi" notu, form temizlenir; sonraki işlem yeni anahtar', async ({
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
  await expect(
    form.getByText(
      'Önceki denemeniz kaydedildi (GD-000001, 600,00 ₺); değiştirdiğiniz içerik yazılmadı. Düzeltme için iade/iptal edin.',
    ),
  ).toBeVisible();
  await expect(amount).toHaveValue('');
  // L1: aynı metin toast'ta ikinci kez çıkmaz (not tek kaynak).
  await expect(page.locator('rc-toast').getByText('Önceki denemeniz')).toHaveCount(0);
  await amount.fill('120');
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

/** Gerçek uç semantiği (ExpenseUiApi.Create): anahtar → kayıt; aynı anahtar + aynı içerik → 409 ayniIcerik:true. */
function keyStore() {
  const db = new Map<string, string>();
  return {
    db,
    async handle(r: Route, amount: (body: Record<string, unknown>) => string, no: string) {
      const key = r.request().headers()['idempotency-key'] ?? '';
      const body = JSON.parse(r.request().postData() ?? '{}') as Record<string, unknown>;
      const existing = db.get(key);
      if (existing !== undefined) {
        await problem(r, 409, 'mukerrer', 'Bu kayıt zaten yazıldı.', {
          mevcut: {
            id: 'k1',
            belgeNo: no,
            tutar: Number(existing),
            doviz: 'TRY',
            ayniIcerik: existing === amount(body),
          },
        });
        return 'dup';
      }
      db.set(key, amount(body));
      return 'new';
    },
  };
}

test('r300 P1 gider: kaybolan yanıt → gövde DONAR, tutar düzeltilemez; tekrar aynı gövde → önceki deneme kayıtlı, TEK gider', async ({
  page,
}) => {
  const store = keyStore();
  let lose = true;
  const written = await documentEndpoints(page, {
    write: async (r, path) => {
      if (path !== '/api/ui/v1/giderler') return false;
      const result = await store.handle(r, (b) => String(b['netTutar']), 'GD-000010');
      if (result === 'new') {
        if (lose) {
          lose = false;
          await r.abort('failed'); // sunucu YAZDI, yanıt kayboldu
        } else await r.fulfill({ json: { id: 'g9', no: 'GD-000011' } });
      }
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
  await expect(form.getByText('İşlemin sonucu bilinmiyor')).toBeVisible();
  await expect(amount).toBeDisabled(); // kullanıcı düzeltip farklı gövdeyi aynı anahtarla gönderemez
  await form.getByRole('button', { name: 'Aynı işlemi tekrar gönder' }).click();
  await expect(form.getByText('Önceki denemeniz kaydedildi (GD-000010,')).toBeVisible();
  expect(written).toHaveLength(2);
  expect(written[1]?.anahtar).toBe(written[0]?.anahtar);
  expect(written[1]?.govde).toBe(written[0]?.govde);
  expect(store.db.size).toBe(1);
  // Form açıldı ve temizlendi; yeni gider bilinçli olarak yeni anahtarla gider.
  await expect(amount).toBeEnabled();
  await amount.fill('780');
  await form.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.getByText('Gider kaydedildi (GD-000011).')).toBeVisible();
  expect(written[2]?.anahtar).not.toBe(written[0]?.anahtar);
});

test('r300 P2 manuel fatura: istek uçarken form KİLİTLİ; başarı mesajı sunucunun genel toplamıyla', async ({
  page,
}) => {
  let release: () => void = () => undefined;
  const gate = new Promise<void>((res) => (release = res));
  const written = await documentEndpoints(page, {
    write: async (r, path) => {
      if (path !== '/api/ui/v1/faturalar/manuel') return false;
      await gate;
      await r.fulfill({ json: { id: 'i9', no: 'RNT2026000000009' } });
      return true;
    },
    read: async (r, path) => {
      if (path !== '/api/ui/v1/faturalar/i9') return false;
      await r.fulfill({
        json: { ...invoiceDetail(), id: 'i9', no: 'RNT2026000000009', genelToplam: 1200 },
      });
      return true;
    },
  });
  await page.goto(INVOICES.yol);
  await hazirBekle(page, INVOICES);
  const form = page.getByRole('region', { name: 'Manuel Fatura' });
  await form.getByRole('combobox', { name: 'Cari' }).fill('Ay');
  await page.getByRole('option', { name: 'Ayşe Yılmaz' }).click();
  const net = form.getByRole('textbox', { name: 'Net Tutar' });
  await net.fill('1000');
  await form.getByRole('button', { name: 'Manuel Fatura Kes' }).click();
  await expect(form.getByRole('button', { name: /Gönderiliyor/ })).toBeVisible();
  await expect(net).toBeDisabled();
  release();
  await expect(
    page.getByText('Fatura kesildi (RNT2026000000009, genel toplam 1.200,00 ₺).'),
  ).toBeVisible();
  expect(JSON.parse(written[0]?.govde ?? '{}').netTutar).toBe('1000.00');
  await expect(net).toBeEnabled();
  await expect(net).toHaveValue('');
});

test('r300 P3 gider: döviz değişince açık kur temizlenir (USD kuru EUR giderine gitmez)', async ({
  page,
}) => {
  const written = await documentEndpoints(page, {
    write: async (r, path) => {
      if (path !== '/api/ui/v1/giderler') return false;
      await r.fulfill({ json: { id: 'g3', no: 'GD-000003' } });
      return true;
    },
  });
  await page.goto(EXPENSES.yol);
  await hazirBekle(page, EXPENSES);
  await page.getByRole('button', { name: 'Yeni Gider' }).click();
  const form = page.getByRole('region', { name: 'Yeni Gider' });
  await form.getByRole('textbox', { name: 'Net Tutar' }).fill('100');
  await form.getByRole('combobox', { name: 'Döviz' }).selectOption('USD');
  const rate = form.getByRole('textbox', { name: 'Kur' });
  await rate.fill('35');
  await form.getByRole('combobox', { name: 'Döviz' }).selectOption('EUR');
  await expect(rate).toHaveValue('');
  await form.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.getByText(/Gider kaydedildi/)).toBeVisible();
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({ doviz: 'EUR', kur: null });
});

test('r300 P4 ceza ödemesi: mevcut\'suz 409 → "kaydedilmiş olabilir", anahtar KORUNUR, form kilitli', async ({
  page,
}) => {
  let n = 0;
  const written = await documentEndpoints(page, {
    penalty: () => penaltyDetail(n >= 3 ? 300 : 0),
    write: async (r, path) => {
      if (path !== `/api/ui/v1/cezalar/${PENALTY_1}/odeme`) return false;
      if (++n === 1) await r.abort('failed');
      else if (n === 2)
        await problem(r, 409, 'mukerrer', 'Bu ceza ödemesi zaten kaydedilmiş (çift gönderim).');
      else
        await problem(r, 409, 'mukerrer', 'Bu ceza ödemesi zaten kaydedildi.', {
          mevcut: { id: 'o1', belgeNo: '1', tutar: 300, doviz: 'TRY', ayniIcerik: true },
        });
      return true;
    },
  });
  await page.goto(PENALTIES.yol);
  await hazirBekle(page, PENALTIES);
  await page.getByRole('button', { name: 'Detay' }).click();
  const form = page.getByRole('region', { name: 'Kalem Ödemesi' });
  await form.getByRole('combobox', { name: 'Kalem' }).selectOption({ index: 1 });
  await form.getByRole('textbox', { name: 'Tutar' }).fill('300');
  await form.getByRole('button', { name: 'Öde' }).click();
  await expect(form.getByText('İşlemin sonucu bilinmiyor')).toBeVisible();
  await form.getByRole('button', { name: 'Aynı işlemi tekrar gönder' }).click();
  await expect(form.getByText('Önceki denemeniz kaydedilmiş olabilir')).toBeVisible();
  await expect(form.getByRole('textbox', { name: 'Tutar' })).toBeDisabled();
  await form.getByRole('button', { name: 'Aynı işlemi tekrar gönder' }).click();
  await expect(form.getByText('Önceki denemeniz kaydedildi (1, 300,00 ₺)')).toBeVisible();
  expect(written).toHaveLength(3);
  expect(new Set(written.map((w) => w.anahtar)).size).toBe(1);
  expect(new Set(written.map((w) => w.govde)).size).toBe(1);
});

test('r300 L4 ceza ödemesi: belirsiz deneme varken başka cezaya geçiş onay ister; dönünce form aynı anahtarla kilitli', async ({
  page,
}) => {
  const written = await documentEndpoints(page, {
    write: async (r, path) => {
      if (path !== `/api/ui/v1/cezalar/${PENALTY_1}/odeme`) return false;
      await r.abort('failed');
      return true;
    },
  });
  await page.goto(PENALTIES.yol);
  await hazirBekle(page, PENALTIES);
  await page.getByRole('button', { name: 'Detay' }).click();
  const form = page.getByRole('region', { name: 'Kalem Ödemesi' });
  await form.getByRole('combobox', { name: 'Kalem' }).selectOption({ index: 1 });
  await form.getByRole('textbox', { name: 'Tutar' }).fill('250');
  await form.getByRole('button', { name: 'Öde' }).click();
  await expect(form.getByText('İşlemin sonucu bilinmiyor')).toBeVisible();
  await page
    .getByRole('region', { name: /Ceza CZ-000001/ })
    .getByRole('button', { name: 'Kapat', exact: true })
    .click();
  const dialog = page.locator('rc-onay-diyalogu');
  await expect(dialog).toContainText('Form kapatılsın mı?');
  await dialog.getByRole('button', { name: 'Onayla' }).click();
  await expect(form).toBeHidden();
  await page.getByRole('button', { name: 'Detay' }).click();
  await expect(form.getByRole('textbox', { name: 'Tutar' })).toHaveValue('250,00');
  await expect(form.getByRole('textbox', { name: 'Tutar' })).toBeDisabled();
  await form.getByRole('button', { name: 'Aynı işlemi tekrar gönder' }).click();
  await expect.poll(() => written.length).toBe(2);
  expect(written[1]?.anahtar).toBe(written[0]?.anahtar);
  expect(written[1]?.govde).toBe(written[0]?.govde);
});

test('r300 P5 gelen e-fatura: kirli kırılım onaysız atılmaz; giderleştirme onayı kayıtlı kırılımı gösterir', async ({
  page,
}) => {
  const written = await documentEndpoints(page);
  await page.goto(INCOMING.yol);
  await hazirBekle(page, INCOMING);
  await page.getByRole('button', { name: 'KDV Kırılımı / Bağla' }).click();
  const link = page.getByRole('region', { name: 'KDV Kırılımı / Bağla — ETTN-0001' });
  await expect(link.getByRole('textbox', { name: '%20 KDV' })).toHaveValue('200,00');
  await link.getByRole('textbox', { name: '%0 Matrah' }).fill('150,25');
  await page.getByRole('button', { name: 'Giderleştir', exact: true }).first().click();
  const dialog = page.locator('rc-onay-diyalogu');
  await expect(dialog).toContainText('kaydedilmemiş değişiklik');
  await dialog.getByRole('button', { name: 'Vazgeç' }).click();
  await expect(link.getByRole('textbox', { name: '%0 Matrah' })).toHaveValue('150,25');

  await page.getByRole('button', { name: 'Giderleştir', exact: true }).first().click();
  await dialog.getByRole('button', { name: 'Onayla' }).click();
  const expense = page.getByRole('region', { name: 'Giderleştir — ETTN-0001' });
  await expense.getByRole('button', { name: 'Deftere Yaz' }).click();
  await expect(dialog).toContainText('%20: 1.000,00 ₺ + KDV 200,00 ₺');
  await dialog.getByRole('button', { name: 'Vazgeç' }).click();
  expect(written).toHaveLength(0);
});

test('r300b A gider ödemesi: istek uçarken Kapat / başka Öde / Yeni Gider pasif; yanıt gelince tek ödeme', async ({
  page,
}) => {
  let release: () => void = () => undefined;
  const gate = new Promise<void>((res) => (release = res));
  const written = await documentEndpoints(page, {
    write: async (r, path) => {
      if (path !== `/api/ui/v1/giderler/${EXPENSE_1}/odeme`) return false;
      await gate;
      await r.fulfill({
        json: {
          id: 'p1',
          sira: 1,
          tutar: 600,
          kalanSonrasi: 0,
          tarih: '2026-09-03T00:00:00Z',
          makbuzNo: null,
          aciklama: null,
          islemYapan: 'u',
        },
      });
      return true;
    },
  });
  await page.goto(EXPENSES.yol);
  await hazirBekle(page, EXPENSES);
  await page.getByRole('button', { name: 'Öde', exact: true }).first().click();
  const form = page.getByRole('region', { name: /Gider Ödemesi/ });
  await form.getByRole('button', { name: 'Öde', exact: true }).click();
  await expect(form.getByRole('button', { name: /Gönderiliyor/ })).toBeVisible();
  // r300b N1: uçuştaki gönderim form kapatılarak iptal edilemez, başka satıra geçilemez.
  await expect(form.getByRole('button', { name: 'Kapat' })).toBeDisabled();
  await expect(
    page.getByRole('gridcell').getByRole('button', { name: 'Öde', exact: true }),
  ).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Yeni Gider' })).toBeDisabled();
  release();
  await expect(page.getByText(/Gider ödemesi kaydedildi \(600,00 ₺\)/)).toBeVisible();
  await expect(form.getByRole('button', { name: 'Kapat' })).toBeEnabled();
  expect(written).toHaveLength(1);
});

test('r300b B gider (USD, kur 35): belirsiz → form kapat (onaylı) / aç → kilitli form USD ve 35 kurla, aynı gövde', async ({
  page,
}) => {
  let n = 0;
  const written = await documentEndpoints(page, {
    write: async (r, path) => {
      if (path !== '/api/ui/v1/giderler') return false;
      if (++n === 1) await r.abort('failed');
      else await r.fulfill({ json: { id: 'g2', no: 'GD-000002' } });
      return true;
    },
  });
  await page.goto(EXPENSES.yol);
  await hazirBekle(page, EXPENSES);
  const toggle = page.getByRole('button', { name: 'Yeni Gider' });
  await toggle.click();
  let form = page.getByRole('region', { name: 'Yeni Gider' });
  await form.getByRole('textbox', { name: 'Net Tutar' }).fill('100');
  await form.getByRole('combobox', { name: 'Döviz' }).selectOption('USD');
  await form.getByRole('textbox', { name: 'Kur' }).fill('35');
  await form.getByRole('button', { name: 'Kaydet' }).click();
  await expect(form.getByText('İşlemin sonucu bilinmiyor')).toBeVisible();

  await toggle.click();
  const dialog = page.locator('rc-onay-diyalogu');
  await expect(dialog).toContainText('sonucu bilinmiyor');
  await dialog.getByRole('button', { name: 'Onayla' }).click();
  await expect(form).toBeHidden();
  await toggle.click();
  form = page.getByRole('region', { name: 'Yeni Gider' });
  await expect(form.getByRole('combobox', { name: 'Döviz' }).locator('option:checked')).toHaveText(
    'USD',
  );
  await expect(form.getByRole('textbox', { name: 'Kur' })).toHaveValue('35,0000');
  const net = form.getByRole('textbox', { name: 'Net Tutar' });
  await expect(net).toBeDisabled();
  // r300b N2: para simgesi form değerinden (USD), eski ₺ değil.
  await expect(form.locator('rc-para-girdisi').first()).toContainText('$');
  await form.getByRole('button', { name: 'Aynı işlemi tekrar gönder' }).click();
  await expect(page.getByText(/Gider kaydedildi/)).toBeVisible();
  expect(written).toHaveLength(2);
  expect(written[1]?.anahtar).toBe(written[0]?.anahtar);
  expect(written[1]?.govde).toBe(written[0]?.govde);
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({ doviz: 'USD', kur: '35.0000' });
});

test('r300b C araç satışı: belirsiz → tekrar → "Araç zaten satılmış" → önceki deneme yazılmış olabilir notu', async ({
  page,
}) => {
  let n = 0;
  const written = await documentEndpoints(page, {
    write: async (r, path) => {
      if (path !== '/api/ui/v1/satislar') return false;
      if (++n === 1) await r.abort('failed');
      else await problem(r, 400, 'dogrulama', 'Araç zaten satılmış.');
      return true;
    },
  });
  await page.goto(SALES.yol);
  await hazirBekle(page, SALES);
  await page.getByRole('button', { name: 'Yeni Satış' }).click();
  const form = page.getByRole('region', { name: 'Yeni Satış' });
  await form.getByRole('combobox', { name: 'Araç' }).fill('34');
  await page.getByRole('option', { name: '34ABC123' }).click();
  await form.getByRole('combobox', { name: /Alıcı/ }).fill('Ay');
  await page.getByRole('option', { name: 'Ayşe Yılmaz' }).click();
  await form.getByRole('textbox', { name: 'Satış Net' }).fill('500000');
  const dialog = page.locator('rc-onay-diyalogu');
  await form.getByRole('button', { name: 'Sat', exact: true }).click();
  await dialog.getByRole('button', { name: 'Onayla' }).click();
  await expect(form.getByText('İşlemin sonucu bilinmiyor')).toBeVisible();
  await form.getByRole('button', { name: 'Aynı işlemi tekrar gönder' }).click();
  await dialog.getByRole('button', { name: 'Onayla' }).click();
  await expect(form.getByText('Araç zaten satılmış.')).toBeVisible();
  await expect(form.getByText(/önceki denemeniz satışı yazmış olabilir/i)).toBeVisible();
  expect(written[1]?.anahtar).toBe(written[0]?.anahtar);
  expect(written[1]?.govde).toBe(written[0]?.govde);
});

test('r300 L3 şubeye bağlı kullanıcı: manuel fatura ve gelen e-fatura eylemleri yok; gider şubesi ön-dolu', async ({
  page,
}) => {
  await oturumAc(page, {
    ...BEN,
    subeKapsami: { tumSubeler: false, subeId: 's1', subeAd: 'Merkez' },
  });
  await documentEndpoints(page);
  await page.goto(INVOICES.yol);
  await hazirBekle(page, INVOICES);
  await expect(page.getByRole('region', { name: 'Manuel Fatura' })).toHaveCount(0);
  await page.goto(INCOMING.yol);
  await hazirBekle(page, INCOMING);
  await expect(page.getByRole('button', { name: 'Elle Gelen Fatura Gir' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Giderleştir', exact: true })).toHaveCount(0);
  await page.goto(EXPENSES.yol);
  await hazirBekle(page, EXPENSES);
  await page.getByRole('button', { name: 'Yeni Gider' }).click();
  await expect(
    page.getByRole('region', { name: 'Yeni Gider' }).getByRole('combobox', { name: 'Şube' }),
  ).toHaveValue('Merkez');
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
