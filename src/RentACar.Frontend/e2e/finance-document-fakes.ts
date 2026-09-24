import type { Page, Route } from '@playwright/test';

import { kaydet, type KayitliIstek } from './ortak';

/** F8.2b finans belge ekranları için sahte `/api/ui/v1` (değerler elle kurulmuş; uygulama kodundan türetilmez). */
export const INVOICE_1 = 'f8f8f8f8-0000-4000-8000-000000000001';
export const PENALTY_1 = 'f8f8f8f8-0000-4000-8000-000000000002';
export const PENALTY_LINE_1 = 'f8f8f8f8-0000-4000-8000-000000000012';
export const EXPENSE_1 = 'f8f8f8f8-0000-4000-8000-000000000003';
export const INCOMING_1 = 'f8f8f8f8-0000-4000-8000-000000000004';
export const SALE_1 = 'f8f8f8f8-0000-4000-8000-000000000005';
export const RENTAL_1 = 'f8f8f8f8-0000-4000-8000-000000000006';
export const CARI_1 = 'c0c0c0c0-0000-4000-8000-000000000001';
export const VEHICLE_1 = 'a1a1a1a1-0000-4000-8000-000000000001';

export function invoiceRow(): Record<string, unknown> {
  return {
    id: INVOICE_1,
    no: 'RNT2026000000001',
    tarih: '2026-09-01T09:00:00Z',
    vadeTarihi: null,
    durum: 'Kesildi',
    iadeMi: false,
    manuelMi: false,
    cariId: CARI_1,
    cariAd: 'Ayşe Yılmaz',
    kiraId: RENTAL_1,
    sozlesmeNo: '2026010901001',
    plaka: '34ABC123',
    ofis: 'Merkez',
    netTutar: 1000,
    kdvTutar: 200,
    genelToplam: 1200,
    doviz: 'TRY',
    kur: 1,
    eFaturaGonderildi: false,
    eFaturaEttn: null,
    kaynakFaturaId: null,
  };
}

export function invoiceDetail(): Record<string, unknown> {
  const r = invoiceRow();
  return {
    ...r,
    kaynakFaturaId: null,
    iadeFaturaId: null,
    otv: null,
    tevkifatOran: null,
    tevkifatTutar: null,
    damgaVergisi: null,
    islemSube: null,
    evrakNo: null,
    faturaOzelKod: null,
    odemeTuru: null,
    gonderimSekli: null,
    kdvSifirSebep: null,
    pdfAdresi: `/faturalar/${INVOICE_1}/pdf`,
    satirlar: [
      {
        id: 'l1',
        aciklama: 'Kira 3 gün',
        miktar: 3,
        birimNetFiyat: 333.33,
        kdvOrani: 0.2,
        satirNet: 1000,
        satirKdv: 200,
        satirToplam: 1200,
      },
    ],
  };
}

export function penaltyRow(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    id: PENALTY_1,
    no: 'CZ-000001',
    cezaTuru: 'Hız',
    tebligTarihi: '2026-09-02T00:00:00Z',
    vadeTarihi: '2026-09-17T00:00:00Z',
    durum: 'Yansitildi',
    tutar: 900,
    odenenTutar: 0,
    kalan: 900,
    odemeDurumu: 'Odenmemis',
    sebep: null,
    aracId: VEHICLE_1,
    plaka: '34ABC123',
    cariId: CARI_1,
    cariAd: 'Ayşe Yılmaz',
    kiraId: null,
    sozlesmeNo: null,
    faturaNo: null,
    makbuzNo: null,
    islemSube: null,
    yer: null,
    saat: null,
    odenmeTarihi: null,
    ...extra,
  };
}

export function penaltyDetail(paid = 0): Record<string, unknown> {
  return {
    ceza: penaltyRow({ odenenTutar: paid, kalan: 900 - paid }),
    cepTel: null,
    kalemler: [
      { id: PENALTY_LINE_1, sira: 1, tutar: 900, odenen: paid, kalan: 900 - paid, sebep: 'Hız' },
    ],
    odemeler: [],
  };
}

export function expenseRow(): Record<string, unknown> {
  return {
    id: EXPENSE_1,
    no: 'GD-000001',
    tip: 'Arac',
    tarih: '2026-09-03T00:00:00Z',
    aracId: VEHICLE_1,
    plaka: '34ABC123',
    cariId: CARI_1,
    cariAd: 'Lastikçi Ltd.',
    sube: 'Merkez',
    evrakNo: null,
    netTutar: 500,
    kdvOrani: 0.2,
    kdvTutar: 100,
    genelToplam: 600,
    doviz: 'TRY',
    kur: 1,
    odemeYontemi: 'AcikHesap',
    kasaBankaHesap: 'Cari',
    aciklama: 'Lastik',
    kiraId: null,
    vade: null,
    odemeTarihi: null,
    odenen: 0,
    kalan: 600,
    takipEdilir: true,
  };
}

export function incomingRow(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    id: INCOMING_1,
    ettn: 'ETTN-0001',
    gonderenVkn: '1234567890',
    gonderenUnvan: 'Tedarikçi A.Ş.',
    tarih: '2026-09-04T00:00:00Z',
    netTutar: 1000,
    kdvTutar: 200,
    genelToplam: 1200,
    doviz: 'TRY',
    durum: 'Onaylandi',
    redNedeni: null,
    aciklama: null,
    kdv20Matrah: 1000,
    kdv20: 200,
    kdv10Matrah: null,
    kdv10: null,
    kdv1Matrah: null,
    kdv1: null,
    kdv0Matrah: null,
    aracId: null,
    plaka: null,
    giderKategoriId: null,
    cariId: null,
    cariAd: null,
    giderTipi: null,
    giderlestirildi: false,
    giderlestirilmeTarihi: null,
    ...extra,
  };
}

export function saleRow(): Record<string, unknown> {
  return {
    id: SALE_1,
    no: 'AS-000001',
    tarih: '2026-09-05T00:00:00Z',
    aracId: VEHICLE_1,
    plaka: '34XYZ987',
    aliciCariId: CARI_1,
    aliciAd: 'Ayşe Yılmaz',
    satisNet: 400000,
    kdvOrani: 0.2,
    kdvTutar: 80000,
    genelToplam: 480000,
    doviz: 'TRY',
    kur: 1,
    durum: 'Tamamlandi',
    noterNo: null,
    noterSatisTarihi: null,
    ihaleTarihi: null,
    ihaleFirmasi: null,
    satisKm: 120000,
    satisKanali: 'Galeri',
    satisiVerildi: false,
    aciklama: null,
  };
}

const page1 = (kayitlar: unknown[], boyut = 50) => ({
  kayitlar,
  toplam: kayitlar.length,
  sayfaNo: 1,
  boyut,
});

export interface DocumentEndpoints {
  /** Yazma istekleri için özel yanıt; true = yanıtlandı. */
  readonly write?: (route: Route, path: string) => Promise<boolean> | boolean;
  /** Okuma istekleri için özel yanıt (ör. yeni kaydın detayı); true = yanıtlandı. */
  readonly read?: (route: Route, path: string) => Promise<boolean> | boolean;
  readonly penalty?: () => Record<string, unknown>;
  readonly incoming?: () => Record<string, unknown>;
}

/** Finans belge uçlarının sahteleri; yazılan istekler (gövde + anahtar) sırayla döner. */
export async function documentEndpoints(
  page: Page,
  e: DocumentEndpoints = {},
): Promise<KayitliIstek[]> {
  const written: KayitliIstek[] = [];
  const json = (route: Route, body: unknown, status = 200) => route.fulfill({ status, json: body });
  await page.route('**/api/ui/v1/tablo-duzenleri/**', (r) =>
    json(r, { tabloKodu: 'x', duzen: null, guncellemeUtc: null }),
  );
  await page.route('**/api/ui/v1/finans/hesaplar', (r) =>
    json(r, [{ id: 'a4a4a4a4-0000-4000-8000-000000000001', etiket: 'Merkez Kasa', tur: 'Kasa' }]),
  );
  await page.route('**/api/ui/v1/gider-turleri*', (r) => json(r, []));
  await page.route('**/api/ui/v1/ceza-turleri*', (r) => json(r, page1([])));
  await page.route('**/api/ui/v1/kiralar*', (r) => json(r, page1([])));
  await page.route(
    (u) => u.pathname.startsWith('/api/ui/v1/secim/'),
    (r) => {
      const p = new URL(r.request().url()).pathname;
      if (p.startsWith('/api/ui/v1/secim/musteri'))
        return json(r, [{ id: CARI_1, etiket: 'Ayşe Yılmaz' }]);
      if (p.startsWith('/api/ui/v1/secim/arac'))
        return json(r, [{ id: VEHICLE_1, etiket: '34ABC123', plaka: '34ABC123' }]);
      if (p.startsWith('/api/ui/v1/secim/sube'))
        return json(r, [{ id: 's1', etiket: 'Merkez', kod: null }]);
      return json(r, []);
    },
  );
  const handled = [
    '/api/ui/v1/faturalar',
    '/api/ui/v1/cezalar',
    '/api/ui/v1/giderler',
    '/api/ui/v1/gelen-efatura',
    '/api/ui/v1/satislar',
  ];
  await page.route(
    (u) => handled.some((h) => u.pathname.startsWith(h)),
    async (r) => {
      const path = new URL(r.request().url()).pathname;
      const method = r.request().method();
      if (method !== 'GET') {
        written.push(kaydet(r.request()));
        if (await e.write?.(r, path)) return;
        return json(r, { id: 'x1', no: 'YENI-1' });
      }
      if (await e.read?.(r, path)) return;
      switch (path) {
        case '/api/ui/v1/faturalar':
          return json(r, page1([invoiceRow()]));
        case `/api/ui/v1/faturalar/${INVOICE_1}`:
          return json(r, invoiceDetail());
        case '/api/ui/v1/faturalar/satirlar':
          return json(
            r,
            page1([
              {
                ...invoiceDetail(),
                faturaId: INVOICE_1,
                faturaNo: 'RNT2026000000001',
                aciklama: 'Kira 3 gün',
                miktar: 3,
                birimNetFiyat: 333.33,
                kdvOrani: 0.2,
                satirNet: 1000,
                satirKdv: 200,
                satirToplam: 1200,
                isaretliToplamTl: 1200,
                cikisOfisi: 'Merkez',
              },
            ]),
          );
        case '/api/ui/v1/cezalar':
          return json(r, page1([penaltyRow()]));
        case `/api/ui/v1/cezalar/${PENALTY_1}`:
          return json(r, e.penalty?.() ?? penaltyDetail());
        case '/api/ui/v1/giderler':
          return json(r, page1([expenseRow()]));
        case '/api/ui/v1/gelen-efatura':
          return json(r, page1([incomingRow()]));
        case `/api/ui/v1/gelen-efatura/${INCOMING_1}`:
          return json(r, e.incoming?.() ?? { fatura: incomingRow(), surum: 'v-1' });
        case '/api/ui/v1/satislar':
          return json(r, page1([saleRow()]));
        default:
          return r.fulfill({ status: 404, json: { kod: 'bulunamadi' } });
      }
    },
  );
  return written;
}
