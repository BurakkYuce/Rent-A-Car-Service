import { expect, test, type Page, type Request, type Route } from '@playwright/test';

import { BEN, ciddiIhlaller, hatalariTopla, oturumAc, problem, xsrfYaz } from './ortak';
import { tasmaOlc } from './vitrin-sayfalari';

/**
 * F4.3 kira formu (sahte `/api/ui/v1`, üretim derlemesi + CSP). Faz çıkışı e2e'leri: "doğrulama
 * hatasında form korunur", "oturum düşünce form kaybolmaz", "müsaitlik `cakisma` formu silmez",
 * "`?varac=` dolu form açar", "`#sekme=` doğru sekmeyi açar" + ek hizmet çift tık + PUT 58 alan.
 * Beklenen değerler sahte yanıtlardan ELLE kurulur (formül yok).
 */
const AG_HATASI = [/Failed to load resource: the server responded with a status of 4\d\d/];

const KIRA_ID = '0b0e7c1a-1111-4aaa-8bbb-000000000001';
const MUSTERI_ID = '0b0e7c1a-2222-4aaa-8bbb-000000000002';
const ARAC_ID = '0b0e7c1a-3333-4aaa-8bbb-000000000003';
const TANIM_ID = '0b0e7c1a-6666-4aaa-8bbb-000000000006';

const YENI = `/app/kiralar/yeni?varac=${ARAC_ID}&vfrom=2026-10-01&vto=2026-10-04&musteriId=${MUSTERI_ID}`;

const MUSAIT = [
  {
    id: ARAC_ID,
    plaka: '34 ABC 123',
    marka: 'Fiat',
    tip: 'Egea',
    modelYili: 2024,
    vites: 'Manuel',
    yakit: 'Dizel',
    grup: 'C',
    segment: 'Orta',
    km: 12000,
    sube: 'Merkez',
    konum: 'Otopark',
  },
];

const HESAP = {
  ok: true,
  hata: null,
  gun: 3,
  gunlukUcret: 1200,
  tutar: 3600,
  net: 3000,
  kdv: 600,
  hediyeGun: null,
  iskontoTutar: null,
  haftaSonuFark: null,
  faturalananGun: null,
  ekKalemler: [],
  ekHizmetToplam: 0,
  genelToplam: 3600,
  doviz: 'TRY',
  kur: null,
  genelToplamTl: null,
  tahsilat: null,
  kalan: 3600,
  notlar: null,
};

const KIRA = {
  id: KIRA_ID,
  sozlesmeNo: '2026220901001',
  durum: 'Kirada',
  reservationId: null,
  musteriId: MUSTERI_ID,
  vehicleId: ARAC_ID,
  basTar: '2026-09-22T06:00:00+00:00',
  bitTar: '2026-09-25T06:00:00+00:00',
  cikisOfisi: 'Merkez',
  cikisSubeId: null,
  donusOfisi: 'Merkez',
  kmLimit: 300,
  fazlaKmUcret: 2.5,
  yakitBirimUcret: 45,
  cikisKm: 12000,
  cikisYakit: 8,
  donusKm: null,
  donusYakit: null,
  gercekDonusTar: null,
  fazlaKm: 0,
  fazlaKmBedeli: 0,
  eksikYakit: 0,
  yakitBedeli: 0,
  uzatmaGun: 0,
  uzatmaBedeli: 0,
  kmHediye: null,
  bitisSebebi: null,
  teslimAlanPersonelId: null,
  teslimEdenPersonelId: null,
  odemeSekli: 'Nakit',
  ikinciSurucuId: null,
  ikinciSurucuSerbestAd: null,
  ikinciSurucuSerbestSoyad: null,
  ikinciSurucuSerbestTel: null,
  ikinciSurucuSerbestEhliyetSinifi: null,
  hediyeGun: null,
  faturalananGun: null,
  vadeTar: null,
  iskontoTutar: null,
  haftaSonuFark: null,
  gun: 3,
  gunlukUcret: 1200,
  tutar: 3600,
  genelToplam: 3600,
  tahsilat: 1000,
  bakiye: 2600,
  provizyon: null,
  depozito: null,
  komisyonOran: null,
  komisyonTutar: null,
  dropUcreti: null,
  sonraOdeOran: null,
  aciklama: null,
  kaynak: null,
  kampanyaKodu: null,
  uyariAciklama: null,
  ozelFaturaAciklama: null,
  faturaListesindeGizle: null,
  ucusNo: null,
  provizyonNo: null,
  provizyonTarih: null,
  provizyonDurum: 'Yok',
  provizyonKapamaTarih: null,
  provizyonKapamaTutar: null,
  onayKodu: null,
  firmaKodu: null,
  projeAdi: null,
  ozelKod: null,
  talepTuru: null,
  geldigiBirim: null,
  kefilBilgisi: null,
  assistFirma: null,
  ozelSoforBilgisi: null,
  ekKosullar: null,
  belgeSablonId: null,
  opsiyonNet: null,
  opsiyonGun: null,
  riskOnay: false,
  manuelFindexPuan: null,
  kabisCikis: null,
  kabisDonus: null,
  otomatikUzat: null,
  aksYedekAnahtarCikis: null,
  aksYedekAnahtarDonus: null,
  aksStepneCikis: null,
  aksStepneDonus: null,
  aksZincirCikis: null,
  aksZincirDonus: null,
  aksIlkYardimCikis: null,
  aksIlkYardimDonus: null,
  aksLastikCikis: null,
  aksLastikDonus: null,
  kiralamaTuru: null,
  faturalamaTipi: null,
  fiyatTuru: 'KDV Dahil Günlük',
  doviz: 'TL',
  kurSnapshot: 1,
  donemselFaturalama: false,
  kdvOranSnapshot: 0.2,
  ozelKdvOran: null,
  damgaVergisi: null,
  createdAtUtc: '2026-09-22T06:00:00+00:00',
  updatedAtUtc: null,
};

const DETAY = {
  kira: KIRA,
  musteri: { id: MUSTERI_ID, ad: 'Ayşe Yılmaz' },
  ikinciSurucu: null,
  arac: { ...MUSAIT[0] },
  islemSubeAdi: 'Merkez Şube',
  teslimAlanPersonelAd: null,
  teslimEdenPersonelAd: null,
  ekHizmetler: [],
  doviz: null,
  paylasim: null,
  yetkiler: { operasyon: true, silme: true, finans: true },
};

interface Sahte {
  /** Kira yazma uçları (POST/PUT/DELETE) — test karar verir. */
  yazma?: (route: Route, istek: Request) => Promise<unknown> | unknown;
}

/** Tek işleyici: `/api/ui/v1/kiralar/**` + seçim uçları (yöntem + yola göre). */
async function sahteKiraApi(page: Page, { yazma }: Sahte = {}): Promise<string[]> {
  const hesapSorgulari: string[] = [];
  await page.route(/\/api\/ui\/v1\/secim\//, (route) =>
    route.fulfill({
      json: route.request().url().includes('/secim/ek-hizmet')
        ? [{ id: TANIM_ID, etiket: 'Bebek koltuğu', kod: 'BEBEK' }]
        : [],
    }),
  );
  await page.route(/\/api\/ui\/v1\/kiralar(\/|\?|$)/, async (route) => {
    const istek = route.request();
    const url = new URL(istek.url());
    const yol = url.pathname.replace('/api/ui/v1/kiralar', '');
    if (istek.method() !== 'GET')
      return (yazma ?? ((r) => r.fulfill({ status: 500 })))(route, istek);
    if (yol === '/form-varsayilanlari') {
      return route.fulfill({
        json: {
          cikisYakit: 8,
          fiyatTuru: null,
          fiyatTurleri: ['Otomatik', 'KDV Dahil Günlük'],
          kiralamaTurleri: ['Kısa Kiralama'],
          faturalamaTipleri: ['Müşteri Ödemeli'],
          dovizler: ['TL', 'EURO', 'USD'],
          odemeSekilleri: ['Nakit'],
          basTarEnGec: '2027-09-22T00:00:00Z',
        },
      });
    }
    if (yol === '/musait-arac') return route.fulfill({ json: MUSAIT });
    if (yol === '/hesapla') {
      hesapSorgulari.push(url.search);
      return route.fulfill({ json: HESAP });
    }
    if (yol === `/${KIRA_ID}`) return route.fulfill({ json: DETAY });
    if (yol === `/${KIRA_ID}/karne-ozeti`) {
      return route.fulfill({ json: { vehicleId: ARAC_ID, dolulukYuzde: 61.5 } });
    }
    if (yol.startsWith(`/${KIRA_ID}/donus-hesapla`)) {
      return route.fulfill({
        json: {
          ok: true,
          hata: null,
          kullanilanKm: 0,
          fazlaKm: 0,
          fazlaKmBedeli: 0,
          eksikYakit: 0,
          yakitBedeli: 0,
          uzatmaGun: 0,
          uzatmaBedeli: 0,
          ekHizmetToplam: 0,
          yeniGenelToplam: 3600,
          kalan: 2600,
        },
      });
    }
    return route.fulfill({ status: 404, json: { status: 404, detail: 'yok' } });
  });
  return hesapSorgulari;
}

/** Hızlı Giriş paneli (CSS ile: diyalog açıkken arka plan `aria-hidden`, rol sorgusu onu görmez). */
const hizli = (page: Page) => page.locator('[data-rc-sekme="hizli"]');

async function formHazir(page: Page): Promise<void> {
  await expect(hizli(page).getByLabel('Araç', { exact: true })).toHaveValue(
    '34 ABC 123 — Fiat Egea',
  );
  await expect(page.getByTestId('canli-hesap').first()).toBeVisible();
}

async function formKorunduMu(page: Page): Promise<void> {
  await expect(page).toHaveURL(/\/app\/kiralar\/yeni\?varac=/);
  await expect(hizli(page).getByLabel('Araç', { exact: true })).toHaveValue(
    '34 ABC 123 — Fiat Egea',
  );
  // Odaktaki para girdisi düzenleme yazımını (gruplamasız) gösterir; değer aynıdır.
  await expect(hizli(page).getByLabel('Günlük ücret / toplam')).toHaveValue(/^1\.?250,50$/);
  await expect(hizli(page).getByLabel('Rezervasyon kaynağı')).toHaveValue('Web sitesi');
}

async function doldur(page: Page): Promise<void> {
  await hizli(page).getByLabel('Günlük ücret / toplam').fill('1250,50');
  await hizli(page).getByLabel('Rezervasyon kaynağı').fill('Web sitesi');
  await hizli(page).getByLabel('Rezervasyon kaynağı').blur();
}

test.beforeEach(async ({ page }) => oturumAc(page));

test('?varac&vfrom&vto&musteriId dolu form açar; canlı hesap sunucudan (UI formül taşımaz)', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page);
  const hesap = await sahteKiraApi(page);
  await page.goto(YENI);
  await formHazir(page);

  const panel = hizli(page);
  await expect(panel.getByLabel('Müşteri', { exact: true })).toHaveValue('Bağlantıdaki müşteri');
  await expect(panel.getByLabel('Başlangıç', { exact: true })).toHaveValue('01.10.2026');
  await expect(panel.getByLabel('Bitiş (beklenen)', { exact: true })).toHaveValue('04.10.2026');
  await expect(panel.getByRole('textbox', { name: 'Saat' }).first()).toHaveValue('09:00');
  // Özet sunucu yanıtının biçimi: 3.600,00 ₺ (hesap yok).
  await expect(panel.getByTestId('canli-hesap')).toContainText('3.600,00');
  await expect(page.getByTestId('yan-ozet')).toContainText('3.600,00');
  await expect.poll(() => hesap.at(-1) ?? '').toContain(`vehicleId=${ARAC_ID}`);
  const son = new URLSearchParams(hesap.at(-1));
  expect(son.get('basTar')).toBe('2026-10-01T06:00:00.000Z');
  expect(son.get('musteriId')).toBe(MUSTERI_ID);

  // Araç sekmesi: müsait liste + seçili satır.
  await page.getByRole('tab', { name: 'Araç' }).click();
  await expect(page).toHaveURL(/#sekme=arac$/);
  await expect(page.getByTestId('musait-notu')).toContainText('1 araç müsait');
  expect(await ciddiIhlaller(page)).toEqual([]);
  expect(hatalar).toEqual([]);
});

test('#sekme= doğru sekmeyi açar (yeni ve kayıtlı kira; Ayrıntılar alt sekmesi dahil)', async ({
  page,
}) => {
  await sahteKiraApi(page);
  await page.goto(`${YENI}#sekme=fiyat`);
  await expect(page.getByRole('tab', { name: 'Fiyat/Toplam' })).toHaveAttribute(
    'aria-selected',
    'true',
  );
  await expect(page.getByRole('tabpanel', { name: 'Fiyat/Toplam' })).toBeVisible();
  await expect(page.getByRole('tabpanel', { name: 'Hızlı Giriş' })).toHaveCount(0);

  await page.goto(`/app/kiralar/${KIRA_ID}#sekme=ayrintilar&alt=aksesuar`);
  await expect(page.getByRole('heading', { level: 1 })).toContainText('Kira 2026220901001');
  await expect(page.getByRole('tab', { name: 'Ayrıntılar' })).toHaveAttribute(
    'aria-selected',
    'true',
  );
  await expect(page.getByRole('tab', { name: 'Aksesuar' })).toHaveAttribute(
    'aria-selected',
    'true',
  );
  await expect(page.getByRole('table', { name: 'Aksesuar' })).toBeVisible();
  expect(await ciddiIhlaller(page)).toEqual([]);
});

test('doğrulama hatasında form korunur: alan işaretlenir, gezinme yok', async ({ page }) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  await sahteKiraApi(page, {
    yazma: (route) =>
      problem(route, 400, 'dogrulama', 'Günlük ücret negatif olamaz.', {
        errors: { gunlukUcret: ['Günlük ücret negatif olamaz.'] },
      }),
  });
  await page.goto(YENI);
  await formHazir(page);
  await doldur(page);
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();

  const alan = hizli(page).getByLabel('Günlük ücret / toplam');
  await expect(alan).toHaveAttribute('aria-invalid', 'true');
  await expect(hizli(page)).toContainText('Günlük ücret negatif olamaz.');
  await formKorunduMu(page);
  expect(hatalar).toEqual([]);
});

test('oturum düşünce form kaybolmaz: yerinde giriş → AYNI istek tekrarlanır → kayıt açılır', async ({
  page,
}) => {
  const govdeler: (string | null)[] = [];
  await sahteKiraApi(page, {
    yazma: (route, istek) => {
      govdeler.push(istek.postData());
      if (govdeler.length === 1) return problem(route, 401, 'oturum_yok', 'Oturum açık değil.');
      return route.fulfill({
        status: 201,
        json: { id: KIRA_ID, sozlesmeNo: '2026220901001', uyari: null },
      });
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

  await page.goto(YENI);
  await formHazir(page);
  await doldur(page);
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();

  const diyalog = page.getByRole('dialog', { name: 'Oturumunuz sona erdi' });
  await expect(diyalog).toBeVisible();
  await formKorunduMu(page);
  await diyalog.getByLabel('Parola').fill('rastgele-e2e-parolasi');
  await diyalog.getByRole('button', { name: 'Giriş yap ve devam et' }).click();

  await expect(page).toHaveURL(new RegExp(`/app/kiralar/${KIRA_ID}$`));
  await expect(page.getByRole('heading', { level: 1 })).toContainText('Kira 2026220901001');
  expect(govdeler).toHaveLength(2);
  expect(govdeler[1]).toBe(govdeler[0]);
  const govde = JSON.parse(govdeler[0] ?? '{}') as Record<string, unknown>;
  expect(govde).toMatchObject({
    musteriId: MUSTERI_ID,
    vehicleId: ARAC_ID,
    basTar: '2026-10-01T06:00:00.000Z',
    bitTar: '2026-10-04T06:00:00.000Z',
    gunlukUcret: '1250.50',
    kaynak: 'Web sitesi',
  });
});

test('müsaitlik `cakisma` formu silmez: uyarı bandı + form üstü mesaj, değerler yerinde', async ({
  page,
}) => {
  await sahteKiraApi(page, {
    yazma: (route) => problem(route, 409, 'cakisma', 'Araç bu tarihlerde müsait değil.'),
  });
  await page.goto(YENI);
  await formHazir(page);
  await doldur(page);
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();

  await expect(page.locator('rc-uyari-bandi')).toContainText('Araç bu tarihlerde müsait değil.');
  await expect(page.locator('.rc-form-hatalari')).toContainText('Araç bu tarihlerde müsait değil.');
  await formKorunduMu(page);
});

test('kayıtlı kira: PUT 58 alanın hepsini taşır; ek hizmet çift tık TEK kalem yazar', async ({
  page,
}) => {
  const putlar: Record<string, unknown>[] = [];
  let ekHizmetIstegi = 0;
  await sahteKiraApi(page, {
    yazma: async (route, istek) => {
      if (istek.method() === 'PUT') {
        putlar.push(istek.postDataJSON() as Record<string, unknown>);
        return route.fulfill({ json: KIRA });
      }
      if (istek.url().endsWith('/ek-hizmetler')) {
        ekHizmetIstegi++;
        await new Promise((r) => setTimeout(r, 400)); // istek sürerken ikinci tık
        return route.fulfill({ json: { kalemler: [], kira: KIRA } });
      }
      return route.fulfill({ status: 500 });
    },
  });
  await page.goto(`/app/kiralar/${KIRA_ID}#sekme=ayrintilar`);
  const aciklama = page
    .getByRole('tabpanel', { name: 'Ayrıntılar' })
    .getByRole('textbox', { name: 'Açıklama', exact: true });
  await aciklama.fill('Müşteri erken gelecek');
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Kira kaydedildi.' })).toBeVisible();
  expect(putlar).toHaveLength(1);
  expect(Object.keys(putlar[0] ?? {})).toHaveLength(58);
  expect(putlar[0]).toMatchObject({
    aciklama: 'Müşteri erken gelecek',
    kmLimit: 300,
    fazlaKmUcret: 2.5,
    cikisOfisi: 'Merkez',
    odemeSekli: 'Nakit',
  });

  await page.getByRole('tab', { name: 'Ek Hizmetler' }).click();
  const ekle = page.getByTestId('ek-hizmet-ekle');
  await ekle.getByRole('combobox').click();
  await page.getByRole('option', { name: /Bebek koltuğu/ }).click();
  await ekle.getByRole('button', { name: 'Ekle' }).dblclick();
  await expect(page.getByRole('status').filter({ hasText: 'Ek hizmet eklendi.' })).toBeVisible();
  expect(ekHizmetIstegi).toBe(1);
});

test('sözleşme linki: oluştur → adres kendi kökünden; yeni sürüm onay ister', async ({ page }) => {
  let link: object | null = null;
  const yazmalar: string[] = [];
  await sahteKiraApi(page, {
    yazma: (route, istek) => {
      yazmalar.push(`${istek.method()} ${new URL(istek.url()).pathname}`);
      link = {
        yol: '/sozlesme/tahmin-edilemez',
        erisimSayisi: 0,
        sonErisimUtc: null,
        olusturmaUtc: '2026-09-22T06:00:00Z',
        anlikGoruntuUtc: '2026-09-22T06:00:00Z',
        bayat: false,
      };
      return route.fulfill({ json: { link } });
    },
  });
  // Detay: paylaşım barı operasyon izniyle dolu, link sonradan gelir.
  await page.route(new RegExp(`/api/ui/v1/kiralar/${KIRA_ID}$`), (route) =>
    route.fulfill({
      json: {
        ...DETAY,
        paylasim: { link, musteriTel: null, musteriEmail: null, konu: 'Kira Sözleşmesi' },
      },
    }),
  );
  await page.goto(`/app/kiralar/${KIRA_ID}`);
  await page.getByRole('button', { name: 'Paylaşım linki oluştur' }).click();
  const adres = page.getByTestId('paylasim-adresi');
  await expect(adres).toHaveValue(/^http:\/\/127\.0\.0\.1:\d+\/sozlesme\/tahmin-edilemez$/);
  await page.getByRole('button', { name: 'Yeni sürüm' }).click();
  const onay = page.getByRole('alertdialog', { name: 'Yeni sürüm paylaşılsın mı?' });
  await expect(onay).toBeVisible();
  await onay.getByRole('button', { name: 'Vazgeç' }).click();
  expect(yazmalar).toEqual([`POST /api/ui/v1/kiralar/${KIRA_ID}/paylasim`]);
});

test("yazdırma rotası sunucunun PDF ucuna gider (SPA'ya yönlenmez)", async ({ page }) => {
  await page.route(`**/kiralar/${KIRA_ID}/pdf`, (route) =>
    route.fulfill({ contentType: 'text/plain', body: 'PDF' }),
  );
  await page.goto(`/app/kiralar/${KIRA_ID}/yazdir`);
  await expect(page).toHaveURL(new RegExp(`/kiralar/${KIRA_ID}/pdf$`));
  expect(new URL(page.url()).pathname.startsWith('/app/')).toBe(false);
});

test.describe('390 px ve koyu tema', () => {
  test.use({ isMobile: true, hasTouch: true, deviceScaleFactor: 2 });

  test('yeni kira: gövde taşması yok, koyu temada axe ciddi/kritik 0', async ({ page }) => {
    await sahteKiraApi(page);
    await page.setViewportSize({ width: 390, height: 844 });
    await page.emulateMedia({ colorScheme: 'dark' });
    await page.goto(YENI);
    await formHazir(page);
    expect(await tasmaOlc(page)).toEqual({ tasma: 0, suclular: [] });
    expect(await ciddiIhlaller(page)).toEqual([]);
    await page.getByRole('tab', { name: 'Araç' }).click();
    expect(await tasmaOlc(page)).toEqual({ tasma: 0, suclular: [] });

    // Kayıtlı kira: geniş tablolar kendi kutusunda kayar, gövde taşmaz.
    await page.goto(`/app/kiralar/${KIRA_ID}#sekme=ekhizmet`);
    await expect(page.getByRole('heading', { level: 1 })).toContainText('Kira 2026220901001');
    expect(await tasmaOlc(page)).toEqual({ tasma: 0, suclular: [] });
    expect(await ciddiIhlaller(page)).toEqual([]);
  });
});
