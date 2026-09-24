import { expect, test, type Page } from '@playwright/test';

import { CARI_1, CARI_2, fixedRate, financeHubEndpoints } from './finance-fakes';
import { BEN, ciddiIhlaller, hatalariTopla, oturumAc, problem, xsrfYaz } from './ortak';
import { hazirBekle, tasmaOlc, type VitrinSayfasi } from './vitrin-sayfalari';

/**
 * F8.2a finans ekranları (PARA): kasa hub, nakit işlem, bakiye düzeltme, cari virman, tek cari / çok cari toplu,
 * toplu gider, depozito, cari ekstre, otomatik tahsilat, dönem kapanışı, kurlar. Üç zorunlu senaryo (doğrulama
 * hatasında form korunur, oturum düşünce form kaybolmaz, `cakisma` formu silmez) + para (kayıp yanıt → aynı anahtar ve
 * gövdeyle tek işlem, çift tıklama tek istek, toplu işlemde kayıp yanıt sonrası tekrar tek kayıt, mükerrer metni) +
 * axe iki tema + 320/390/768/1440 taşma.
 */
const AG_HATASI = [
  /Failed to load resource: the server responded with a status of 4\d\d/,
  /Failed to load resource: net::ERR_FAILED/,
];
const BEN_TERS = { ...BEN, izinler: [...BEN.izinler, 'FinanceReverse'] };

const page_ = (
  ad: string,
  yol: string,
  baslik: string,
  hazir?: (p: Page) => Promise<void>,
): VitrinSayfasi => ({
  ad,
  yol,
  baslik,
  ...(hazir ? { hazir } : {}),
});

const KASA = page_('kasa', '/app/kasa', 'Kasa / Banka', (p) =>
  expect(p.getByRole('cell', { name: '2026220905001' })).toBeVisible(),
);
const NAKIT = page_(
  'nakit-islem',
  `/app/finans/nakit-islem?cariId=${CARI_1}`,
  'Nakit İşlem (Tahsilat / Ödeme)',
  (p) => expect(p.getByRole('heading', { name: 'Ayşe Yılmaz' })).toBeVisible(),
);
const DUZELTME = page_(
  'bakiye-duzeltme',
  `/app/finans/bakiye-duzeltme?cariId=${CARI_1}`,
  'Bakiye Düzeltme',
  (p) => expect(p.getByRole('heading', { name: 'Ayşe Yılmaz' })).toBeVisible(),
);
const CARI_VIRMAN = page_('cari-virman', '/app/cari-virman', 'Cari ↔ Cari Virman', (p) =>
  expect(p.getByRole('cell', { name: 'Bora Kaya' })).toBeVisible(),
);
const TEK_CARI = page_(
  'tek-cari-toplu',
  `/app/tek-cari-toplu?cariId=${CARI_1}`,
  'Tek Cari — Toplu Kapatma',
  (p) => expect(p.getByRole('cell', { name: 'HGS' })).toBeVisible(),
);
const TOPLU_TAHSILAT = page_('toplu-tahsilat', '/app/toplu-tahsilat', 'Toplu Tahsilat');
const TOPLU_GIDER = page_('toplu-gider', '/app/toplu-gider', 'Toplu Gider');
const DEPOZITO = page_('depozito', '/app/depozito', 'Depozito (Emanet) İşlemleri', (p) =>
  expect(p.getByRole('link', { name: 'Ayşe Yılmaz' })).toBeVisible(),
);
const EKSTRE = page_('cari-ekstre', `/app/cariler/${CARI_1}/ekstre`, 'Ekstre — Ayşe Yılmaz', (p) =>
  expect(p.getByRole('cell', { name: 'Kira faturası' })).toBeVisible(),
);
const OTOMATIK = page_(
  'otomatik-tahsilat',
  '/app/otomatik-tahsilat',
  'Otomatik Tahsilat — Elle Çalıştır',
  (p) => expect(p.getByRole('cell', { name: '2026010901001', exact: true })).toBeVisible(),
);
const DONEM = page_('donem-kapanis', '/app/donem-kapanis', 'Dönem Kapanışı', (p) =>
  expect(p.getByText('30.06.2026')).toBeVisible(),
);
const KURLAR = page_('kurlar', '/app/kurlar', 'Döviz Kurları (TCMB)', (p) =>
  expect(p.getByRole('cell', { name: 'Euro' })).toBeVisible(),
);
const PAGES = [
  KASA,
  NAKIT,
  DUZELTME,
  CARI_VIRMAN,
  TEK_CARI,
  TOPLU_TAHSILAT,
  TOPLU_GIDER,
  DEPOZITO,
  EKSTRE,
  OTOMATIK,
  DONEM,
  KURLAR,
];

test.beforeEach(async ({ page }) => {
  await oturumAc(page, BEN_TERS);
});

test('tüm sayfalar: içerik + axe iki tema, konsol hatası yok', async ({ page }) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  await financeHubEndpoints(page);
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

test('kasa: özet kartlar sunucudan (negatif banka), virman kayıp yanıt → AYNI anahtar + gövde → sunucu aynı kimlik', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  let n = 0;
  const written = await financeHubEndpoints(page, {
    write: async (r, path) => {
      if (path !== '/api/ui/v1/finans/kasa/virman') return false;
      if (++n === 1)
        await r.abort('failed'); // sunucu YAZDI, yanıt kayboldu
      else await r.fulfill({ json: { id: 'vr-1' } }); // E06: aynı içerik → 200 aynı id
      return true;
    },
  });
  await page.goto(KASA.yol);
  await hazirBekle(page, KASA);
  await expect(page.getByText('-2.000,25 ₺')).toBeVisible();
  const form = page.getByRole('region', { name: 'Virman (Kasa ↔ Banka, hesaplar arası)' });
  await form.getByRole('textbox', { name: 'Tutar' }).fill('1.500,75');
  await form.getByRole('combobox', { name: 'Hedef hesap' }).selectOption({ label: 'Ziraat TL' });
  await form.getByRole('button', { name: 'Aktar' }).click();
  await expect(form.getByRole('alert')).toContainText('sonucu bilinmiyor');
  await expect(form.getByRole('textbox', { name: 'Tutar' })).toBeDisabled();
  // Donmuş işlem varken sekmeyi kapatmak sorulur (kopya kaybolmasın).
  await page
    .getByRole('navigation', { name: 'Açık sekmeler' })
    .getByRole('button', { name: /Kasa \/ Banka sekmesini kapat/ })
    .click();
  const leave = page.getByRole('alertdialog');
  await expect(leave).toContainText('Sonucu bilinmeyen bir para işlemi var');
  await leave.getByRole('button', { name: 'Sayfada kal' }).click();
  await form.getByRole('button', { name: 'Aynı işlemi tekrar gönder' }).click();
  await expect(page.getByText('Virman kaydedildi.')).toBeVisible();
  expect(written).toHaveLength(2);
  expect(written[1]?.anahtar).toBe(written[0]?.anahtar);
  expect(written[1]?.govde).toBe(written[0]?.govde);
  expect(JSON.parse(written[0]?.govde ?? '{}')).toEqual({
    kaynak: 'Kasa',
    hedef: 'Banka',
    tutar: '1500.75',
    kaynakHesapId: null,
    hedefHesapId: 'h2',
    doviz: 'TRY',
    makbuzNo: null,
    sube: null,
    aciklama: null,
  });
  expect(hatalar.filter((h) => !/Failed to load resource/.test(h))).toEqual([]);
});

test('nakit işlem: çift tıklamada tek tahsilat; sonraki işlem YENİ anahtar; tutar bakiye kadar önerilir', async ({
  page,
}) => {
  let paid = 0;
  const written = await financeHubEndpoints(page, {
    balance: () => (paid > 0 ? 0 : 1250.5),
    write: async (r, path) => {
      if (path !== '/api/ui/v1/finans/tahsilat') return false;
      await new Promise((ok) => setTimeout(ok, 400));
      paid++;
      await r.fulfill({ json: { id: `t-${paid}` } });
      return true;
    },
  });
  await page.goto(NAKIT.yol);
  await hazirBekle(page, NAKIT);
  const form = page.getByRole('region', { name: 'Tahsilat', exact: true });
  const amount = form.getByRole('textbox', { name: 'Tutar' });
  await expect(amount).toHaveValue('1.250,50');
  await form.getByRole('button', { name: 'Tahsilat Yap' }).dblclick();
  await expect(page.getByText('Tahsilat kaydedildi.')).toBeVisible();
  expect(written).toHaveLength(1);
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({
    cariId: CARI_1,
    tutar: '1250.50',
    hesap: 'Kasa',
  });
  await expect(amount).toHaveValue('');
  await amount.fill('100');
  await form.getByRole('button', { name: 'Tahsilat Yap' }).click();
  await expect.poll(() => written.length).toBe(2);
  expect(written[1]?.anahtar).not.toBe(written[0]?.anahtar);
});

test('nakit işlem: döviz değişince eski kur temizlenir; TRY kur göndermez (#291 M2 dersi)', async ({
  page,
}) => {
  const written = await financeHubEndpoints(page);
  await page.goto(NAKIT.yol);
  await hazirBekle(page, NAKIT);
  const form = page.getByRole('region', { name: 'Ödeme (tediye)' });
  await form.getByRole('textbox', { name: 'Tutar' }).fill('50');
  await form.getByRole('combobox', { name: 'Döviz' }).selectOption('EUR');
  await form.getByRole('textbox', { name: 'Kur' }).fill('36,5');
  await form.getByRole('combobox', { name: 'Döviz' }).selectOption('USD');
  await expect(form.getByRole('textbox', { name: 'Kur' })).toHaveValue('');
  await form.getByRole('button', { name: 'Ödeme Yap' }).click();
  await expect(page.getByText('Ödeme kaydedildi.')).toBeVisible();
  const body = JSON.parse(written[0]?.govde ?? '{}') as Record<string, unknown>;
  expect(body).toMatchObject({ doviz: 'USD', tutar: '50.00' });
  expect('kur' in body).toBe(false);
});

test('toplu tahsilat: kayıp yanıt → tekrar AYNI satırlarla → 409 mükerrer (aynı içerik) → ikinci parti yazılmaz', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  let n = 0;
  const written = await financeHubEndpoints(page, {
    write: async (r, path) => {
      if (path !== '/api/ui/v1/finans/toplu-tahsilat') return false;
      if (++n === 1) await r.abort('failed');
      else
        await problem(
          r,
          409,
          'mukerrer',
          'Bu toplu tahsilat zaten kaydedildi (No TH-1, 2 satır).',
          {
            mevcut: { id: 't1', belgeNo: 'TH-1', tutar: 1500.5, doviz: 'TRY', ayniIcerik: true },
          },
        );
      return true;
    },
  });
  await page.goto(TOPLU_TAHSILAT.yol);
  await hazirBekle(page, TOPLU_TAHSILAT);
  const row1 = page.getByRole('group', { name: 'Satır 1' });
  await row1.getByRole('combobox', { name: 'Cari' }).fill('Ay');
  await page.getByRole('option', { name: 'Ayşe Yılmaz' }).click();
  await row1.getByRole('textbox', { name: 'Tutar' }).fill('1.000,50');
  await page.getByRole('button', { name: 'Satır Ekle' }).click();
  const row2 = page.getByRole('group', { name: 'Satır 2' });
  await row2.getByRole('combobox', { name: 'Cari' }).fill('Bo');
  await page.getByRole('option', { name: 'Bora Kaya' }).click();
  await row2.getByRole('textbox', { name: 'Tutar' }).fill('500');
  await page.getByRole('button', { name: 'Toplu Tahsilat Yap' }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'sonucu bilinmiyor' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Satır Ekle' })).toBeDisabled();
  await page.getByRole('button', { name: 'Aynı işlemi tekrar gönder' }).click();
  await expect(page.getByText('İşlem zaten kaydedildi')).toBeVisible();
  await expect(
    page.getByText('Bu toplu tahsilat zaten kaydedildi (No TH-1, 2 satır).'),
  ).toBeVisible();
  // Mükerrer metni kullanıcıyı ikinci işleme yönlendirmez; form temizlenir, tekrar düğmesi kalkar.
  await expect(page.getByRole('button', { name: 'Aynı işlemi tekrar gönder' })).toHaveCount(0);
  await expect(page.getByRole('group', { name: 'Satır 2' })).toHaveCount(0);
  expect(written).toHaveLength(2);
  expect(written[1]?.anahtar).toBe(written[0]?.anahtar);
  expect(written[1]?.govde).toBe(written[0]?.govde);
  expect(JSON.parse(written[0]?.govde ?? '{}')).toEqual({
    satirlar: [
      { cariId: CARI_1, tutar: '1000.50', aciklama: null },
      { cariId: 'c0c0c0c0-0000-4000-8000-000000000002', tutar: '500.00', aciklama: null },
    ],
    hesap: 'Kasa',
    hesapId: null,
    kanal: 'Masaüstü',
  });
  expect(hatalar.filter((h) => !/Failed to load resource/.test(h))).toEqual([]);
});

test('bakiye düzeltme: dönem kilidi alan hatası → form KORUNUR, aynı anahtarla düzeltilip gönderilir; onaylı', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  let n = 0;
  const written = await financeHubEndpoints(page, {
    write: async (r, path) => {
      if (path !== '/api/ui/v1/finans/bakiye-duzeltme') return false;
      if (++n === 1)
        await problem(r, 400, 'dogrulama', 'Doğrulama hatası.', {
          errors: { tarih: ['Dönem 30.06.2026 tarihine kadar KAPALI; bu tarihe kayıt atılamaz.'] },
        });
      else await r.fulfill({ json: { id: 'bd-1' } });
      return true;
    },
  });
  await page.goto(DUZELTME.yol);
  await hazirBekle(page, DUZELTME);
  await page
    .getByRole('combobox', { name: 'Yön' })
    .selectOption({ label: 'Borçlandır (borcu artır)' });
  const amount = page.getByRole('textbox', { name: 'Tutar' });
  await amount.fill('10,555');
  await page.getByRole('button', { name: 'Düzeltmeyi Kaydet' }).click();
  await expect(page.getByText('En fazla 2 ondalık hane girilebilir.')).toBeVisible();
  expect(written).toHaveLength(0);
  await amount.fill('12,40');
  await page.getByRole('textbox', { name: 'Tarih' }).fill('15.06.2026');
  await page.getByRole('button', { name: 'Düzeltmeyi Kaydet' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Onayla' }).click();
  await expect(page.getByText('bu tarihe kayıt atılamaz')).toBeVisible();
  await expect(amount).toHaveValue('12,40');
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({
    cariId: CARI_1,
    yon: 'Borclandir',
    tutar: '12.40',
    tarih: '2026-06-14T21:00:00.000Z',
  });
  await page.getByRole('textbox', { name: 'Tarih' }).fill('');
  await page.getByRole('button', { name: 'Düzeltmeyi Kaydet' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Onayla' }).click();
  await expect(page.getByText('Düzeltme kaydedildi.')).toBeVisible();
  expect(written[1]?.anahtar).toBe(written[0]?.anahtar); // ilk istek yazılmadı: aynı işlem
  expect(hatalar).toEqual([]);
});

test('cari virman: oturum düşünce form kaybolmaz — yerinde giriş, AYNI istek (aynı anahtar)', async ({
  page,
}) => {
  let n = 0;
  const written = await financeHubEndpoints(page, {
    write: async (r, path) => {
      if (path !== '/api/ui/v1/finans/cari-virman') return false;
      if (++n === 1) await problem(r, 401, 'oturum_yok', 'Oturum açık değil.');
      else await r.fulfill({ json: { id: 'cv-2' } });
      return true;
    },
  });
  await page.route('**/api/ui/v1/oturum/xsrf', async (route) => {
    await xsrfYaz(page, 'anonim-belirtec');
    return route.fulfill({ status: 204 });
  });
  await page.route('**/api/ui/v1/oturum/giris', async (route) => {
    await xsrfYaz(page, 'yeni-belirtec');
    return route.fulfill({ json: BEN_TERS });
  });
  await page.goto(CARI_VIRMAN.yol);
  await hazirBekle(page, CARI_VIRMAN);
  const form = page.getByRole('region', { name: 'Virman', exact: true });
  await form.getByRole('combobox', { name: 'Kaynak Cari (alacak)' }).fill('Ay');
  await page.getByRole('option', { name: 'Ayşe Yılmaz' }).click();
  await form.getByRole('combobox', { name: 'Hedef Cari (borç)' }).fill('Bo');
  await page.getByRole('option', { name: 'Bora Kaya' }).click();
  await form.getByRole('textbox', { name: 'Tutar' }).fill('300');
  await form.getByRole('button', { name: 'Virman Yap' }).click();
  const dialog = page.getByRole('dialog', { name: 'Oturumunuz sona erdi' });
  await expect(dialog).toBeVisible();
  await expect(page.getByRole('textbox', { name: 'Tutar', includeHidden: true })).toHaveValue(
    '300,00',
  );
  await dialog.getByLabel('Parola').fill('rastgele-e2e-parolasi');
  await dialog.getByRole('button', { name: 'Giriş yap ve devam et' }).click();
  await expect(page.getByText('Virman kaydedildi.')).toBeVisible();
  expect(written).toHaveLength(2);
  expect(written[1]?.govde).toBe(written[0]?.govde);
  expect(written[1]?.anahtar).toBe(written[0]?.anahtar);
  expect(JSON.parse(written[0]?.govde ?? '{}')).toMatchObject({
    kaynakCariId: CARI_1,
    hedefCariId: 'c0c0c0c0-0000-4000-8000-000000000002',
    tutar: '300.00',
  });
});

test('kurlar: sabit kur PUT cakisma formu silmez — güncel kayıt birleşir, sonraki PUT yeni sürümle', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  let surum = 'sk-1';
  let bitTar: string | null = null;
  let put = 0;
  const written = await financeHubEndpoints(page, {
    fixed: () => fixedRate({ surum, bitTar }),
    write: async (r) => {
      if (r.request().method() !== 'PUT') return false;
      if (++put === 1) {
        surum = 'sk-2';
        bitTar = '2026-12-31'; // başka oturum bitiş tarihi girdi
        await problem(r, 409, 'cakisma', 'Kayıt siz düzenlerken değişti; güncel hâli yükleyin.');
      } else await r.fulfill({ status: 204 });
      return true;
    },
  });
  await page.goto(KURLAR.yol);
  await hazirBekle(page, KURLAR);
  await page.getByRole('button', { name: 'Düzenle' }).click();
  const kur = page.getByRole('textbox', { name: 'Sabit Kur (TL / 1 birim)' });
  await kur.fill('37,25');
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.locator('rc-uyari-bandi')).toContainText('Kayıt siz düzenlerken değişti');
  await expect(kur).toHaveValue('37,250000'); // form SİLİNMEDİ
  await expect(page.getByRole('textbox', { name: 'Bitiş (ops.)' })).toHaveValue('31.12.2026');
  expect(JSON.parse(written[0]?.govde ?? '{}')).toEqual({
    kur: 37.25,
    basTar: null,
    bitTar: null,
    aktif: true,
    surum: 'sk-1',
  });
  await page.getByRole('button', { name: 'Kaydet' }).click();
  await expect(page.getByText('Sabit kur kaydedildi.')).toBeVisible();
  expect(JSON.parse(written[1]?.govde ?? '{}')).toEqual({
    kur: 37.25,
    basTar: null,
    bitTar: '2026-12-31',
    aktif: true,
    surum: 'sk-2',
  });
  expect(hatalar).toEqual([]);
});

test('dönem kapanışı: kilitle ve kilit kaldır ONAYLI; vazgeçilirse istek gitmez', async ({
  page,
}) => {
  const written = await financeHubEndpoints(page, {
    write: async (r) => {
      await r.fulfill({ status: 204 });
      return true;
    },
  });
  await page.goto(DONEM.yol);
  await hazirBekle(page, DONEM);
  await page.getByRole('textbox', { name: 'Kapanış Tarihi' }).fill('31.08.2026');
  await page.getByRole('button', { name: 'Dönemi Kapat (fiş + kilit)' }).click();
  const dialog = page.getByRole('alertdialog');
  await expect(dialog).toContainText('Kilidi kaldırmak kapanış fişini geri almaz');
  await dialog.getByRole('button', { name: 'Vazgeç' }).click();
  expect(written).toHaveLength(0);
  await page.getByRole('button', { name: 'Dönemi Kapat (fiş + kilit)' }).click();
  await page
    .getByRole('alertdialog')
    .getByRole('button', { name: 'Dönemi Kapat (fiş + kilit)' })
    .click();
  await expect(page.getByText('Dönem kapatıldı.')).toBeVisible();
  expect(JSON.parse(written[0]?.govde ?? '{}')).toEqual({ kapanisTarihi: '2026-08-31' });
  expect(written[0]?.anahtar).toBeUndefined(); // E36 yapısal: işlem anahtarı yok
});

// ---------------------------------------------------------------- r299 bağımsız inceleme düzeltmeleri

test('r299 HIGH-1: kayıp yanıt → tekrar → mevcut SUZ 409: "daha önce kaydedildi", "yazılmadı/değişti" denmez', async ({
  page,
}) => {
  let n = 0;
  const written = await financeHubEndpoints(page, {
    write: async (r, path) => {
      if (path !== '/api/ui/v1/finans/tahsilat') return false;
      if (++n === 1)
        await r.abort('failed'); // sunucu YAZDI, yanıt kayboldu
      else
        await problem(r, 409, 'mukerrer', 'Bu işlem zaten kaydedilmiş (çift gönderim / mükerrer).');
      return true;
    },
  });
  await page.goto(NAKIT.yol);
  await hazirBekle(page, NAKIT);
  const form = page.getByRole('region', { name: 'Tahsilat', exact: true });
  await form.getByRole('button', { name: 'Tahsilat Yap' }).click();
  await form.getByRole('button', { name: 'Aynı işlemi tekrar gönder' }).click();
  await expect(page.getByText('İşlem daha önce kaydedildi')).toBeVisible();
  await expect(
    page.getByText(
      'Bu işlem daha önce kaydedildi; ikinci kez yazılmadı. Hareketleri kontrol edin.',
    ),
  ).toBeVisible();
  await expect(page.getByText('Kayıt değişmiş')).toHaveCount(0);
  await expect(page.getByText('işleminiz yazılmadı')).toHaveCount(0);
  expect(written).toHaveLength(2);
  expect(written[1]?.anahtar).toBe(written[0]?.anahtar);
});

test('r299 MEDIUM-2: TRY bakiye önerisi dövize geçince temizlenir; USD tutarı olarak GİTMEZ', async ({
  page,
}) => {
  const written = await financeHubEndpoints(page);
  await page.goto(NAKIT.yol);
  await hazirBekle(page, NAKIT);
  const form = page.getByRole('region', { name: 'Tahsilat', exact: true });
  const amount = form.getByRole('textbox', { name: 'Tutar' });
  await expect(amount).toHaveValue('1.250,50');
  await form.getByRole('combobox', { name: 'Döviz' }).selectOption('USD');
  await expect(amount).toHaveValue('');
  await form.getByRole('button', { name: 'Tahsilat Yap' }).click();
  expect(written).toHaveLength(0); // tutar boş: istemci doğrulaması
  await form.getByRole('combobox', { name: 'Döviz' }).selectOption('TRY');
  await expect(amount).toHaveValue('1.250,50');
});

test('r299 MEDIUM-1: istek uçarken form ve satır ekle/sil kilitli; satır hatası doğru satırda', async ({
  page,
}) => {
  await financeHubEndpoints(page, {
    write: async (r, path) => {
      if (path !== '/api/ui/v1/finans/toplu-tahsilat') return false;
      await new Promise((ok) => setTimeout(ok, 1000));
      await problem(r, 400, 'dogrulama', 'Tutar çok büyük.', {
        errors: { 'satirlar[1].tutar': ['Satır 2 tutarı çok büyük.'] },
      });
      return true;
    },
  });
  await page.goto(TOPLU_TAHSILAT.yol);
  await hazirBekle(page, TOPLU_TAHSILAT);
  const row = (i: number) => page.getByRole('group', { name: `Satır ${i}` });
  await row(1).getByRole('combobox', { name: 'Cari' }).fill('Ay');
  await page.getByRole('option', { name: 'Ayşe Yılmaz' }).click();
  await row(1).getByRole('textbox', { name: 'Tutar' }).fill('100');
  await page.getByRole('button', { name: 'Satır Ekle' }).click();
  await row(2).getByRole('combobox', { name: 'Cari' }).fill('Bo');
  await page.getByRole('option', { name: 'Bora Kaya' }).click();
  await row(2).getByRole('textbox', { name: 'Tutar' }).fill('999999');
  await page.getByRole('button', { name: 'Toplu Tahsilat Yap' }).click();
  await expect(page.getByRole('button', { name: 'Satır Ekle' })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Satır 1 sil' })).toBeDisabled();
  await expect(row(1).getByRole('textbox', { name: 'Tutar' })).toBeDisabled();
  await expect(row(2).getByText('Satır 2 tutarı çok büyük.')).toBeVisible();
  await expect(row(1).getByText('Satır 2 tutarı çok büyük.')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Satır Ekle' })).toBeEnabled();
});

test('r299 LOW-1: dönem kapanışı ve ekstre ters kayıt çift tık → tek onay, tek POST', async ({
  page,
}) => {
  const written = await financeHubEndpoints(page, {
    write: async (r) => {
      await r.fulfill({ status: r.request().method() === 'POST' ? 204 : 200 });
      return true;
    },
  });
  await page.goto(DONEM.yol);
  await hazirBekle(page, DONEM);
  await page.getByRole('textbox', { name: 'Kapanış Tarihi' }).fill('31.08.2026');
  await page.getByRole('button', { name: 'Dönemi Kapat (fiş + kilit)' }).dblclick();
  await expect(page.getByRole('alertdialog')).toHaveCount(1);
  await page
    .getByRole('alertdialog')
    .getByRole('button', { name: 'Dönemi Kapat (fiş + kilit)' })
    .click();
  await expect(page.getByText('Dönem kapatıldı.')).toBeVisible();
  expect(written).toHaveLength(1);

  await page.goto(EKSTRE.yol);
  await hazirBekle(page, EKSTRE);
  await page.getByRole('button', { name: 'Ters Kayıt' }).first().dblclick();
  await expect(page.getByRole('alertdialog')).toHaveCount(1);
  await page.getByRole('alertdialog').getByRole('button', { name: 'Ters Kayıt' }).click();
  await expect(page.getByText('Ters kayıt alındı.')).toBeVisible();
  expect(written).toHaveLength(2);
});

test('r299 LOW-3: aynı sekmede ?cariId= değişince sayfa yeni cariye geçer', async ({ page }) => {
  await financeHubEndpoints(page);
  await page.goto(NAKIT.yol);
  await hazirBekle(page, NAKIT);
  await page.evaluate((url) => {
    history.pushState({}, '', url);
    dispatchEvent(new PopStateEvent('popstate', { state: {} }));
  }, `/app/finans/nakit-islem?cariId=${CARI_2}`);
  await expect(page.getByRole('heading', { name: 'Bora Kaya' })).toBeVisible();
  await expect(page.getByRole('combobox', { name: 'Cari', exact: true })).toHaveValue('Bora Kaya');
  await expect(
    page
      .getByRole('region', { name: 'Tahsilat', exact: true })
      .getByRole('textbox', { name: 'Tutar' }),
  ).toHaveValue(''); // alacaklı: öneri yok
});

for (const s of PAGES) {
  test.describe(`${s.ad}: mobil taşma (dokunmatik öykünme)`, () => {
    test.use({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });
    test(`${s.yol}: 320/390/768 px gövde yatay taşması yok`, async ({ page }) => {
      await financeHubEndpoints(page);
      for (const width of [320, 390, 768]) {
        await page.setViewportSize({ width, height: 844 });
        await page.goto(s.yol);
        await hazirBekle(page, s);
        expect(await tasmaOlc(page), `${width}px`).toEqual({ tasma: 0, suclular: [] });
      }
    });
  });
  test(`${s.yol}: 1440 px gövde yatay taşması yok`, async ({ page }) => {
    await financeHubEndpoints(page);
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(s.yol);
    await hazirBekle(page, s);
    expect(await tasmaOlc(page)).toEqual({ tasma: 0, suclular: [] });
  });
}
