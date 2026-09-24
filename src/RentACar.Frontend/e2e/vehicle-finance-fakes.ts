import type { Page, Route } from '@playwright/test';

import { kaydet, type KayitliIstek } from './ortak';

/** F6.2b araç finans ekranları için sahte `/api/ui/v1` (değerler elle kurulmuş; uygulama kodundan türetilmez). */
export const LOAN_1 = 'b1b1b1b1-0000-4000-8000-000000000001';
export const INSTALLMENT_1 = 'd0d0d0d0-0000-4000-8000-000000000001';
export const ORDER_1 = 'e1e1e1e1-0000-4000-8000-000000000001';
export const ALLOCATION_1 = 'f1f1f1f1-0000-4000-8000-000000000001';
export const DAMAGE_1 = 'f2f2f2f2-0000-4000-8000-000000000001';
export const PLAN_1 = 'f3f3f3f3-0000-4000-8000-000000000001';
const CARI_1 = 'c0c0c0c0-0000-4000-8000-000000000001';
const VEHICLE_1 = 'a1a1a1a1-0000-4000-8000-000000000001';

/** 12 taksit × 2.500 TRY, 3'ü ödenmiş → sonraki 4. taksit (vade 15.12.2026). */
export function loanDetail(paid = 3): Record<string, unknown> {
  const taksitler = Array.from({ length: 12 }, (_, i) => ({
    sira: i + 1,
    vade: `2026-${String(((8 + i) % 12) + 1).padStart(2, '0')}-14T21:00:00Z`,
    tutar: 2500,
    odendi: i < paid,
  }));
  return {
    id: LOAN_1,
    no: 'KR-000001',
    bankaAdi: 'Ziraat',
    vehicleId: VEHICLE_1,
    plaka: '34ABC123',
    cariId: null,
    cariAd: null,
    dosyaNo: 'D-7',
    krediTutari: 25000,
    faizOran: 0.2,
    taksitSayisi: 12,
    odenenTaksit: paid,
    baslangicTarihi: '2026-09-14T21:00:00Z',
    doviz: 'TRY',
    kur: 1,
    durum: 'Aktif',
    aciklama: null,
    ozet: {
      toplamFaiz: 5000,
      toplamGeriOdeme: 30000,
      aylikTaksit: 2500,
      odenenTutar: paid * 2500,
      kalanBakiye: 30000 - paid * 2500,
      taksitler,
      sonVadeGunu: '2027-08-14T21:00:00Z',
      sonTaksitTutari: 2500,
      buAyToplamTaksit: 2500,
    },
    sonrakiTaksit:
      paid < 12 ? { sira: paid + 1, vade: taksitler[paid]?.vade ?? null, tutar: 2500 } : null,
    yetkiler: { taksitOde: paid < 12, iptal: true },
  };
}

export function loanRow(): Record<string, unknown> {
  return {
    id: LOAN_1,
    no: 'KR-000001',
    bankaAdi: 'Ziraat',
    vehicleId: VEHICLE_1,
    plaka: '34ABC123',
    cariId: null,
    cariAd: null,
    dosyaNo: 'D-7',
    krediTutari: 25000,
    faizOran: 0.2,
    taksitSayisi: 12,
    odenenTaksit: 3,
    baslangicTarihi: '2026-09-14T21:00:00Z',
    doviz: 'TRY',
    durum: 'Aktif',
    toplamGeriOdeme: 30000,
    aylikTaksit: 2500,
    kalanBakiye: 22500,
    sonVadeGunu: '2027-08-14T21:00:00Z',
  };
}

export function installment(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    id: INSTALLMENT_1,
    sira: 1,
    cariId: CARI_1,
    cariAd: 'Ayşe Yılmaz',
    vehicleId: null,
    plaka: null,
    vehicleSaleId: null,
    vade: '2026-10-14T21:00:00Z',
    taksitTutari: 1250.5,
    doviz: 'TRY',
    kur: 1,
    tutarBaz: 1250.5,
    durum: 'Bekliyor',
    gecikti: false,
    odemeTarihi: null,
    aciklama: 'Peşinat sonrası',
    surum: 'ts-1',
    ...extra,
  };
}

export function order(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    id: ORDER_1,
    no: 'SP-000001',
    durum: 'Bekliyor',
    surum: 'sp-1',
    tedarikci: 'Bayi A',
    tedarikciCariId: null,
    tedarikciCariAd: null,
    siparisTarihi: '2026-09-01T21:00:00Z',
    beklenenTeslim: null,
    imzaTarih: null,
    dosyaNo: null,
    satisTemsilci: null,
    ozelTemsilci: null,
    marka: 'Fiat',
    tip: 'Egea',
    grup: 'Ekonomik',
    versiyon: '1.4 Urban',
    opsiyon: null,
    renk: 'Beyaz',
    icRenk: 'Siyah',
    kaynakTip: 'Filo',
    satisTipi: 'Sıfır',
    tsbKayitNo: 'TSB-9',
    krediId: LOAN_1,
    adet: 2,
    birimFiyat: 750000,
    toplam: 1500000,
    piyasaFiyat: 800000,
    opsFiyat: null,
    filoFiyat: null,
    doviz: 'TRY',
    kur: 1,
    aciklama: null,
    yetkiler: { duzenle: true, onayla: true, teslimAl: true, iptal: true },
    ...extra,
  };
}

export function allocation(): Record<string, unknown> {
  return {
    id: ALLOCATION_1,
    no: 'BAF-000001',
    durum: 'Acik',
    personelId: 'a3a3a3a3-0000-4000-8000-000000000001',
    personelAd: 'Ali Veli',
    vehicleId: VEHICLE_1,
    plaka: '34ABC123',
    cikisTarihi: '2026-09-20T07:00:00Z',
    cikisSaat: '10:00:00',
    cikisKm: 12000,
    cikisYakit: 50,
    sube: 'Merkez',
    donusTarihi: null,
    donusSaat: null,
    donusKm: null,
    donusYakit: null,
    donusSube: null,
    kullanimAmaci: 'Yikama',
    onaylayan: null,
    onaylayanAd: null,
    kirayaVer: false,
    aciklama: null,
  };
}

export function damageFile(): Record<string, unknown> {
  return {
    id: DAMAGE_1,
    no: 'HD-000001',
    durum: 'Acik',
    vehicleId: VEHICLE_1,
    plaka: '34ABC123',
    rentalId: null,
    cariId: null,
    cariAd: null,
    acilisTarihi: '2026-09-20T07:00:00Z',
    aciklama: 'Ön tampon',
    tahminiTutar: 4500.75,
    onayNotu: null,
    yetkiler: { onayaGonder: true, onayla: false, reddet: false, kapat: false },
  };
}

export function fleetPlan(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    id: PLAN_1,
    aracGrupAdi: 'EKO',
    sipp: null,
    donem: '2026-Q4',
    hedefAdet: 10,
    aciklama: null,
    gerceklesen: 7,
    toplamKayitli: 9,
    fark: 3,
    durum: 'Eksik',
    surum: 'fp-1',
    ...extra,
  };
}

const page1 = (kayitlar: unknown[], boyut = 50) => ({
  kayitlar,
  toplam: kayitlar.length,
  sayfaNo: 1,
  boyut,
});

export interface FinanceEndpoints {
  /** Yazma istekleri (POST/PUT/DELETE) için özel yanıt; true = yanıtlandı, false = varsayılan. */
  readonly write?: (route: Route, path: string) => Promise<boolean> | boolean;
  readonly loan?: () => Record<string, unknown>;
  readonly installment?: () => Record<string, unknown>;
}

/** Araç finans uçlarının sahteleri; yazılan istekler (gövde + anahtar) sırayla döner. */
export async function financeEndpoints(
  page: Page,
  e: FinanceEndpoints = {},
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
    (u) =>
      u.pathname.startsWith('/api/ui/v1/secim/') ||
      u.pathname.startsWith('/api/ui/v1/araclar/secim/'),
    (r) => {
      const p = new URL(r.request().url()).pathname;
      if (p.startsWith('/api/ui/v1/secim/musteri'))
        return json(r, [{ id: CARI_1, etiket: 'Ayşe Yılmaz' }]);
      if (p.startsWith('/api/ui/v1/secim/arac'))
        return json(
          r,
          p.includes('grubu') ? [] : [{ id: VEHICLE_1, etiket: '34ABC123', plaka: '34ABC123' }],
        );
      return json(r, []);
    },
  );
  const handled = [
    '/api/ui/v1/arac-kredileri',
    '/api/ui/v1/musteri-taksitleri',
    '/api/ui/v1/arac-siparisleri',
    '/api/ui/v1/baflar',
    '/api/ui/v1/hasar-dosyalari',
    '/api/ui/v1/filo-plan',
  ];
  await page.route(
    (u) => handled.some((h) => u.pathname.startsWith(h)),
    async (r) => {
      const path = new URL(r.request().url()).pathname;
      const method = r.request().method();
      if (method !== 'GET') {
        written.push(kaydet(r.request()));
        if (await e.write?.(r, path)) return;
        return defaultWrite(r, path, method);
      }
      return defaultRead(r, path);
    },
  );

  async function defaultRead(r: Route, path: string) {
    const loan = e.loan?.() ?? loanDetail();
    const inst = e.installment?.() ?? installment();
    switch (path) {
      case '/api/ui/v1/arac-kredileri':
        return json(r, page1([loanRow()]));
      case '/api/ui/v1/arac-kredileri/ozet':
        return json(r, {
          toplamFaiz: 5000,
          sonVadeGunu: '2027-08-14T21:00:00Z',
          sonTaksitTutari: 2500,
          buAyToplamTaksit: 2500,
          toplamKrediBorcu: 22500,
        });
      case `/api/ui/v1/arac-kredileri/${LOAN_1}`:
        return json(r, loan);
      case '/api/ui/v1/musteri-taksitleri':
        return json(r, page1([{ ...inst, surum: null }]));
      case '/api/ui/v1/musteri-taksitleri/ozet':
        return json(r, {
          adet: 1,
          odenenAdet: 0,
          gecikenAdet: 0,
          toplamBaz: 1250.5,
          odenenBaz: 0,
          kalanBaz: 1250.5,
        });
      case `/api/ui/v1/musteri-taksitleri/${INSTALLMENT_1}`:
        return json(r, inst);
      case '/api/ui/v1/arac-siparisleri':
        return json(r, page1([order()]));
      case `/api/ui/v1/arac-siparisleri/${ORDER_1}`:
        return json(r, order());
      case '/api/ui/v1/baflar':
        return json(r, page1([allocation()]));
      case '/api/ui/v1/hasar-dosyalari':
        return json(r, page1([damageFile()]));
      case '/api/ui/v1/filo-plan':
        return json(r, page1([{ ...fleetPlan(), surum: null }], 200));
      case `/api/ui/v1/filo-plan/${PLAN_1}`:
        return json(r, fleetPlan());
      default:
        return r.fulfill({ status: 404, json: { kod: 'bulunamadi' } });
    }
  }

  async function defaultWrite(r: Route, path: string, method: string) {
    if (path.endsWith('/taksit-ode'))
      return json(r, {
        giderId: 'g1',
        giderNo: 'GD-000010',
        sira: 4,
        tutar: 2500,
        doviz: 'TRY',
        kredi: loanDetail(4),
      });
    if (path === '/api/ui/v1/musteri-taksitleri' && method === 'POST')
      return json(r, { id: INSTALLMENT_1 }, 201);
    if (path.startsWith('/api/ui/v1/musteri-taksitleri/'))
      return json(r, installment({ surum: 'ts-2' }));
    return json(r, {}, method === 'POST' ? 201 : 200);
  }

  return written;
}
