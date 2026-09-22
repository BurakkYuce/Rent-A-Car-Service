import { expect, test, type Page, type Request, type Route } from '@playwright/test';

import { BEN, ciddiIhlaller, hatalariTopla, oturumAc, problem, xsrfYaz } from './ortak';
import { tasmaOlc } from './vitrin-sayfalari';

/**
 * F4.3 kira formu (sahte `/api/ui/v1`, üretim derlemesi + CSP). Faz çıkışı e2e'leri: "doğrulama
 * hatasında form korunur", "oturum düşünce form kaybolmaz", "müsaitlik `cakisma` formu silmez",
 * "`?varac=` dolu form açar", "`#sekme=` doğru sekmeyi açar" + ek hizmet çift tık + PUT 58 alan + sürüm;
 * adversarial kalıcılaştırma: R2 bayat sekme (409 → birleştir → yeniden kaydet), P261-10, F4, F7.
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
  surum: 'v1',
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
  // F4.3b: sunucuda hesaplanmış gösterim toplamları (SPA toplamaz).
  toplamlar: { ekHizmetToplam: 180, cezaToplam: 250 },
};

/** F4.3b müşteri özeti — TC hiç gelmez; ehliyet/pasaport no sunucudan MASKELİ (SPA düz numara görmez). */
const MUSTERI_OZETI = {
  id: MUSTERI_ID,
  ad: 'Ayşe Yılmaz',
  tip: 'Bireysel',
  cepTel: '05321112233',
  email: 'ayse@ornek.test',
  ehliyetNoMaskeli: '****6543',
  pasaportNoMaskeli: '****4567',
  ehliyetSinifi: 'B',
  ehliyetTarihi: '2015-06-01T00:00:00+00:00',
  ehliyetYeri: 'İzmir',
  ehliyetUlke: 'TR',
  pasaportYeri: 'Ankara',
  adres: 'Atatürk Cd. No:5',
  il: 'İzmir',
  ilce: 'Karşıyaka',
  musteriTipi: 'Türk Ehliyetli',
  riskLimiti: 5000,
  karaListe: true,
  uyari: true,
  uyariNedeni: 'Geç iade geçmişi',
};

const KATALOG = {
  ogeler: [
    {
      id: TANIM_ID,
      kod: 'BEBEK',
      ad: 'Bebek koltuğu',
      birimUcret: 75.5,
      kdvOrani: 0.1,
      aciklama: '0-4 yaş',
      maxGun: 30,
    },
  ],
  toplam: 1,
};

interface Sahte {
  /** Kira yazma uçları (POST/PUT/DELETE) — test karar verir. */
  yazma?: (route: Route, istek: Request) => Promise<unknown> | unknown;
}

/** Tek işleyici: `/api/ui/v1/kiralar/**` + seçim uçları (yöntem + yola göre). */
async function sahteKiraApi(page: Page, { yazma }: Sahte = {}): Promise<string[]> {
  const hesapSorgulari: string[] = [];
  await page.route(/\/api\/ui\/v1\/secim\//, (route) => {
    const yol = new URL(route.request().url()).pathname;
    // F4.3b kimlikle etiket uçları.
    if (yol === `/api/ui/v1/secim/musteri/${MUSTERI_ID}`) {
      return route.fulfill({ json: { id: MUSTERI_ID, etiket: 'Ayşe Yılmaz', tip: 'Bireysel' } });
    }
    return route.fulfill({
      json: yol.endsWith('/secim/ek-hizmet')
        ? [{ id: TANIM_ID, etiket: 'Bebek koltuğu', kod: 'BEBEK' }]
        : [],
    });
  });
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
    if (yol === '/ek-hizmet-katalogu') return route.fulfill({ json: KATALOG });
    if (yol === `/${KIRA_ID}/musteri-ozet`) return route.fulfill({ json: MUSTERI_OZETI });
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
  // F4.3b: bağlantıdaki kimlik gerçek adla çözülür (secim/musteri/{id}).
  await expect(panel.getByLabel('Müşteri', { exact: true })).toHaveValue('Ayşe Yılmaz');
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

test('kayıtlı kira: PUT 58 alanın hepsini + sürümü taşır; ek hizmet çift tık TEK kalem yazar', async ({
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
  expect(Object.keys(putlar[0] ?? {})).toHaveLength(59);
  expect(putlar[0]?.['surum']).toBe('v1');
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

test('faturalı kirada ek hizmet ekle/sil 400: mesaj gösterilir, seçim silinmez', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, AG_HATASI);
  await sahteKiraApi(page, {
    yazma: (route, istek) =>
      istek.method() === 'DELETE'
        ? problem(route, 400, 'dogrulama', 'Faturalanmış kiranın ek hizmeti silinemez.')
        : problem(route, 400, 'dogrulama', 'Faturalanmış kiraya ek hizmet eklenemez.'),
  });
  await page.route(new RegExp(`/api/ui/v1/kiralar/${KIRA_ID}$`), (route) =>
    route.fulfill({
      json: {
        ...DETAY,
        ekHizmetler: [
          {
            id: '0b0e7c1a-7777-4aaa-8bbb-000000000007',
            ekHizmetTanimId: TANIM_ID,
            ad: 'Bebek koltuğu',
            miktar: 1,
            birimNetFiyat: 100,
            kdvOrani: 0.2,
            netTutar: 100,
            kdvTutar: 20,
            toplam: 120,
          },
        ],
      },
    }),
  );
  await page.goto(`/app/kiralar/${KIRA_ID}#sekme=ekhizmet`);
  const ekle = page.getByTestId('ek-hizmet-ekle');
  await ekle.getByRole('combobox').click();
  await page.getByRole('option', { name: /Bebek koltuğu/ }).click();
  await ekle.getByRole('button', { name: 'Ekle' }).click();
  await expect(page.locator('.rc-form-hatalari')).toContainText(
    'Faturalanmış kiraya ek hizmet eklenemez.',
  );
  // F4.3b: seçenek etiketi katalogdan, Blazor gibi "Ad (birim net)".
  await expect(ekle.getByRole('combobox')).toHaveValue('Bebek koltuğu (75,50 ₺ net)');

  await page.getByRole('button', { name: 'Sil Bebek koltuğu' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Onayla' }).click();
  await expect(
    page.getByRole('alert').filter({ hasText: 'Faturalanmış kiranın ek hizmeti silinemez.' }),
  ).toBeVisible();
  await expect(page.getByRole('table', { name: 'Ek hizmet kalemleri' })).toContainText(
    'Bebek koltuğu',
  );
  expect(hatalar).toEqual([]);
});

/** Kayıtlı kira için değişebilir sunucu durumu: detay GET'i ve yazmalar aynı nesneyi görür. */
async function durumluKira(
  page: Page,
  baslangic: Record<string, unknown>,
  yazma: (route: Route, istek: Request, sunucu: { kira: Record<string, unknown> }) => unknown,
  detayEk: Record<string, unknown> = {},
): Promise<{ kira: Record<string, unknown> }> {
  const sunucu = { kira: { ...KIRA, ...baslangic } as Record<string, unknown> };
  await sahteKiraApi(page, { yazma: (route, istek) => yazma(route, istek, sunucu) });
  await page.route(new RegExp(`/api/ui/v1/kiralar/${KIRA_ID}$`), (route) =>
    route.request().method() === 'GET'
      ? route.fulfill({ json: { ...DETAY, ...detayEk, kira: sunucu.kira } })
      : (yazma(route, route.request(), sunucu) as Promise<void>),
  );
  return sunucu;
}

const ayrintiAciklama = (page: Page) =>
  page
    .getByRole('tabpanel', { name: 'Ayrıntılar' })
    .getByRole('textbox', { name: 'Açıklama', exact: true });

test('F2/R2 bayat sekme: başka oturumun drop ücreti geri ALINMAZ — 409 → güncel hâl birleşir, açıklama korunur', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page, [...AG_HATASI, /status of 409/]);
  const putlar: Record<string, unknown>[] = [];
  const sunucu = await durumluKira(page, { surum: 'v1', dropUcreti: null }, (route, istek, s) => {
    if (istek.method() !== 'PUT') return route.fulfill({ status: 500 });
    const g = istek.postDataJSON() as Record<string, unknown>;
    putlar.push(g);
    if (g['surum'] !== s.kira['surum']) {
      return problem(
        route,
        409,
        'cakisma',
        'Kira başka bir oturumda değişti; güncel hâli yüklendi.',
      );
    }
    s.kira = { ...s.kira, aciklama: g['aciklama'], dropUcreti: g['dropUcreti'], surum: 'v3' };
    return route.fulfill({ json: s.kira });
  });
  await page.goto(`/app/kiralar/${KIRA_ID}#sekme=ayrintilar`);
  await expect(page.getByRole('heading', { level: 1 })).toContainText('Kira 2026220901001');

  // Başka oturum drop ücretini 300 yaptı (sunucu sürümü v2); bu sekme yeniden okumadı.
  sunucu.kira = { ...sunucu.kira, dropUcreti: 300, genelToplam: 3960, surum: 'v2' };
  await ayrintiAciklama(page).fill('bayat sekme');
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();

  await expect(page.locator('rc-uyari-bandi')).toContainText('başka bir oturumda değişti');
  expect(putlar[0]).toMatchObject({ surum: 'v1', dropUcreti: null }); // reddedildi, hiçbir şey yazılmadı
  // Güncel hâl birleşti: dokunulmayan drop ücreti 300, dokunulan açıklama yerinde.
  await page.getByRole('tab', { name: 'Fiyat/Toplam' }).click();
  await expect(
    page.getByRole('tabpanel', { name: 'Fiyat/Toplam' }).getByLabel('Drop ücreti'),
  ).toHaveValue('300,00');
  await page.getByRole('tab', { name: 'Ayrıntılar' }).click();
  await expect(ayrintiAciklama(page)).toHaveValue('bayat sekme');

  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Kira kaydedildi.' })).toBeVisible();
  expect(putlar).toHaveLength(2);
  expect(putlar[1]).toMatchObject({ surum: 'v2', dropUcreti: 300, aciklama: 'bayat sekme' });
  expect(sunucu.kira['dropUcreti']).toBe(300);
  expect(hatalar).toEqual([]);
});

test('P261-10 form kirliyken "Provizyon al": sunucunun yazdığı provizyon tarihi Kaydet\'te SİLİNMEZ', async ({
  page,
}) => {
  const putlar: Record<string, unknown>[] = [];
  await durumluKira(
    page,
    { provizyon: 500, provizyonTarih: null, provizyonDurum: 'Yok' },
    (route, istek, s) => {
      if (new URL(istek.url()).pathname.endsWith('/provizyon/al')) {
        s.kira = {
          ...s.kira,
          provizyonTarih: '2026-09-22T22:30:00+00:00', // İstanbul 23.09 01:30
          provizyonDurum: 'Alindi',
          surum: 'v2',
        };
        return route.fulfill({ json: s.kira });
      }
      if (istek.method() === 'PUT') {
        const g = istek.postDataJSON() as Record<string, unknown>;
        putlar.push(g);
        if (g['surum'] !== s.kira['surum']) return problem(route, 409, 'cakisma', 'Bayat.');
        return route.fulfill({ json: s.kira });
      }
      return route.fulfill({ status: 500 });
    },
  );
  await page.goto(`/app/kiralar/${KIRA_ID}#sekme=ayrintilar`);
  await ayrintiAciklama(page).fill('not');
  await page.getByRole('tab', { name: 'Finans/Uçuş' }).click();
  await page.getByRole('button', { name: 'Provizyon al (manuel)' }).click();
  await expect(page.getByTestId('provizyon-durum')).toHaveText('Alındı');
  await expect(
    page.getByRole('tabpanel', { name: 'Finans/Uçuş' }).getByLabel('Provizyon tarihi'),
  ).toHaveValue('23.09.2026');
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Kira kaydedildi.' })).toBeVisible();
  expect(putlar).toHaveLength(1);
  expect(putlar[0]).toMatchObject({
    surum: 'v2',
    aciklama: 'not',
    provizyonTarih: '2026-09-22T22:30:00+00:00', // orijinal an, gün yuvarlaması yok (F6)
  });
});

test('F4 2. sürücü carisi silinmiş: kimlik korunur, PUT null göndermez', async ({ page }) => {
  const IKINCI = '0b0e7c1a-9999-4aaa-8bbb-000000000009';
  const putlar: Record<string, unknown>[] = [];
  await durumluKira(
    page,
    { ikinciSurucuId: IKINCI },
    (route, istek, s) => {
      putlar.push(istek.postDataJSON() as Record<string, unknown>);
      return route.fulfill({ json: s.kira });
    },
    { ikinciSurucu: null },
  );
  await page.goto(`/app/kiralar/${KIRA_ID}#sekme=musteri`);
  await expect(
    page.getByRole('tabpanel', { name: 'Müşteri' }).getByLabel('2. sürücü (kayıtlı cari)'),
  ).toHaveValue('(kayıt bulunamadı)');
  await page.getByRole('tab', { name: 'Ayrıntılar' }).click();
  await ayrintiAciklama(page).fill('yalnız açıklama');
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect.poll(() => putlar.length).toBe(1);
  expect(putlar[0]).toMatchObject({ ikinciSurucuId: IKINCI, aciklama: 'yalnız açıklama' });
});

test('F7 hızlı müşteri: ana form geçersizken (araç yok) cari AÇILMAZ', async ({ page }) => {
  const musteriPostlari: string[] = [];
  await sahteKiraApi(page, {
    yazma: (route, istek) => {
      musteriPostlari.push(new URL(istek.url()).pathname);
      return route.fulfill({ status: 201, json: { id: MUSTERI_ID, etiket: 'Yetim' } });
    },
  });
  await page.goto('/app/kiralar/yeni');
  await page.locator('#kf-yeni-musteri summary').click();
  await page.locator('#kf-yeni-musteri').getByLabel('Ad', { exact: true }).fill('Yetim');
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect(hizli(page).getByLabel('Araç', { exact: true })).toHaveAttribute(
    'aria-invalid',
    'true',
  );
  await page.waitForTimeout(300);
  expect(musteriPostlari).toEqual([]);
  await expect(page.locator('#kf-yeni-musteri').getByLabel('Ad', { exact: true })).toHaveValue(
    'Yetim',
  );
});

test("yazdırma rotası sunucunun PDF ucuna gider (SPA'ya yönlenmez)", async ({ page }) => {
  await page.route(`**/kiralar/${KIRA_ID}/pdf`, (route) =>
    route.fulfill({ contentType: 'text/plain', body: 'PDF' }),
  );
  await page.goto(`/app/kiralar/${KIRA_ID}/yazdir`);
  await expect(page).toHaveURL(new RegExp(`/kiralar/${KIRA_ID}/pdf$`));
  expect(new URL(page.url()).pathname.startsWith('/app/')).toBe(false);
});

test('müşteri sekmesi: TC şifreli notu, belge no MASKELİ, kara liste, risk limiti', async ({
  page,
}) => {
  const hatalar = hatalariTopla(page);
  await sahteKiraApi(page);
  await page.goto(`/app/kiralar/${KIRA_ID}#sekme=musteri`);
  const panel = page.getByRole('tabpanel', { name: 'Müşteri' });
  const ozet = panel.getByTestId('musteri-ozeti');
  await expect(ozet.getByTestId('tc-kimlik')).toHaveText('***şifreli — cari kartında');
  await expect(ozet).toContainText('05321112233');
  await expect(panel).toContainText('****6543');
  await expect(panel).toContainText('****4567');
  await expect(panel).toContainText('Atatürk Cd. No:5');
  await expect(panel).toContainText('01.06.2015');
  await expect(panel.getByTestId('kara-liste')).toContainText('Kara liste');
  await expect(panel.getByTestId('kara-liste')).toContainText('Geç iade geçmişi');
  await expect(panel.getByTestId('risk-limiti')).toHaveText('5.000,00 ₺');
  expect(await ciddiIhlaller(page)).toEqual([]);
  expect(hatalar).toEqual([]);
});

test('Hızlı Giriş: ceza rozeti ve ek hizmet tutarı SUNUCU toplamından', async ({ page }) => {
  await sahteKiraApi(page);
  await page.goto(`/app/kiralar/${KIRA_ID}`);
  await expect(page.getByRole('heading', { level: 1 })).toContainText('Kira 2026220901001');
  const rozetler = hizli(page).getByTestId('hizli-rozetler');
  await expect(rozetler).toContainText('Tahsilat: 1.000,00 ₺');
  await expect(rozetler).toContainText('Kalan: 2.600,00 ₺');
  await expect(rozetler.getByTestId('ceza-rozeti')).toHaveText('Ceza: 250,00 ₺');
  await expect(hizli(page).getByTestId('kayitli-ek-hizmet')).toHaveText('180,00 ₺');
});

test('paylaş: WhatsApp / Gmail bağlantıları sunucu metni + link, geçersiz GSM uyarır', async ({
  page,
}) => {
  // window.open yakalanır (dış siteye gidilmez); init betiği CDP ile eklenir, CSP'den etkilenmez.
  await page.addInitScript(() => {
    const w = window as unknown as { __acilan: string[] };
    w.__acilan = [];
    window.open = (u?: string | URL) => {
      w.__acilan.push(String(u));
      return null;
    };
  });
  await sahteKiraApi(page);
  const MESAJ =
    'Sayın Ayşe Yılmaz, 2026220901001 nolu kira sözleşmeniz: 22.09.2026 - 25.09.2026, genel toplam 3.600,00 TL.';
  await page.route(new RegExp(`/api/ui/v1/kiralar/${KIRA_ID}$`), (route) =>
    route.fulfill({
      json: {
        ...DETAY,
        paylasim: {
          link: {
            yol: '/sozlesme/tahmin-edilemez',
            erisimSayisi: 0,
            sonErisimUtc: null,
            olusturmaUtc: '2026-09-22T06:00:00Z',
            anlikGoruntuUtc: '2026-09-22T06:00:00Z',
            bayat: false,
          },
          musteriTel: '0532 111 22 33',
          musteriEmail: 'ayse@ornek.test',
          konu: 'Kira Sözleşmesi 2026220901001',
          mesaj: MESAJ,
        },
      },
    }),
  );
  await page.goto(`/app/kiralar/${KIRA_ID}`);
  const bar = page.getByTestId('paylas-bari');
  await expect(bar.getByLabel('Numara (GSM)')).toHaveValue('0532 111 22 33');
  await bar.getByRole('button', { name: "WhatsApp'ta aç" }).click();
  await bar.getByRole('button', { name: "Gmail'de aç" }).click();
  const acilan = await page.evaluate(() => (window as unknown as { __acilan: string[] }).__acilan);
  const koken = new URL(page.url()).origin;
  const tamMesaj = `${MESAJ} Sözleşmeniz: ${koken}/sozlesme/tahmin-edilemez`;
  expect(acilan).toEqual([
    `https://wa.me/905321112233?text=${encodeURIComponent(tamMesaj)}`,
    'https://mail.google.com/mail/?view=cm&fs=1&to=ayse%40ornek.test' +
      `&su=${encodeURIComponent('Kira Sözleşmesi 2026220901001')}&body=${encodeURIComponent(tamMesaj)}`,
  ]);

  await bar.getByLabel('Numara (GSM)').fill('123');
  await bar.getByRole('button', { name: "WhatsApp'ta aç" }).click();
  await expect(bar.getByRole('alert')).toHaveText('Geçerli bir GSM girin (örn. 05xx xxx xx xx).');
  expect(
    await page.evaluate(() => (window as unknown as { __acilan: string[] }).__acilan.length),
  ).toBe(2);
});

test('ek hizmet matrisi: tanım fiyat/KDV satırları, işaret hesaba girer (tutar sunucudan)', async ({
  page,
}) => {
  const hesap = await sahteKiraApi(page);
  await page.goto(`${YENI}#sekme=ekhizmet`);
  const matris = page.getByTestId('ek-hizmet-matrisi');
  const satir = matris.getByRole('row', { name: /Bebek koltuğu/ });
  await expect(satir).toContainText('75,50 ₺');
  await expect(satir).toContainText('%10');
  await expect(satir).toContainText('maks 30 gün');
  await satir.getByRole('checkbox', { name: 'Seç Bebek koltuğu' }).check();
  await expect(satir.getByRole('textbox', { name: 'Miktar Bebek koltuğu' })).toHaveValue(
    /^1(,00)?$/,
  );
  await expect.poll(() => decodeURIComponent(hesap.at(-1) ?? '')).toContain(`ek=${TANIM_ID}:1`);
  await satir.getByRole('checkbox', { name: 'Seç Bebek koltuğu' }).uncheck();
  await expect.poll(() => hesap.at(-1) ?? '').not.toContain('ek=');
  expect(await ciddiIhlaller(page)).toEqual([]);
});

test('ek hizmet kataloğu KESİKSE listede olmayan tanım sunucu aramasıyla eklenir (#262 L2)', async ({
  page,
}) => {
  const hesap = await sahteKiraApi(page);
  const NAV_ID = '0b0e7c1a-6666-4aaa-8bbb-000000000016';
  await page.route('**/api/ui/v1/kiralar/ek-hizmet-katalogu', (route) =>
    route.fulfill({
      json: {
        ogeler: [{ id: NAV_ID, kod: 'NAV', ad: 'Navigasyon', birimUcret: 40, kdvOrani: 0.2 }],
        toplam: 206,
      },
    }),
  );
  await page.goto(`${YENI}#sekme=ekhizmet`);
  const panel = page.getByRole('tabpanel', { name: 'Ek Hizmetler' });
  await expect(panel).toContainText('İlk 1 tanım gösteriliyor (toplam 206).');
  const diger = panel.getByRole('combobox', {
    name: 'Listede olmayan ek hizmet ekle (ada göre ara)',
  });
  await diger.click();
  await page.getByRole('option', { name: /Bebek koltuğu/ }).click();
  const satir = page.getByTestId('ek-hizmet-matrisi').getByRole('row', { name: /Bebek koltuğu/ });
  await expect(satir.getByRole('checkbox', { name: 'Seç Bebek koltuğu' })).toBeChecked();
  await expect.poll(() => decodeURIComponent(hesap.at(-1) ?? '')).toContain(`ek=${TANIM_ID}:1`);
  await satir.getByRole('checkbox', { name: 'Seç Bebek koltuğu' }).uncheck();
  await expect(page.getByTestId('ek-hizmet-matrisi')).not.toContainText('Bebek koltuğu');
});

test('anonim cari (#262 M1): paylaşım kutuları boş gelir, geçersiz numarayla WhatsApp açılmaz', async ({
  page,
}) => {
  await sahteKiraApi(page);
  await page.route(new RegExp(`/api/ui/v1/kiralar/${KIRA_ID}$`), (route) =>
    route.fulfill({
      json: {
        ...DETAY,
        musteri: { id: MUSTERI_ID, ad: 'Anonim müşteri' },
        paylasim: {
          link: null,
          musteriTel: null,
          musteriEmail: null,
          konu: 'Kira Sözleşmesi 2026220901001',
          mesaj: 'Sayın müşterimiz, 2026220901001 nolu kira sözleşmeniz: …',
        },
      },
    }),
  );
  await page.goto(`/app/kiralar/${KIRA_ID}`);
  const bar = page.getByTestId('paylas-bari');
  await expect(bar.getByLabel('Numara (GSM)')).toHaveValue('');
  await expect(bar.getByLabel('E-posta')).toHaveValue('');
  await bar.getByRole('button', { name: "WhatsApp'ta aç" }).click();
  await expect(bar.getByRole('alert')).toHaveText('Geçerli bir GSM girin (örn. 05xx xxx xx xx).');
  await expect(hizli(page).getByLabel('Müşteri', { exact: true })).toHaveValue('Anonim müşteri');
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

    await page.getByRole('tab', { name: 'Ek Hizmetler' }).click();
    await expect(page.getByTestId('ek-hizmet-matrisi')).toBeVisible();
    expect(await tasmaOlc(page)).toEqual({ tasma: 0, suclular: [] });

    // Kayıtlı kira: müşteri özeti ızgarası taşmaz.
    await page.goto(`/app/kiralar/${KIRA_ID}#sekme=musteri`);
    await expect(page.getByTestId('musteri-ozeti')).toBeVisible();
    expect(await tasmaOlc(page)).toEqual({ tasma: 0, suclular: [] });

    // Kayıtlı kira: geniş tablolar kendi kutusunda kayar, gövde taşmaz.
    await page.goto(`/app/kiralar/${KIRA_ID}#sekme=ekhizmet`);
    await expect(page.getByRole('heading', { level: 1 })).toContainText('Kira 2026220901001');
    expect(await tasmaOlc(page)).toEqual({ tasma: 0, suclular: [] });
    expect(await ciddiIhlaller(page)).toEqual([]);
  });
});
