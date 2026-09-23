import type { Page, Route } from '@playwright/test';

import { kaydet, type KayitliIstek } from './ortak';

/** F6.2a araç ekranları için sahte `/api/ui/v1` (değerler elle kurulmuş; uygulama kodundan türetilmez). */
export const VEHICLE_1 = 'a1a1a1a1-0000-4000-8000-000000000001';
export const PHOTO_1 = 'f0f0f0f0-0000-4000-8000-000000000001';
export const OWNER_1 = 'b0b0b0b0-0000-4000-8000-000000000001';

export function listRow(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    id: VEHICLE_1,
    plaka: '34ABC123',
    marka: 'Fiat',
    tip: 'Egea',
    detayTipi: 'Sedan',
    grup: 'Ekonomik',
    segment: 'C',
    sipp: 'CDMD',
    modelYili: 2024,
    renk: 'Beyaz',
    vites: 'Manuel',
    yakit: 'Dizel',
    km: 12500,
    sube: 'Merkez',
    durum: 'Musait',
    filoDurum: 'Havuz',
    sasiNo: 'SASI1',
    motorNo: 'MOT1',
    motorGucu: 95,
    aracSahibi: 'Bizim',
    hgsNo: null,
    ogsNo: null,
    kasaTipi: null,
    sonBakimKm: 10000,
    alimBedeli: 850000.5,
    filoGirisTarih: '2024-01-14T21:00:00Z',
    filoCikisTarih: null,
    tescilTarihi: '2024-01-09T21:00:00Z',
    alimYapilanFirma: 'Bayi',
    ruhsatNo: 'R-1',
    ikinciElDeger: 700000,
    tsbKaskoDegeri: 900000,
    yedekAnahtar: true,
    karLastigi: false,
    lastikDurumu: null,
    zIzni: false,
    sonDurum: null,
    konum: null,
    takipNo: null,
    teypKodu: null,
    aciklama: null,
    aktifKiraSozlesmeNo: null,
    acikServis: false,
    acikBaf: false,
    satisVar: false,
    kaskoBitis: null,
    kaskoPrim: null,
    krediKurulusu: null,
    krediSonTarih: null,
    ...extra,
  };
}

export function vehicleCard(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    id: VEHICLE_1,
    surum: 'surum-1',
    subeId: null,
    createdAtUtc: '2024-01-01T00:00:00Z',
    updatedAtUtc: null,
    plaka: '34ABC123',
    marka: 'Fiat',
    tip: 'Egea',
    grup: 'Ekonomik',
    grupBilincliBos: false,
    durum: 'Musait',
    km: 12500,
    sube: 'Merkez',
    // 1995-03-10 İstanbul gece yarısı (o tarihte +02:00).
    tescilTarihi: '1995-03-09T22:00:00Z',
    alimBedeli: 850000.5,
    aracSahibi: 'Bizim',
    kiraMusteriId: null,
    alisEuro: null,
    webRezKapat: false,
    ofisRezKapat: false,
    zIzni: false,
    utts: false,
    karLastigi: false,
    yedekAnahtar: false,
    temizlik: false,
    rehin: false,
    aciklama: null,
    ...extra,
  };
}

export function vehicleDetail(): Record<string, unknown> {
  return {
    id: VEHICLE_1,
    plaka: '34ABC123',
    marka: 'Fiat',
    grup: 'Ekonomik',
    sube: 'Merkez',
    durum: 'Musait',
    km: 12500,
    kiralar: [
      {
        id: 'c0c0c0c0-0000-4000-8000-000000000001',
        sozlesmeNo: '2026230901001',
        basTar: '2026-09-01T07:00:00Z',
        bitTar: '2026-09-04T07:00:00Z',
        durum: 'Tamamlandi',
        genelToplam: 3600,
      },
    ],
    servisler: [],
    cezalar: [],
    hasarlar: [],
    kmKayitlari: [{ tarih: '2026-09-04T08:00:00Z', km: 12500, kaynak: 'Donus' }],
  };
}

export function statusRow(): Record<string, unknown> {
  return {
    vehicleId: VEHICLE_1,
    plaka: '34ABC123',
    marka: 'Fiat',
    tip: 'Egea',
    grup: 'Ekonomik',
    filoDurum: 'Havuz',
    durum: 'Kirada',
    aktifKiraId: 'c0c0c0c0-0000-4000-8000-000000000001',
    kiraSozlesmeNo: '2026230901001',
    musteriAd: 'Ayşe Yılmaz',
    musteriTel: '0532 000 00 00',
    kiraBitTar: '2026-09-25T07:00:00Z',
    kiraKalanGun: -2,
    kiraBakiye: 1250.5,
    rezMusteriAd: null,
    rezBasTar: null,
    acikServisNo: null,
    servisAtolye: null,
    aktifBafNo: null,
    bafPersonelAd: null,
    dosyaNo: null,
    pasifSebep: null,
    konum: null,
    takipNo: null,
    hgsNo: null,
    karLastigi: false,
    webRezKapat: true,
    ofisRezKapat: false,
    km: 12500,
    sube: 'Merkez',
    kirada: true,
    serviste: false,
    bafta: false,
  };
}

export interface VehicleEndpoints {
  /** Kart GET'i (varsayılan `vehicleCard()`). */
  readonly card?: () => unknown;
  /** POST /araclar ve PUT /araclar/{id} yanıtı (varsayılan 200/201 kart). */
  readonly write?: (route: Route) => Promise<void> | void;
}

/** Araç uçlarının sahteleri; yazılan istekler (gövde + anahtar) sırayla döner. */
export async function vehicleEndpoints(
  page: Page,
  e: VehicleEndpoints = {},
): Promise<KayitliIstek[]> {
  const written: KayitliIstek[] = [];
  const json = (route: Route, body: unknown) => route.fulfill({ json: body });
  await page.route('**/api/ui/v1/tablo-duzenleri/**', (r) =>
    json(r, { tabloKodu: 'x', duzen: null, guncellemeUtc: null }),
  );
  await page.route(
    (u) =>
      u.pathname.startsWith('/api/ui/v1/araclar/secim/') ||
      u.pathname.startsWith('/api/ui/v1/secim/'),
    (r) =>
      r.request().url().includes('varsayilan-grup')
        ? json(r, { ad: 'Ekonomik' })
        : r.request().url().includes('/secim/arac-grubu')
          ? json(r, [
              { id: 'd0d0d0d0-0000-4000-8000-000000000001', etiket: 'Ekonomik', kod: 'EKO' },
            ])
          : json(r, []),
  );
  await page.route(
    (u) => u.pathname.startsWith('/api/ui/v1/araclar'),
    (r) => {
      const path = new URL(r.request().url()).pathname;
      const method = r.request().method();
      const base = '/api/ui/v1/araclar';
      // Son kaydedilen rota önce eşleşir: seçim uçları yukarıdaki sahteye düşsün.
      if (path.startsWith(`${base}/secim/`)) return r.fallback();
      if (method !== 'GET') {
        written.push(kaydet(r.request()));
        if (e.write) return e.write(r);
        return method === 'POST' && path === base
          ? r.fulfill({ status: 201, json: vehicleCard() })
          : json(r, vehicleCard({ surum: 'surum-2' }));
      }
      if (path === base)
        return json(r, { kayitlar: [listRow()], toplam: 1, sayfaNo: 1, boyut: 50 });
      if (path === `${base}/ozet`)
        return json(r, { toplam: 1, musait: 1, serviste: 0, doluluk: 0 });
      if (path === `${base}/model-gruplari`)
        return json(r, {
          toplamArac: 1,
          kirpildi: false,
          gruplar: [
            {
              etiket: 'Fiat Egea',
              grup: 'Ekonomik',
              yilAralik: '2024',
              toplam: 1,
              musait: 1,
              kirada: 0,
              serviste: 0,
              araclar: [
                {
                  id: VEHICLE_1,
                  plaka: '34ABC123',
                  modelYili: 2024,
                  renk: 'Beyaz',
                  vites: 'Manuel',
                  yakit: 'Dizel',
                  km: 12500,
                  sube: 'Merkez',
                  durum: 'Musait',
                  sipp: 'CDMD',
                },
              ],
            },
          ],
        });
      if (path === `${base}/detayli`)
        return json(r, {
          kayitlar: [{ ...listRow(), aktifKiraMusteri: null, alisEuro: false, alimTarihi: null }],
          toplam: 1,
          sayfaNo: 1,
          boyut: 50,
        });
      if (path === `${base}/durum`)
        return json(r, {
          liste: { kayitlar: [statusRow()], toplam: 1, sayfaNo: 1, boyut: 100 },
          kirada: 1,
          serviste: 0,
          bafta: 0,
        });
      if (path === `${base}/${VEHICLE_1}/detay`) return json(r, vehicleDetail());
      if (path === `${base}/${VEHICLE_1}/fotograflar`)
        return json(r, [{ id: PHOTO_1, sira: 0, contentType: 'image/png' }]);
      if (path.startsWith(`${base}/${VEHICLE_1}/fotograflar/`))
        return r.fulfill({ status: 404, body: '' });
      if (path === `${base}/${VEHICLE_1}`) return json(r, e.card?.() ?? vehicleCard());
      return r.fulfill({ status: 404, json: { kod: 'bulunamadi' } });
    },
  );
  return written;
}
