import type { Page, Request, Route } from '@playwright/test';

/**
 * F5.2a rezervasyon + teklif ekranlarının sahte `/api/ui/v1` verisi (harness yalnız statik SPA sunar). Beklenen
 * değerler bu elle kurulmuş yanıtlardan gelir (formül yok).
 */
export const REZ_ID = '0b0e7c1a-7777-4aaa-8bbb-000000000007';
export const TEKLIF_ID = '0b0e7c1a-8888-4aaa-8bbb-000000000008';
export const MUSTERI_ID = '0b0e7c1a-2222-4aaa-8bbb-000000000002';
export const ARAC_ID = '0b0e7c1a-3333-4aaa-8bbb-000000000003';
export const KIRA_ID = '0b0e7c1a-1111-4aaa-8bbb-000000000001';

export function rezSatiri(no: string, ek: Record<string, unknown> = {}) {
  return {
    id: REZ_ID,
    no,
    musteriId: MUSTERI_ID,
    musteriAd: 'Ayşe Yılmaz',
    cepTel: '0532 000 00 00',
    vehicleId: ARAC_ID,
    plaka: '34 ABC 123',
    basTar: '2026-10-01T06:00:00Z',
    bitTar: '2026-10-04T06:00:00Z',
    cikisOfisi: 'Merkez',
    donusOfisi: 'Havalimanı',
    kaynak: 'Web',
    talepTuru: 'Kurumsal',
    geldigiBirim: 'Satış',
    projeAdi: 'Fuar',
    onayKodu: 'ONY-1',
    gun: 3,
    tutar: 3751.5,
    durum: 'Rezerv',
    ...ek,
  };
}

export function rezDetayi(ek: Record<string, unknown> = {}, yetki: Record<string, boolean> = {}) {
  return {
    rezervasyon: {
      id: REZ_ID,
      no: 'RZ-000042',
      durum: 'Rezerv',
      surum: '812',
      musteriId: MUSTERI_ID,
      vehicleId: ARAC_ID,
      basTar: '2026-10-01T06:00:00+00:00',
      bitTar: '2026-10-04T06:00:00+00:00',
      cikisOfisi: 'Merkez',
      donusOfisi: 'Havalimanı',
      gun: 3,
      gunlukUcret: 1250.5,
      tutar: 3751.5,
      hediyeGun: null,
      faturalananGun: 3,
      iskontoTutar: null,
      haftaSonuFark: null,
      fiyatTuru: 'Günlük',
      kampanyaKodu: null,
      kdvOranSnapshot: 0.2,
      kmLimit: 300,
      fazlaKmUcret: 2.5,
      yakitBirimUcret: 45,
      provizyon: 5000,
      depozito: null,
      komisyonOran: 10,
      komisyonTutar: null,
      dropUcreti: null,
      sonraOdeOran: null,
      kaynak: 'Web',
      aciklama: null,
      otaKiraBedeli: null,
      otaDropBedeli: null,
      otaBebekKoltugu: null,
      otaNavigasyon: null,
      otaLcf: null,
      otaCdw: null,
      otaScdw: null,
      otaEkSurucu: null,
      talepTuru: 'Kurumsal',
      geldigiBirim: 'Satış',
      onayKodu: 'ONY-1',
      projeAdi: 'Fuar',
      kiraId: null,
      olusturmaUtc: '2026-09-20T08:00:00+00:00',
      ...ek,
    },
    musteriAd: 'Ayşe Yılmaz',
    plaka: '34 ABC 123',
    yetkiler: { duzenle: true, onayla: true, kirayaCevir: true, iptal: true, ...yetki },
  };
}

export function teklifSatiri(no: string, ek: Record<string, unknown> = {}) {
  return {
    id: TEKLIF_ID,
    no,
    musteriId: MUSTERI_ID,
    musteriAd: 'Ayşe Yılmaz',
    vehicleId: ARAC_ID,
    plaka: '34 ABC 123',
    basTar: '2026-10-01T06:00:00Z',
    bitTar: '2026-10-04T06:00:00Z',
    gun: 3,
    tutar: 3600,
    gecerlilikTarihi: '2026-10-05T21:00:00Z',
    durum: 'Gonderildi',
    rezervasyonId: null,
    ...ek,
  };
}

export function teklifDetayi(ek: Record<string, unknown> = {}) {
  const acik = !('durum' in ek) || ek['durum'] === 'Taslak' || ek['durum'] === 'Gonderildi';
  return {
    teklif: {
      id: TEKLIF_ID,
      no: 'TK-000007',
      durum: 'Gonderildi',
      musteriId: MUSTERI_ID,
      vehicleId: ARAC_ID,
      basTar: '2026-10-01T06:00:00+00:00',
      bitTar: '2026-10-04T06:00:00+00:00',
      cikisOfisi: 'Merkez',
      donusOfisi: null,
      gun: 3,
      gunlukUcret: 1200,
      tutar: 3600,
      hediyeGun: null,
      faturalananGun: null,
      iskontoTutar: null,
      haftaSonuFark: null,
      fiyatTuru: 'Otomatik',
      kdvOranSnapshot: 0.2,
      kmLimit: 300,
      fazlaKmUcret: 2.5,
      yakitBirimUcret: 45,
      gecerlilikTarihi: '2026-10-05T21:00:00+00:00',
      aciklama: null,
      rezervasyonId: null,
      olusturmaUtc: '2026-09-20T08:00:00+00:00',
      ...ek,
    },
    musteriAd: 'Ayşe Yılmaz',
    plaka: '34 ABC 123',
    yetkiler: { gonder: false, kabul: acik, reddet: acik },
  };
}

const sayfa = (kayitlar: unknown[]) => ({
  kayitlar,
  toplam: kayitlar.length,
  sayfaNo: 1,
  boyut: 50,
});

export interface SahteSecenekler {
  /** `POST /rezervasyonlar` ve `PUT /rezervasyonlar/{id}` yanıtı (verilmezse 201/200). */
  readonly yazma?: (route: Route, istek: Request) => Promise<void> | void;
  /** `GET /rezervasyonlar/{id}` (her çağrıda). */
  readonly detay?: () => unknown;
  readonly rezSatirlari?: () => unknown[];
  readonly teklifSatirlari?: () => unknown[];
  readonly teklifDetay?: () => unknown;
  readonly teklifKabul?: (route: Route) => Promise<void> | void;
}

export interface Sahte {
  readonly listeIstekleri: URL[];
  readonly detayIstekleri: number[];
  readonly teklifDetayIstekleri: number[];
}

/** Rezervasyon + teklif + seçim uçlarının tamamı. */
export async function sahteRezervasyonApi(page: Page, s: SahteSecenekler = {}): Promise<Sahte> {
  const kayit: Sahte = { listeIstekleri: [], detayIstekleri: [], teklifDetayIstekleri: [] };
  await page.route('**/api/ui/v1/tablo-duzenleri/**', (route) =>
    route.fulfill({ json: { tabloKodu: 'x', duzen: null, guncellemeUtc: null } }),
  );
  await page.route('**/api/ui/v1/secim/**', (route) => {
    const yol = new URL(route.request().url()).pathname;
    if (yol.endsWith('/musteri'))
      return route.fulfill({ json: [{ id: MUSTERI_ID, etiket: 'Ayşe Yılmaz', tip: 'Bireysel' }] });
    if (yol.endsWith('/arac'))
      return route.fulfill({
        json: [
          { id: ARAC_ID, etiket: '34 ABC 123', plaka: '34 ABC 123', grup: 'C', durum: 'Musait' },
        ],
      });
    if (yol.endsWith('/lokasyon'))
      return route.fulfill({ json: [{ id: 'l-1', etiket: 'Merkez' }] });
    if (yol.endsWith('/rezervasyon-kaynagi'))
      return route.fulfill({ json: [{ id: 'k-1', etiket: 'Web' }] });
    return route.fulfill({ json: [] });
  });
  await page.route('**/api/ui/v1/rezervasyonlar/form-secenekleri', (route) =>
    route.fulfill({
      json: {
        varsayilanFiyatTuru: null,
        fiyatTurleri: ['Otomatik', 'KDV Dahil Günlük', 'Günlük', 'KDV Dahil Toplam', 'Toplam'],
        talepTurleri: ['Bireysel', 'Kurumsal', 'Sigorta İkame', 'Filo'],
      },
    }),
  );
  await page.route(
    (url) => url.pathname === '/api/ui/v1/rezervasyonlar',
    async (route, istek) => {
      if (istek.method() === 'GET') {
        kayit.listeIstekleri.push(new URL(istek.url()));
        return route.fulfill({ json: sayfa(s.rezSatirlari?.() ?? [rezSatiri('RZ-000042')]) });
      }
      if (s.yazma) return s.yazma(route, istek);
      return route.fulfill({ status: 201, json: { id: REZ_ID, no: 'RZ-000042' } });
    },
  );
  await page.route(`**/api/ui/v1/rezervasyonlar/${REZ_ID}`, async (route, istek) => {
    if (istek.method() === 'PUT') {
      if (s.yazma) return s.yazma(route, istek);
      return route.fulfill({ json: rezDetayi({ surum: '813' }) });
    }
    kayit.detayIstekleri.push(Date.now());
    return route.fulfill({ json: s.detay?.() ?? rezDetayi() });
  });
  await page.route(`**/api/ui/v1/rezervasyonlar/${REZ_ID}/onayla`, (route) =>
    route.fulfill({ json: rezDetayi({ durum: 'Onayli' }) }),
  );
  await page.route(`**/api/ui/v1/rezervasyonlar/${REZ_ID}/kiraya-cevir`, (route) =>
    route.fulfill({ json: { kiraId: KIRA_ID, sozlesmeNo: '2026230901001' } }),
  );
  await page.route(
    (url) => url.pathname === '/api/ui/v1/teklifler',
    (route, istek) =>
      istek.method() === 'GET'
        ? route.fulfill({
            json: sayfa(s.teklifSatirlari?.() ?? [teklifSatiri('TK-000007')]),
          })
        : route.fulfill({ status: 201, json: { id: TEKLIF_ID, no: 'TK-000007' } }),
  );
  await page.route(`**/api/ui/v1/teklifler/${TEKLIF_ID}`, (route) => {
    kayit.teklifDetayIstekleri.push(Date.now());
    return route.fulfill({ json: s.teklifDetay?.() ?? teklifDetayi() });
  });
  await page.route(`**/api/ui/v1/teklifler/${TEKLIF_ID}/kabul`, (route) =>
    s.teklifKabul
      ? s.teklifKabul(route)
      : route.fulfill({ json: { rezervasyonId: REZ_ID, rezervasyonNo: 'RZ-000042' } }),
  );
  return kayit;
}
