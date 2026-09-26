import type { Page, Route } from '@playwright/test';

import { kaydet, type KayitliIstek } from './ortak';

/**
 * F9.2 servis / sigorta / vade / fiyat-tarife ekranları için sahte `/api/ui/v1`. Değerler ELLE kurulmuş senaryo
 * (bağımsız oracle): MTV 1.500 − 500 ödenmiş = kalan 1.000; kalem 100 × 2 = 200 net, %20 KDV 40, genel 240.
 */
export const VEHICLE_1 = 'a1a1a1a1-0000-4000-8000-000000000001';
export const MTV_1 = '11111111-0000-4000-8000-000000000001';
export const INSPECTION_1 = '22222222-0000-4000-8000-000000000001';
export const POLICY_1 = '33333333-0000-4000-8000-000000000001';
export const SERVICE_1 = '44444444-0000-4000-8000-000000000001';
export const RATE_1 = '55555555-0000-4000-8000-000000000001';
export const OFFER_1 = '66666666-0000-4000-8000-000000000001';
const ACCOUNT_1 = 'c0c0c0c0-0000-4000-8000-000000000001';

export const page1 = (records: unknown[]) => ({
  kayitlar: records,
  toplam: records.length,
  sayfaNo: 1,
  boyut: 50,
  toplamSayfa: 1,
});

export function mtvDetail(remaining = 1000): Record<string, unknown> {
  return {
    mtv: {
      id: MTV_1,
      vehicleId: VEHICLE_1,
      plaka: '34ABC123',
      donem: '2026-1',
      tutar: 1500,
      kalan: remaining,
      vade: '2026-10-30T21:00:00Z',
      odendi: remaining === 0,
      aciklama: null,
    },
    odemeler: [
      {
        id: 'o1',
        sira: 1,
        tarih: '2026-09-01T09:00:00Z',
        tutar: 500,
        ceza: 0,
        kalanSonrasi: 1000,
        hesap: 'Kasa',
        hesapId: null,
        kasaKodu: null,
        hesapNo: null,
        evrakNo: 'EV-1',
        islemYapan: null,
        aciklama: null,
      },
    ],
    yetkiler: { odeyebilir: remaining > 0, duzenleyebilir: true },
  };
}

export function inspectionDetail(): Record<string, unknown> {
  return {
    muayene: {
      id: INSPECTION_1,
      vehicleId: VEHICLE_1,
      plaka: '34ABC123',
      muayeneTarihi: '2026-09-09T21:00:00Z',
      bitis: '2028-09-09T21:00:00Z',
      ucret: 800,
      ceza: 0,
      kalan: 800,
      islemKm: 45000,
      odendi: false,
      aciklama: null,
    },
    odemeler: [],
    yetkiler: { odeyebilir: true, duzenleyebilir: true },
  };
}

export function policyRow(paid = false): Record<string, unknown> {
  return {
    id: POLICY_1,
    vehicleId: VEHICLE_1,
    plaka: '34ABC123',
    tip: 'Kasko',
    baslangic: '2026-01-14T21:00:00Z',
    bitis: '2027-01-14T21:00:00Z',
    prim: 12000,
    zeyilPrim: 0,
    doviz: 'TRY',
    policeNo: 'PL-77',
    firma: 'Anadolu',
    acenta: null,
    aracDegeri: 900000,
    immDegeri: null,
    aksesuarDegeri: null,
    kalan: paid ? 0 : 12000,
    odendi: paid,
  };
}

export function policyDetail(paid = false): Record<string, unknown> {
  return {
    police: policyRow(paid),
    zeyiller: [
      {
        id: 'z1',
        policyId: POLICY_1,
        zeyilNo: 'Z-1',
        tarih: '2026-03-01T09:00:00Z',
        tanzim: null,
        deger: 0,
        brut: -150,
        net: -120,
        fonVergi: -30,
        tipi: 'Tenzil',
        neden: 'Sürücü değişikliği',
      },
    ],
    odeme: paid
      ? {
          tarih: '2026-09-20T09:00:00Z',
          tutar: 12000,
          doviz: 'TRY',
          kur: 1,
          tutarBaz: 12000,
          hesap: 'Kasa',
          hesapId: null,
        }
      : null,
    yetkiler: { odeyebilir: !paid, duzenleyebilir: true },
  };
}

export function serviceRow(): Record<string, unknown> {
  return {
    id: SERVICE_1,
    no: 'SR-000001',
    vehicleId: VEHICLE_1,
    plaka: '34ABC123',
    tip: 'Periyodik',
    durum: 'Serviste',
    girisTarihi: '2026-09-20T07:00:00Z',
    cikisTarihi: null,
    girisKm: 45000,
    cikisKm: null,
    atolyeAdi: 'Usta Oto',
    hasarSorumlu: 'Yok',
    kusurOrani: null,
    toplamIscilik: 200,
    yansitildi: false,
    planBasTarihi: null,
    planBitTarihi: null,
    kdvToplam: 40,
    genelToplam: 240,
    faturaNo: 'F-77',
  };
}

/** Durum sayaçları (`/servisler/sayaclar`): elle — 1 serviste, 2 açık. */
export const SERVICE_COUNTS = {
  tumu: 3,
  durumlar: [
    { durum: 'Acik', adet: 2 },
    { durum: 'Serviste', adet: 1 },
    { durum: 'Tamamlandi', adet: 0 },
    { durum: 'Iptal', adet: 0 },
    { durum: 'Rezerve', adet: 0 },
  ],
};

export function endorsementRow(): Record<string, unknown> {
  return {
    id: 'z1z1z1z1-0000-4000-8000-000000000001',
    policyId: POLICY_1,
    vehicleId: VEHICLE_1,
    plaka: '34ABC123',
    policeNo: 'P-100',
    firma: 'Anadolu',
    policeTipi: 'Kasko',
    zeyilNo: 'Z-7',
    tarih: '2026-09-10T00:00:00Z',
    tanzim: null,
    deger: 0,
    brut: 120,
    net: 100,
    fonVergi: 20,
    tipi: 'Zam',
    neden: 'Araç değeri',
  };
}

export function serviceDetail(version = 'sv-1', lines = 1): Record<string, unknown> {
  const info = Object.fromEntries(
    [
      'aciklama',
      'beyanTuru',
      'karsiPlaka',
      'karsiTrafikSigortasi',
      'kazaTarihi',
      'kazaSorumlusu',
      'hasarDosyaNo',
      'degerKaybi',
      'faturaTarihi',
      'faturaNo',
      'faturaTutar',
      'faturaKdv',
      'faturaGenelToplam',
      'odemeTarihi',
      'odeme',
      'odemeDoviz',
      'odemeKur',
      'odemeTuru',
      'kasaKodu',
      'hesapNo',
      'cikisYakit',
      'donusYakit',
      'planBasTarihi',
      'planBitTarihi',
    ].map((k) => [k, null]),
  );
  return {
    kayit: { ...serviceRow(), toplamIscilik: 200 * lines },
    bilgi: { ...info, atolyeAdi: 'Usta Oto' },
    kalemler: Array.from({ length: lines }, (_, i) => ({
      id: `l${i + 1}`,
      aciklama: 'Yağ',
      tutar: 200,
      birimFiyat: 100,
      miktar: 2,
      indirim: null,
      kdvOran: 0.2,
      brut: 200,
      kdvTutar: 40,
      genelToplam: 240,
    })),
    kdvToplam: 40 * lines,
    genelToplam: 240 * lines,
    yansitma: null,
    yetkiler: {
      serviseAlabilir: false,
      baslatabilir: false,
      tamamlayabilir: true,
      iptalEdebilir: true,
      kalemEkleyebilir: true,
      yansitabilir: false,
      yansitilacakTutar: null,
    },
    surum: version,
  };
}

export function rateCard(version: string | null = null, extra: Record<string, unknown> = {}) {
  return {
    id: RATE_1,
    kod: 'B-STD',
    ad: 'B Grubu Standart',
    grup: 'EKO',
    minGun: 1,
    maxGun: 9999,
    gunlukUcret: 1250.5,
    doviz: 'TRY',
    gecerliBas: '2026-09-30T21:00:00Z',
    gecerliBit: null,
    scdwDahil: false,
    miniHasarDahil: false,
    hirsizlikDahil: false,
    scdwZorunlu: false,
    gosterme: false,
    tarifeGrubuId: null,
    aktif: true,
    surum: version,
    ...extra,
  };
}

export interface Fakes {
  /** Yazma isteği (POST/PUT/DELETE): `true` → yanıtlandı; yoksa varsayılan 200/201. */
  readonly write?: (route: Route, path: string, method: string) => Promise<boolean>;
  readonly mtv?: () => Record<string, unknown>;
  readonly service?: () => Record<string, unknown>;
  readonly rate?: () => Record<string, unknown>;
}

const QUOTE = {
  gun: 3,
  hediyeGun: 0,
  faturalananGun: 3,
  gunlukUcret: 1000,
  bazTutar: 3000,
  haftaSonuFark: 0,
  kmAsimTutar: 0,
  sigortaToplam: 300,
  araToplam: 3300,
  iskontoOran: 10,
  iskontoTutar: 330,
  genelToplam: 2970,
  paraBirimi: 'TRY',
  tarifeKodu: 'WEB-EKO',
  provizyon: 5000,
  muafiyet: 10000,
  gencSurucu: false,
  sigortaKalemleri: [
    { kod: 'SCDW', ad: 'Süper hasar', tur: 'Scdw', birimUcret: 100, gun: 3, tutar: 300 },
  ],
  notlar: ['Kampanya YAZ uygulandı.'],
};

const COST = {
  residualDeger: 300000,
  netAmortisman: 700000,
  finansmanFaiz: 0,
  finansmanVergi: 0,
  damga: 0,
  toplamGider: 36000,
  toplamMaliyet: 736000,
  basaBasAylik: 20444.44,
  kar: 147200,
  teklifNet: 883200,
  teklifAylikNet: 24533.33,
  teklifKdvli: 1059840,
  aracSayisi: 1,
  filoToplamMaliyet: 736000,
  filoTeklifNet: 883200,
  filoTeklifAylikNet: 24533.33,
  filoTeklifKdvli: 1059840,
  kalemler: [{ ad: 'Kasko', periyot: 'Yillik', birim: 12000, donemTutar: 36000 }],
};

/** Tüm F9.2 uçlarının sahteleri; yazılan istekler (gövde + anahtar) sırayla döner. */
export async function serviceInsuranceEndpoints(
  page: Page,
  e: Fakes = {},
): Promise<KayitliIstek[]> {
  const written: KayitliIstek[] = [];
  const json = (route: Route, body: unknown, status = 200) => route.fulfill({ status, json: body });
  await page.route('**/api/ui/v1/tablo-duzenleri/**', (r) =>
    json(r, { tabloKodu: 'x', duzen: null, guncellemeUtc: null }),
  );
  await page.route('**/api/ui/v1/finans/hesaplar', (r) =>
    json(r, [
      { id: 'a4a4a4a4-0000-4000-8000-000000000001', etiket: 'Merkez Kasa', tur: 'Kasa' },
      { id: 'a4a4a4a4-0000-4000-8000-000000000002', etiket: 'Ziraat TL', tur: 'Banka' },
    ]),
  );
  await page.route(
    (u) => u.pathname.startsWith('/api/ui/v1/secim/'),
    (r) => {
      const p = new URL(r.request().url()).pathname;
      if (p.startsWith('/api/ui/v1/secim/musteri'))
        return json(r, [{ id: ACCOUNT_1, etiket: 'Ayşe Yılmaz' }]);
      if (p === '/api/ui/v1/secim/arac')
        return json(r, [{ id: VEHICLE_1, etiket: '34ABC123', plaka: '34ABC123' }]);
      if (p === '/api/ui/v1/secim/arac-grubu')
        return json(r, [{ id: 'g1', etiket: 'Ekonomik', kod: 'EKO' }]);
      if (p === '/api/ui/v1/secim/tarife-grubu')
        return json(r, [{ id: 'tg1', etiket: 'Broker X', kod: 'BRK-X' }]);
      return json(r, []);
    },
  );
  const handled = [
    '/api/ui/v1/servisler',
    '/api/ui/v1/regulasyon',
    '/api/ui/v1/vade',
    '/api/ui/v1/tarifeler',
    '/api/ui/v1/tarife-gruplari',
    '/api/ui/v1/sigorta-urunleri',
    '/api/ui/v1/ek-hizmetler',
    '/api/ui/v1/tarife-matris',
    '/api/ui/v1/kira-kurallari',
    '/api/ui/v1/broker-yasaklari',
    '/api/ui/v1/servis-tanimlari',
    '/api/ui/v1/fiyat-hesapla',
    '/api/ui/v1/maliyet-hesapla',
    '/api/ui/v1/maliyet-teklifleri',
    '/api/ui/v1/tarife-aktar',
  ];
  await page.route(
    (u) => handled.some((h) => u.pathname === h || u.pathname.startsWith(`${h}/`)),
    async (r) => {
      const path = new URL(r.request().url()).pathname;
      const method = r.request().method();
      const read = method === 'GET';
      if (!read) {
        written.push(kaydet(r.request()));
        if (await e.write?.(r, path, method)) return;
      }
      return respond(r, path, method);
    },
  );

  function respond(r: Route, path: string, method: string) {
    const mtv = e.mtv?.() ?? mtvDetail();
    const service = e.service?.() ?? serviceDetail();
    if (path === '/api/ui/v1/regulasyon/secenekler')
      return json(r, {
        firmalar: ['Anadolu'],
        zeyilTipleri: ['Zam', 'Tenzil'],
        dovizler: ['TRY', 'EUR'],
        sigortaTipleri: ['Trafik', 'Kasko'],
      });
    if (path === '/api/ui/v1/regulasyon/sigortalar')
      return json(r, page1([policyRow()]), method === 'POST' ? 201 : 200);
    if (path === `/api/ui/v1/regulasyon/sigortalar/${POLICY_1}`) return json(r, policyDetail());
    if (path === `/api/ui/v1/regulasyon/sigortalar/${POLICY_1}/odeme`)
      return json(r, policyDetail(true));
    if (path === '/api/ui/v1/regulasyon/zeyiller') return json(r, page1([endorsementRow()]));
    if (path === '/api/ui/v1/regulasyon/mtv')
      return json(r, page1([(mtv as { mtv: unknown }).mtv]));
    if (path === `/api/ui/v1/regulasyon/mtv/${MTV_1}`) return json(r, mtv);
    if (path === `/api/ui/v1/regulasyon/mtv/${MTV_1}/odeme`)
      return json(r, { odemeId: 'o2', sira: 2, tutar: 400, kalan: 600, odendi: false });
    if (path === '/api/ui/v1/regulasyon/muayeneler')
      return json(r, page1([(inspectionDetail() as { muayene: unknown }).muayene]));
    if (path === `/api/ui/v1/regulasyon/muayeneler/${INSPECTION_1}`)
      return json(r, inspectionDetail());
    if (path === '/api/ui/v1/vade')
      return json(r, {
        ozet: { gecmis: 1, yediGun: 0, otuzGun: 1, ileri: 1 },
        kalemler: page1([
          {
            vehicleId: VEHICLE_1,
            plaka: '34ABC123',
            tur: 'MTV',
            bitis: '2026-09-01T21:00:00Z',
            kalanGun: -23,
            kova: 'Gecmis',
          },
          {
            vehicleId: VEHICLE_1,
            plaka: '34ABC123',
            tur: 'Kasko',
            bitis: '2026-10-14T21:00:00Z',
            kalanGun: 20,
            kova: 'OtuzGun',
          },
          {
            vehicleId: VEHICLE_1,
            plaka: '34ABC123',
            tur: 'Muayene',
            bitis: '2028-09-09T21:00:00Z',
            kalanGun: 716,
            kova: 'Ileri',
          },
        ]),
      });
    if (path === '/api/ui/v1/servisler')
      return json(r, page1([serviceRow()]), method === 'POST' ? 201 : 200);
    if (path === '/api/ui/v1/servisler/sayaclar') return json(r, SERVICE_COUNTS);
    if (path.startsWith(`/api/ui/v1/servisler/${SERVICE_1}`)) return json(r, service);
    if (path === '/api/ui/v1/tarifeler')
      return json(r, page1([rateCard()]), method === 'POST' ? 201 : 200);
    if (path === `/api/ui/v1/tarifeler/${RATE_1}`) return json(r, e.rate?.() ?? rateCard('rc-1'));
    if (path === '/api/ui/v1/servis-tanimlari/oneriler')
      return json(r, [
        {
          marka: 'Renault',
          tip: 'Clio',
          yakit: 'Dizel',
          vites: 'Manuel',
          aracSayisi: 4,
          etiket: 'Renault · Clio · Dizel · Manuel',
          onerilenKod: 'RENAULT-CLIO',
        },
      ]);
    if (path === '/api/ui/v1/fiyat-hesapla') return json(r, QUOTE);
    if (path === '/api/ui/v1/maliyet-hesapla') return json(r, COST);
    if (path === '/api/ui/v1/maliyet-teklifleri')
      return json(r, {
        kayitlar: page1([
          {
            id: OFFER_1,
            kayitNo: 'MT-000001',
            baslik: 'X Filo',
            plaka: null,
            tarih: '2026-09-20T21:00:00Z',
            cariId: null,
            cariAd: null,
            hazirlayanId: null,
            aracSayisi: 1,
            teklifAylikNet: 24533.33,
            teklifKdvli: 1059840,
            filoTeklifAylikNet: 24533.33,
            filoTeklifKdvli: 1059840,
          },
        ]),
        ozet: { adet: 1, aracAdet: 1, filoAylikNet: 24533.33, filoKdvli: 1059840 },
      });
    if (path === '/api/ui/v1/tarife-aktar')
      return json(r, { satirlar: page1([]), bekleyen: 0, onayli: 0, silinecek: null });
    // Diğer tanım listeleri: boş sayfa (ekran iskeleti + axe).
    if (method === 'GET') return json(r, page1([]));
    return json(r, {}, method === 'POST' ? 201 : 200);
  }

  return written;
}

export { ACCOUNT_1 as CARI_1 };
