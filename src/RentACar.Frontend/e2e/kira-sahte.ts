import { expect, type Page, type Request, type Route } from '@playwright/test';

/**
 * Kira formu (F4.3/F4.4) sahte `/api/ui/v1` verisi ve yardımcıları — `kira-formu.spec.ts` ile F4.6
 * `kesis.spec.ts` (Blazor "Kirala" bağlantısı → 302 → SPA formu) ORTAK kullanır. Beklenen değerler sahte
 * yanıtlardan ELLE kurulur.
 */
export const RENTAL_ID = '0b0e7c1a-1111-4aaa-8bbb-000000000001';
export const MUSTERI_ID = '0b0e7c1a-2222-4aaa-8bbb-000000000002';
export const ARAC_ID = '0b0e7c1a-3333-4aaa-8bbb-000000000003';
export const DEFINITION_ID = '0b0e7c1a-6666-4aaa-8bbb-000000000006';

export const YENI = `/app/kiralar/yeni?varac=${ARAC_ID}&vfrom=2026-10-01&vto=2026-10-04&musteriId=${MUSTERI_ID}`;

export const AVAILABLE = [
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

export const CALCULATION = {
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

export const RENTAL = {
  id: RENTAL_ID,
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

export const DETAIL = {
  kira: RENTAL,
  musteri: { id: MUSTERI_ID, ad: 'Ayşe Yılmaz' },
  ikinciSurucu: null,
  arac: { ...AVAILABLE[0] },
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
export const CUSTOMER_SUMMARY = {
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

export const CATALOG = {
  ogeler: [
    {
      id: DEFINITION_ID,
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

export interface Sahte {
  /** Kira yazma uçları (POST/PUT/DELETE) — test karar verir. */
  yazma?: (route: Route, request: Request) => Promise<unknown> | unknown;
}

/** Tek işleyici: `/api/ui/v1/kiralar/**` + seçim uçları (yöntem + yola göre). */
export async function fakeRentalApi(page: Page, { yazma }: Sahte = {}): Promise<string[]> {
  const calculationQueries: string[] = [];
  // F4.4 sabit finans paneli (tembel) kayıtlı kirada kasa/banka hesaplarını okur — bu dosyanın testleri panele
  // dokunmaz; boş liste yeter (sahte olmayan istek 404 konsol hatası üretirdi).
  await page.route(/\/api\/ui\/v1\/finans\//, (route) =>
    route.request().method() === 'GET'
      ? route.fulfill({ json: [] })
      : route.fulfill({ status: 500 }),
  );
  await page.route(/\/api\/ui\/v1\/secim\//, (route) => {
    const path = new URL(route.request().url()).pathname;
    // F4.3b kimlikle etiket uçları.
    if (path === `/api/ui/v1/secim/musteri/${MUSTERI_ID}`) {
      return route.fulfill({ json: { id: MUSTERI_ID, etiket: 'Ayşe Yılmaz', tip: 'Bireysel' } });
    }
    return route.fulfill({
      json: path.endsWith('/secim/ek-hizmet')
        ? [{ id: DEFINITION_ID, etiket: 'Bebek koltuğu', kod: 'BEBEK' }]
        : [],
    });
  });
  await page.route(/\/api\/ui\/v1\/kiralar(\/|\?|$)/, async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname.replace('/api/ui/v1/kiralar', '');
    if (request.method() !== 'GET')
      return (yazma ?? ((r) => r.fulfill({ status: 500 })))(route, request);
    if (path === '/form-varsayilanlari') {
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
    if (path === '/musait-arac') return route.fulfill({ json: AVAILABLE });
    if (path === '/ek-hizmet-katalogu') return route.fulfill({ json: CATALOG });
    if (path === `/${RENTAL_ID}/musteri-ozet`) return route.fulfill({ json: CUSTOMER_SUMMARY });
    if (path === '/hesapla') {
      calculationQueries.push(url.search);
      return route.fulfill({ json: CALCULATION });
    }
    if (path === `/${RENTAL_ID}`) return route.fulfill({ json: DETAIL });
    if (path === `/${RENTAL_ID}/karne-ozeti`) {
      return route.fulfill({ json: { vehicleId: ARAC_ID, dolulukYuzde: 61.5 } });
    }
    if (path.startsWith(`/${RENTAL_ID}/donus-hesapla`)) {
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
  return calculationQueries;
}

/** Hızlı Giriş paneli (CSS ile: diyalog açıkken arka plan `aria-hidden`, rol sorgusu onu görmez). */
export const quick = (page: Page) => page.locator('[data-rc-sekme="hizli"]');

export async function formReady(page: Page): Promise<void> {
  await expect(quick(page).getByLabel('Araç', { exact: true })).toHaveValue(
    '34 ABC 123 — Fiat Egea',
  );
  await expect(page.getByTestId('canli-hesap').first()).toBeVisible();
}
