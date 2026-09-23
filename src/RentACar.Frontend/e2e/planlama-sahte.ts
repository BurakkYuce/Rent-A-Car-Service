import type { Page } from '@playwright/test';

/**
 * F5.2b planlama ekranlarının sahte `/api/ui/v1` yanıtları (harness yalnız statik SPA sunar). Değerler
 * ELLE kurulmuş senaryodur (bağımsız oracle): ekran hesap yapmaz, sunucunun verdiğini gösterir.
 */
export const ARAC_1 = 'a0000000-0000-4000-8000-000000000001';
export const ARAC_2 = 'a0000000-0000-4000-8000-000000000002';
export const MUSTERI_1 = 'c0000000-0000-4000-8000-000000000001';
export const SART_1 = 'b0000000-0000-4000-8000-000000000001';
export const SART_2 = 'b0000000-0000-4000-8000-000000000002';
export const FILO_1 = 'f0000000-0000-4000-8000-000000000001';

const bos = (n: number) => Array.from({ length: n }, () => null as string | null);

/** Ekim 2026: 34ABC123 1–3 kirada, 5'inde rezervasyon; 34XYZ9 boş. */
export function takvimYaniti(ay = '2026-10') {
  const gunler = bos(31);
  gunler[0] = gunler[1] = gunler[2] = 'Kira';
  gunler[4] = 'Rezervasyon';
  return {
    ay,
    gunSayisi: 31,
    oncekiAy: '2026-09',
    sonrakiAy: '2026-11',
    araclar: [
      { id: ARAC_1, plaka: '34ABC123', gunler },
      { id: ARAC_2, plaka: '34XYZ9', gunler: bos(31) },
    ],
    aracToplam: 2,
  };
}

export function musaitlikSatiri(id: string, plaka: string, ek: Record<string, unknown> = {}) {
  return {
    id,
    plaka,
    marka: 'Fiat',
    tip: 'Egea',
    modelYili: 2024,
    yas: 2,
    yakit: 'Dizel',
    vites: 'Manuel',
    renk: 'Beyaz',
    sipp: 'CDMR',
    grup: 'C',
    sube: 'Merkez',
    kmLimiti: 300,
    minSurucuYas: 21,
    minEhliyetYil: 2,
    provizyon: 1500,
    provizyonDoviz: 'EUR',
    karLastigi: true,
    temizlik: false,
    ozelKod1: null,
    km: 45210,
    bostaGun: 4,
    sonMusteri: 'Ayşe Yılmaz',
    fiyat: { gunluk: 1250.5, toplam: 3751.5, paraBirimi: 'TRY' },
    ...ek,
  };
}

/** Pencere: 01.10.2026 09:00 – 04.10.2026 09:00 İstanbul (UTC 06:00). */
export const MUSAITLIK_YANITI = {
  pencereBas: '2026-10-01T06:00:00Z',
  pencereBit: '2026-10-04T06:00:00Z',
  araclar: [musaitlikSatiri(ARAC_1, '34ABC123')],
  brokerElenen: 1,
  brokerGerekce: ['GRUP_KAPALI'],
  kiralaSorgusu: { vfrom: '2026-10-01', vto: '2026-10-04', vgrup: 'C' },
};

export function rezSart(id: string, ek: Record<string, unknown> = {}) {
  return {
    id,
    musteriId: MUSTERI_1,
    musteriAd: 'Ayşe Yılmaz',
    sart: 'Bebek koltuğu',
    grup: 'Ekipman',
    basTar: '2026-09-30T21:00:00Z',
    bitTar: null,
    talepTarihi: '2026-09-20T07:15:00Z',
    karsilamaTarihi: null,
    karsilandi: false,
    teslimEden: null,
    reservationId: null,
    quotationId: null,
    surum: null,
    ...ek,
  };
}

/** Eski (1995 tarihli) aktif filo sözleşmesi: 3 × (1.000 net + 200 KDV) = 3.600 + 50 damga = 3.650. */
export function filoDetay(ek: Record<string, unknown> = {}) {
  const taksit = (sira: number, vade: string) => ({
    sira,
    vade,
    net: 1000,
    kdv: 200,
    toplam: 1200,
  });
  return {
    id: FILO_1,
    no: 'FK-000001',
    durum: 'Aktif',
    surum: 'surum-1',
    musteriId: MUSTERI_1,
    musteriAd: 'Ayşe Yılmaz',
    vehicleId: ARAC_1,
    plaka: '34ABC123',
    basTar: '2026-09-30T21:00:00Z',
    sureAy: 3,
    aylikUcret: 1000,
    kdvOrani: 0.2,
    doviz: 'TRY',
    kur: 1,
    toplamKmLimiti: null,
    damgaVergisi: 50,
    aciklama: null,
    satisTemsilcisi: 'Ali',
    faturaTuru: 'Dönem',
    sozlesmeTarihi: '1995-03-10T00:00:00Z',
    imzaTarih: null,
    makbuzNo: null,
    dosyaNo: null,
    sozlesmeNo: 'S-1',
    vadeGun: 30,
    fiyatTuru: null,
    kaynak: null,
    cikisKm: 1000,
    toplamKm: null,
    ozet: {
      toplamNet: 3000,
      toplamKdv: 600,
      damga: 50,
      genelToplam: 3650,
      taksitler: [
        taksit(1, '2026-09-30T21:00:00Z'),
        taksit(2, '2026-10-31T21:00:00Z'),
        taksit(3, '2026-11-30T21:00:00Z'),
      ],
    },
    yetkiler: { kunye: true, tamamla: true, iptal: false },
    ...ek,
  };
}

/** Ortak yardımcı uçlar: tablo düzeni (yok), müşteri seçimi. */
export async function ortakUclar(page: Page): Promise<void> {
  await page.route('**/api/ui/v1/tablo-duzenleri/**', (route) =>
    route.fulfill({ json: { tabloKodu: 'x', duzen: null, guncellemeUtc: null } }),
  );
  await page.route(
    (url) => url.pathname === '/api/ui/v1/secim/musteri',
    (route) => route.fulfill({ json: [{ id: MUSTERI_1, etiket: 'Ayşe Yılmaz', tip: 'Bireysel' }] }),
  );
  await page.route(
    (url) => url.pathname === '/api/ui/v1/secim/arac',
    (route) =>
      route.fulfill({
        json: [{ id: ARAC_1, etiket: '34ABC123', plaka: '34ABC123', grup: 'C', durum: 'Musait' }],
      }),
  );
}
