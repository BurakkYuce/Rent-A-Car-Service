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

const period = (start: string | null = null, bit: string | null = null) => ({ bas: start, bit });
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

const balance = (name: string, bakiye: number) => ({
  cariId: CUSTOMER_1.replace(/1$/, String(name.length % 9)),
  ad: name,
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
    const tracking =
      key === 'arac-durum-takip'
        ? url.searchParams.get('gorunum') === 'arac'
          ? TRACKING_VEHICLES
          : TRACKING_DAYS
        : undefined;
    const body = bodies[key] ?? extraBodies[key] ?? tracking;
    return body ? json(route, body) : problem(route, 404, 'bulunamadi', 'Rapor yok.');
  });
  return requests;
}

const page1 = <T>(records: T[]) => ({
  kayitlar: records,
  toplam: records.length,
  sayfaNo: 1,
  boyut: 50,
  toplamSayfa: 1,
});

export const SCORECARD = {
  donem: period(),
  ozet: {
    baslik: {
      vehicleId: VEHICLE_1,
      plaka: '34 ABC 123',
      marka: 'Fiat',
      tip: 'Egea',
      grup: 'Ekonomi',
      segment: null,
      sube: 'Merkez',
      aracSahibi: null,
      durum: 'Musait',
      km: 45210,
      alimBedeli: 800000,
      alimTarihi: null,
      ikinciElDeger: null,
      filoGirisTarih: null,
      filoCikisTarih: null,
      sonBakimTarih: null,
      sonBakimKm: null,
    },
    toplamGelir: 9000,
    toplamGider: 3000,
    toplamNetKar: 6000,
    yillikPnl: [{ yil: 2026, gelir: 9000, gider: 3000, netKar: 6000 }],
    gelirKaynak: [{ kategori: 'Kira', tutar: 9000, yuzdeGelir: 100 }],
    giderKategori: [],
    olaylar: [
      {
        tarih: '2026-09-01T09:00:00Z',
        tur: 'Kira',
        aciklama: 'Sözleşme 1',
        tutar: 9000,
        deftereYansir: true,
      },
    ],
    kpi: {
      sahiplikGun: 60,
      kiralananGun: 37,
      servisGun: 0,
      bosGun: 23,
      dolulukYuzde: 61.7,
      revPacd: 150,
      adr: 243.24,
      kmBasinaMaliyet: null,
      netMarjYuzde: 66.7,
      roiYuzde: null,
      geriOdemeAy: null,
      tco: 803000,
      gerceklesenAmortisman: null,
      aylikAmortisman: null,
      ekonomikKar: -1000,
      toplamKatedilenKm: 3000,
      kiraSayisi: 4,
    },
    maliyetModel: null,
    tutSat: { sinyal: 0, gerekceler: [] },
    basaBasGunluk: null,
    kalinti: null,
    donemKm: null,
    donemKmMaliyet: null,
    vadeler: [],
    bakimKm: null,
  },
  export: links('arac-karne', `&vehicleId=${VEHICLE_1}`),
};

export const TRACKING_DAYS = {
  donem: period(),
  ozet: {
    gorunum: 'gun',
    gunler: [
      { gun: '2026-09-23T00:00:00Z', toplamArac: 10, dolu: 6, bakim: 1, bos: 3, toplamBaf: 0 },
    ],
  },
  satirlar: page1([]),
  export: null,
};

export const TRACKING_VEHICLES = {
  donem: period(),
  ozet: { gorunum: 'arac', gunler: [] },
  satirlar: page1([
    {
      vehicleId: VEHICLE_1,
      plaka: '34 ABC 123',
      sipp: null,
      grup: 'Ekonomi',
      sube: 'Merkez',
      aracSahibi: null,
      toplamGun: 30,
      doluGun: 20,
      bakimGun: 2,
      bafGun: 0,
      bosGun: 8,
    },
  ]),
  export: null,
};

export const COMPARATIVE = {
  donem: period(),
  ozet: {
    tablo: 'Kira',
    veriTuru: 'Adet',
    kirilim: 'AracGrubu',
    ayAnahtarlari: ['2026-08', '2026-09'],
    satirlar: [{ kirilim: 'Ekonomi', aylar: { '2026-08': 3, '2026-09': 5 }, toplam: 8 }],
    ayToplamlari: [3, 5],
    genelToplam: 8,
  },
  export: null,
};

export const SHIFTS = {
  bas: '2026-09-21',
  bit: '2026-09-22',
  kirpildi: true,
  gunler: ['2026-09-21', '2026-09-22'],
  matris: [
    {
      personelId: 'f1f1f1f1-0000-4000-8000-000000000001',
      personelAd: 'Ali Veli',
      gunler: [
        {
          gun: '2026-09-21',
          vardiyalar: [
            {
              id: 'f2f2f2f2-0000-4000-8000-000000000001',
              personelId: 'f1f1f1f1-0000-4000-8000-000000000001',
              personelAd: 'Ali Veli',
              personelKadroSube: 'Merkez',
              tarih: '2026-09-21',
              baslangicSaat: '09:00:00',
              bitisSaat: '17:00:00',
              sureDk: 480,
              aralik: '09:00–17:00',
              sube: 'Merkez',
              aciklama: null,
            },
          ],
        },
      ],
      toplamDk: 480,
      toplamSaatMetni: '8 sa',
    },
  ],
  toplamVardiya: 1,
  toplamDk: 480,
  liste: [],
};

/** İkinci grup (filo/operasyon) sahteleri; `arac-durum-takip` sabit `gorunum` parametresine göre. */
export const extraBodies: Record<string, unknown> = {
  [`arac-karne/${VEHICLE_1}`]: SCORECARD,
  'karsilastirmali-analiz': COMPARATIVE,
  'personel-calisma': SHIFTS,
};
