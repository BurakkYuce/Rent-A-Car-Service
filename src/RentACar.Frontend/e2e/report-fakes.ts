import type { Page, Request, Route } from '@playwright/test';

import { problem } from './ortak';

/**
 * F10.2 rapor ekranlarının sahte `/api/ui/v1/raporlar/*` yanıtları. Biçim uçların zarfıyla birebir
 * (`{ donem, ozet, satirlar?, export }`); tutarlar ELLE kurulmuş senaryodan (bağımsız oracle) — ekran hesap yapmaz,
 * yalnız gösterir. Müşteri adı sunucuda maskeli gelir ("Anonim müşteri").
 */

export const VEHICLE_1 = 'b1b1b1b1-0000-4000-8000-000000000001';
export const RENTAL_1 = 'c1c1c1c1-0000-4000-8000-000000000001';
export const CUSTOMER_1 = 'd1d1d1d1-0000-4000-8000-000000000001';

const period = (bas: string | null = null, bit: string | null = null) => ({ bas, bit });
const links = (report: string, q = '') => ({
  excel: `/raporlar/export/${report}?format=excel${q}`,
  csv: `/raporlar/export/${report}?format=csv${q}`,
  pdf: null,
});

export const INCOME_EXPENSE = {
  donem: period(),
  ozet: {
    gelirToplam: 12500,
    giderToplam: 4200.5,
    kdvTahsil: 2500,
    kdvIndirilecek: 700,
    netKar: 8299.5,
    gelirKirilim: [{ sourceType: 'Fatura', tutar: 12500 }],
    giderKirilim: [{ sourceType: 'Gider', tutar: 4200.5 }],
  },
  export: links('gelir-gider'),
};

const ledgerLine = (i: number) => ({
  tarih: `2026-09-${String(10 + i).padStart(2, '0')}T09:00:00+00:00`,
  sourceType: 'Tahsilat',
  aciklama: `Tahsilat ${i}`,
  borc: 100 * i,
  alacak: 0,
  yuruyenBakiye: 100 * i,
  hesapId: null,
  native: 100 * i,
  doviz: 'TRY',
  cariAd: 'Anonim müşteri',
  belgeNo: `TH-${i}`,
  sube: 'Merkez',
  kanal: null,
  devirMi: false,
});

export const CASH_BANK = {
  donem: period(),
  ozet: {
    hesap: 'Kasa',
    toplam: {
      kasaGiris: 300,
      kasaCikis: 0,
      kasaBakiye: 300,
      bankaGiris: 0,
      bankaCikis: 50,
      bankaBakiye: -50,
    },
    hesaplar: [
      { tur: 'Kasa', hesapId: null, hesapAd: 'Merkez Kasa', giris: 300, cikis: 0, bakiye: 300 },
    ],
    secenekler: { dovizler: ['TRY'], turler: ['Tahsilat'], subeler: ['Merkez'] },
  },
  satirlar: {
    kayitlar: [ledgerLine(1), ledgerLine(2)],
    toplam: 2,
    sayfaNo: 1,
    boyut: 50,
    toplamSayfa: 1,
  },
  export: links('kasa-banka', '&hesap=Kasa'),
};

const balance = (ad: string, bakiye: number) => ({
  cariId: CUSTOMER_1.replace(/1$/, String(ad.length % 9)),
  ad,
  bakiye,
  toplamBorc: Math.max(bakiye, 0),
  toplamAlacak: Math.max(-bakiye, 0),
  telefon: null,
  email: null,
  banka: null,
  doviz: 'TRY',
  ozelKod: null,
  sinif: 'A',
  kurumsal: false,
  pasif: false,
});

export const CUSTOMER_BALANCE = {
  donem: period(),
  ozet: {
    borcluToplam: 1500,
    alacakliToplam: 200,
    ozelKodlar: ['VIP'],
    siniflar: ['A'],
    dovizler: ['TRY'],
  },
  satirlar: {
    kayitlar: [balance('Anonim müşteri', 1500), balance('Deniz Ltd', -200)],
    toplam: 2,
    sayfaNo: 1,
    boyut: 50,
    toplamSayfa: 1,
  },
  export: links('cari-bakiye'),
};

export const AGING = {
  donem: period(),
  ozet: {
    tarih: '2026-09-24',
    kovalar: { b0_30: 1000, b31_60: 300, b61_90: 0, b90Plus: 200 },
    toplam: 1500,
  },
  satirlar: {
    kayitlar: [
      {
        cariId: CUSTOMER_1,
        ad: 'Anonim müşteri',
        b0_30: 1000,
        b31_60: 300,
        b61_90: 0,
        b90Plus: 200,
        toplam: 1500,
      },
    ],
    toplam: 1,
    sayfaNo: 1,
    boyut: 50,
    toplamSayfa: 1,
  },
  export: null,
};

export const PROFITABILITY = {
  donem: period(),
  ozet: {
    toplamGelir: 9000,
    toplamGider: 3000,
    toplamNetKar: 6000,
    kdvDahil: false,
    toplamPotansiyelGelir: null,
    toplamReferansMaliyet: null,
    toplamHesaplananKdv: null,
  },
  satirlar: {
    kayitlar: [
      {
        vehicleId: VEHICLE_1,
        plaka: '34 ABC 123',
        sube: 'Merkez',
        grup: 'Ekonomi',
        segment: null,
        gelir: 9000,
        gider: 3000,
        netKar: 6000,
        sipp: null,
        otopark: null,
        rezKaynagi: null,
        cariAd: null,
        cariBakiye: null,
        referansAylikMaliyet: null,
        referansFiloYonetimMaliyeti: null,
        potansiyelGelir: null,
        hesaplananKdv: null,
        dolulukYuzde: 62.5,
        revPacd: 150,
        adr: 240,
        sahiplikGun: 60,
        kiralananGun: 37,
        kiraAdet: 4,
        referansToplamMaliyet: null,
        gelirKdvDahil: null,
      },
    ],
    toplam: 1,
    sayfaNo: 1,
    boyut: 50,
    toplamSayfa: 1,
  },
  export: links('karlilik'),
};

export interface ReportFakeOptions {
  /** Firma geneli uçlar 403 döner (şube kapsamlı kullanıcı). */
  readonly firmWideForbidden?: boolean;
}

/** Rapor uçları + tablo düzeni + seçim listeleri. İstekler (yol + sorgu) kaydedilir. */
export async function reportEndpoints(page: Page, o: ReportFakeOptions = {}): Promise<URL[]> {
  const requests: URL[] = [];
  const json = (route: Route, body: unknown) => route.fulfill({ json: body });
  await page.route('**/api/ui/v1/tablo-duzenleri/**', (r) =>
    json(r, { tabloKodu: 'x', duzen: null, guncellemeUtc: null }),
  );
  await page.route('**/api/ui/v1/finans/hesaplar', (r) =>
    json(r, [
      {
        id: 'a4a4a4a4-0000-4000-8000-000000000001',
        etiket: 'Merkez Kasa',
        kod: 'K1',
        ad: 'Merkez Kasa',
      },
    ]),
  );
  await page.route('**/api/ui/v1/secim/sube**', (r) =>
    json(r, [
      { id: 'e1e1e1e1-0000-4000-8000-000000000001', etiket: 'Merkez', kod: null },
      { id: 'e1e1e1e1-0000-4000-8000-000000000002', etiket: 'Havalimanı', kod: null },
    ]),
  );
  const bodies: Record<string, unknown> = {
    'gelir-gider': INCOME_EXPENSE,
    'kasa-banka': CASH_BANK,
    'cari-bakiye': CUSTOMER_BALANCE,
    'cari-bakiye/yaslandirma': AGING,
    karlilik: PROFITABILITY,
  };
  await page.route('**/api/ui/v1/raporlar/**', (route: Route, request: Request) => {
    const url = new URL(request.url());
    requests.push(url);
    const key = url.pathname.replace('/api/ui/v1/raporlar/', '');
    if (o.firmWideForbidden) {
      return problem(
        route,
        403,
        'yetki_yok',
        'Bu rapor firma genelidir; şube kapsamlı kullanıcıya kapalıdır.',
      );
    }
    const body = bodies[key] ?? extraBodies[key];
    return body ? json(route, body) : problem(route, 404, 'bulunamadi', 'Rapor yok.');
  });
  return requests;
}

/** İkinci grup (filo/operasyon) sahteleri buraya eklenir. */
export const extraBodies: Record<string, unknown> = {};
