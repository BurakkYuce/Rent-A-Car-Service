import type { Page, Route } from '@playwright/test';

import { kaydet, type KayitliIstek } from './ortak';

/** F8.2a finans ekranları için sahte `/api/ui/v1` (değerler elle kurulmuş; uygulama kodundan türetilmez). */
export const CARI_1 = 'c0c0c0c0-0000-4000-8000-000000000001';
export const CARI_2 = 'c0c0c0c0-0000-4000-8000-000000000002';
export const KALEM_1 = 'e0e0e0e0-0000-4000-8000-000000000001';
export const KALEM_2 = 'e0e0e0e0-0000-4000-8000-000000000002';
export const TX_1 = 'd1d1d1d1-0000-4000-8000-000000000001';
export const KIRA_1 = 'a0a0a0a0-0000-4000-8000-000000000001';
export const SABIT_1 = 'f5f5f5f5-0000-4000-8000-000000000001';

const json = (r: Route, body: unknown, status = 200) => r.fulfill({ status, json: body });

export function fixedRate(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    id: SABIT_1,
    kod: 'EUR',
    kur: 36.5,
    basTar: null,
    bitTar: null,
    aktif: true,
    surum: 'sk-1',
    ...extra,
  };
}

export interface FinanceFakes {
  /** Yazma isteği; `true` dönerse yanıt verilmiş sayılır. */
  readonly write?: (r: Route, path: string) => Promise<boolean>;
  readonly fixed?: () => Record<string, unknown>;
  readonly balance?: () => number;
}

export async function financeHubEndpoints(
  page: Page,
  e: FinanceFakes = {},
): Promise<KayitliIstek[]> {
  const written: KayitliIstek[] = [];
  await page.route('**/api/ui/v1/tablo-duzenleri/**', (r) => r.fulfill({ status: 204 }));
  await page.route(
    (u) => u.pathname.startsWith('/api/ui/v1/finans') || u.pathname.startsWith('/api/ui/v1/secim/'),
    async (r) => {
      const path = new URL(r.request().url()).pathname;
      const method = r.request().method();
      if (method !== 'GET') {
        written.push(kaydet(r.request()));
        if (await e.write?.(r, path)) return;
        return json(
          r,
          method === 'DELETE' || method === 'PUT' ? null : { id: 'yeni-1' },
          method === 'PUT' || method === 'DELETE' ? 204 : 200,
        );
      }
      return read(r, path);
    },
  );

  async function read(r: Route, path: string) {
    const bakiye = e.balance?.() ?? 1250.5;
    switch (path) {
      case '/api/ui/v1/secim/musteri':
        return json(r, [
          { id: CARI_1, etiket: 'Ayşe Yılmaz', kod: 'C-1' },
          { id: CARI_2, etiket: 'Bora Kaya', kod: 'C-2' },
        ]);
      case '/api/ui/v1/secim/arac':
        return json(r, [{ id: 'v1', etiket: '34ABC123', plaka: '34ABC123' }]);
      case '/api/ui/v1/secim/sube':
        return json(r, [{ id: 's1', etiket: 'Merkez' }]);
      case '/api/ui/v1/finans/hesaplar':
        return json(r, [
          {
            id: 'h1',
            etiket: 'Merkez Kasa',
            kod: 'K1',
            ad: 'Merkez Kasa',
            tur: 'Kasa',
            doviz: 'TRY',
          },
          { id: 'h2', etiket: 'Ziraat TL', kod: 'B1', ad: 'Ziraat TL', tur: 'Banka', doviz: 'TRY' },
        ]);
      case '/api/ui/v1/finans/kasa/ozet':
        return json(r, {
          kasaGiris: 5000,
          kasaCikis: 1500,
          kasaBakiye: 3500,
          bankaGiris: 12000,
          bankaCikis: 14000.25,
          bankaBakiye: -2000.25,
        });
      case '/api/ui/v1/finans/kasa/islemler':
        return json(r, {
          liste: {
            kayitlar: [
              {
                id: TX_1,
                no: '2026220905001',
                tarih: '2026-09-22T09:30:00Z',
                tip: 'Tahsilat',
                tersKayit: false,
                tersAlinanId: null,
                hesap: 'Kasa',
                hesapId: 'h1',
                hesapAd: 'Merkez Kasa',
                kanal: 'Masaüstü',
                cariId: CARI_1,
                cariAd: 'Ayşe Yılmaz',
                cariKod: 'C-1',
                kiraId: null,
                tutar: 750,
                doviz: 'TRY',
                kur: 1,
                tutarTl: 750,
                aciklama: 'Eylül',
              },
            ],
            toplam: 1,
            sayfaNo: 1,
            boyut: 50,
          },
          kesildi: false,
        });
      case '/api/ui/v1/finans/kasa/virmanlar':
        return json(r, [
          {
            id: 'vr1',
            tarih: '2026-09-21T08:00:00Z',
            kaynakTur: 'Kasa',
            kaynakHesapId: 'h1',
            kaynakHesapAd: 'Merkez Kasa',
            hedefTur: 'Banka',
            hedefHesapId: 'h2',
            hedefHesapAd: 'Ziraat TL',
            tutar: 1000,
            doviz: 'TRY',
            kur: 1,
            tutarTl: 1000,
            makbuzNo: 'MK-1',
            sube: 'Merkez',
            islemYapan: 'ayse',
            aciklama: null,
            kunyeVar: true,
          },
        ]);
      case `/api/ui/v1/finans/cariler/${CARI_1}/bakiye`:
        return json(r, { cariId: CARI_1, cariAd: 'Ayşe Yılmaz', bakiye, depozitoBakiye: 0 });
      case `/api/ui/v1/finans/cariler/${CARI_2}/bakiye`:
        return json(r, { cariId: CARI_2, cariAd: 'Bora Kaya', bakiye: -40, depozitoBakiye: 0 });
      case `/api/ui/v1/finans/cariler/${CARI_1}/ekstre`:
        return json(r, {
          cariId: CARI_1,
          cariAd: 'Ayşe Yılmaz',
          bakiye,
          devir: 0,
          yuruyenBakiyeMi: true,
          satirlar: [
            {
              id: 'l1',
              tarih: '2026-09-01T10:00:00Z',
              aciklama: 'Kira faturası',
              kaynak: 'Fatura',
              kaynakId: 'f1',
              doviz: 'TRY',
              tutar: 2000.5,
              kur: 1,
              borc: 2000.5,
              alacak: 0,
              yuruyen: 2000.5,
              kasaIslemId: null,
            },
            {
              id: 'l2',
              tarih: '2026-09-22T09:30:00Z',
              aciklama: 'Eylül',
              kaynak: 'Tahsilat',
              kaynakId: TX_1,
              doviz: 'TRY',
              tutar: 750,
              kur: 1,
              borc: 0,
              alacak: 750,
              yuruyen: 1250.5,
              kasaIslemId: TX_1,
            },
          ],
          ozet: null,
          toplamBorc: 2000.5,
          toplamAlacak: 750,
          dovizler: ['TRY'],
          kaynaklar: ['Fatura', 'Tahsilat'],
        });
      case `/api/ui/v1/finans/cariler/${CARI_1}/acik-kalemler`:
        return json(r, {
          cariId: CARI_1,
          cariAd: 'Ayşe Yılmaz',
          bakiye,
          acikToplam: 1300,
          kalemler: [
            {
              id: KALEM_1,
              tarih: '2026-09-01T10:00:00Z',
              kaynak: 'Fatura',
              aciklama: 'Kira',
              tutar: 1000,
              doviz: 'TRY',
              baz: 1000,
              kapanan: 0,
              kalan: 1000,
              kapali: false,
            },
            {
              id: KALEM_2,
              tarih: '2026-09-05T10:00:00Z',
              kaynak: 'Ceza',
              aciklama: 'HGS',
              tutar: 500,
              doviz: 'TRY',
              baz: 500,
              kapanan: 200,
              kalan: 300,
              kapali: false,
            },
          ],
        });
      case '/api/ui/v1/finans/cari-virmanlar':
        return json(r, [
          {
            id: 'cv1',
            tarih: '2026-09-20T09:00:00Z',
            vade: null,
            kaynakCariId: CARI_1,
            kaynakCariAd: 'Ayşe Yılmaz',
            hedefCariId: CARI_2,
            hedefCariAd: 'Bora Kaya',
            tutar: 300,
            doviz: 'TRY',
            kur: 1,
            tutarTl: 300,
            makbuzNo: null,
            sube: 'Merkez',
            islemYapan: 'ayse',
            aciklama: 'Aktarım',
          },
        ]);
      case '/api/ui/v1/finans/depozito':
        return json(r, [{ cariId: CARI_1, cariAd: 'Ayşe Yılmaz', bakiye: 2500 }]);
      case '/api/ui/v1/finans/donem-kapanis':
        return json(r, {
          kapanisTarihi: '2026-06-30',
          mizan: [
            { hesap: 'Kasa', ad: 'Kasa', borc: 5000, alacak: 1500, bakiye: 3500 },
            { hesap: 'Gelir', ad: 'Gelir', borc: 0, alacak: 3500, bakiye: -3500 },
          ],
          toplamBorc: 5000,
          toplamAlacak: 5000,
          toplamBakiye: 0,
          gelir: 3500,
          gider: 0,
          donemSonucu: 3500,
        });
      case '/api/ui/v1/finans/otomatik-tahsilat':
        return json(r, {
          jobAcik: false,
          adaylar: [
            {
              kiraId: KIRA_1,
              sozlesmeNo: '2026010901001',
              donemSira: 2,
              donemBas: '2026-08-31T21:00:00Z',
              donemBit: '2026-09-29T21:00:00Z',
              cariId: CARI_1,
              cariAd: 'Ayşe Yılmaz',
              sube: 'Merkez',
              doviz: 'TRY',
              kiraTutar: 15000,
              cariBakiye: 1250.5,
            },
          ],
          dovizToplamlari: [{ doviz: 'TRY', toplam: 15000 }],
        });
      case '/api/ui/v1/finans/kurlar':
        return json(r, {
          tcmb: [
            {
              kod: 'EUR',
              ad: 'Euro',
              birim: 1,
              forexAlis: 36.1,
              forexSatis: 36.3,
              efektifAlis: null,
              efektifSatis: null,
              tarih: '2026-09-22',
              sabitVar: true,
            },
            {
              kod: 'USD',
              ad: 'ABD Doları',
              birim: 1,
              forexAlis: 33.9,
              forexSatis: 34.05,
              efektifAlis: null,
              efektifSatis: null,
              tarih: '2026-09-22',
              sabitVar: false,
            },
          ],
          sabitler: [e.fixed?.() ?? fixedRate()],
          dovizKodlari: ['EUR', 'TRY', 'USD'],
        });
      case '/api/ui/v1/finans/kurlar/cevir':
        return json(r, { tutar: 100, kaynak: 'USD', hedef: 'TRY', sonuc: 3405 });
      default:
        return r.fulfill({ status: 404, json: { kod: 'bulunamadi' } });
    }
  }

  return written;
}
