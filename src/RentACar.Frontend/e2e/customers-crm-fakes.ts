import type { Page, Route } from '@playwright/test';

import { kaydet, type KayitliIstek } from './ortak';

/** F7.2 cari + CRM ekranları için sahte `/api/ui/v1` (değerler elle kurulmuş; uygulama kodundan türetilmez). */
export const CARI_1 = 'c1c1c1c1-0000-4000-8000-000000000001';
export const CARI_2 = 'c1c1c1c1-0000-4000-8000-000000000002';
export const RENTAL_1 = 'b2b2b2b2-0000-4000-8000-000000000001';
export const SURVEY_1 = 'a5a5a5a5-0000-4000-8000-000000000001';
export const COMPLAINT_1 = 'a6a6a6a6-0000-4000-8000-000000000001';
export const ASSIST_1 = 'a7a7a7a7-0000-4000-8000-000000000001';
export const LEGAL_1 = 'a8a8a8a8-0000-4000-8000-000000000001';

/** Bireysel cari kartı: TC kayıtlı ama GİZLİ (yalnız `tcKimlikVar`), ehliyet maskeli, vergi no maskeli. */
export function card(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    id: CARI_1,
    surum: 'c-1',
    tcKimlikVar: true,
    ehliyetNoMaske: '****5678',
    pasaportNoMaske: null,
    vergiNoMaske: null,
    sifreVar: false,
    createdAtUtc: '2026-01-05T09:00:00Z',
    updatedAtUtc: null,
    tip: 'Bireysel',
    ad: 'Ayşe',
    soyad: 'Yılmaz',
    unvan: null,
    vergiDairesi: null,
    vergiNo: null,
    cepTel: '05321112233',
    email: 'ayse@example.com',
    il: 'İstanbul',
    ilce: 'Kadıköy',
    adres: 'Moda Cd. 1',
    kaynak: 'Web',
    iysIzinli: true,
    uyari: false,
    vadeGun: 30,
    riskLimiti: 15000.5,
    karaListe: false,
    pasif: false,
    mailIzin: true,
    smsIzin: null,
    telefonIzin: null,
    kisiler: [],
    anonimAd: false,
    anonimTc: false,
    anonimTelefon: false,
    anonimMail: false,
    anonimAdres: false,
    anonimBelge: false,
    aracVerilmez: false,
    islemSubeId: null,
    firmaId: null,
    ...extra,
  };
}

function row(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    id: CARI_1,
    tip: 'Bireysel',
    ad: 'Ayşe Yılmaz',
    anonim: false,
    vergiNo: null,
    cepTel: '05321112233',
    gsm2: null,
    email: 'ayse@example.com',
    il: 'İstanbul',
    ilce: 'Kadıköy',
    kaynak: 'Web',
    musteriTemsilcisi: null,
    entegrasyonKodu: null,
    ozelKod: null,
    sinif: 'VIP',
    ulke: null,
    vadeGun: 30,
    kiraAdet: 4,
    ciro: 12500.75,
    sonKira: '2026-08-20T09:00:00Z',
    karaListe: false,
    pasif: false,
    uyari: true,
    uyariNedeni: 'Ödeme gecikti',
    iysIzinli: true,
    aracVerilmez: false,
    ...extra,
  };
}

const page1 = (kayitlar: unknown[], boyut = 50) => ({
  kayitlar,
  toplam: kayitlar.length,
  sayfaNo: 1,
  boyut,
});

function detail(finance: boolean): Record<string, unknown> {
  return {
    musteri: {
      id: CARI_1,
      ad: 'Ayşe Yılmaz',
      tip: 'Bireysel',
      cepTel: '05321112233',
      email: 'ayse@example.com',
      ehliyetNoMaskeli: '****5678',
      pasaportNoMaskeli: null,
      ehliyetSinifi: 'B',
      ehliyetTarihi: null,
      ehliyetYeri: null,
      ehliyetUlke: null,
      pasaportYeri: null,
      adres: 'Moda Cd. 1',
      il: 'İstanbul',
      ilce: 'Kadıköy',
      musteriTipi: null,
      riskLimiti: 15000.5,
      karaListe: false,
      uyari: false,
      uyariNedeni: null,
    },
    karaListe: false,
    pasif: false,
    aracVerilmez: false,
    kiralar: [
      {
        id: RENTAL_1,
        sozlesmeNo: '2026260801001',
        basTar: '2026-08-26T09:00:00Z',
        bitTar: '2026-08-29T09:00:00Z',
        durum: 'Kapali',
        genelToplam: 3000,
        bakiye: 250,
        doviz: 'TL',
      },
    ],
    bakiye: finance ? 250 : null,
    hareketler: finance
      ? [
          {
            tarih: '2026-08-29T09:00:00Z',
            kaynak: 'Fatura',
            aciklama: 'Kira faturası',
            borc: 3000,
            alacak: null,
          },
          {
            tarih: '2026-08-30T09:00:00Z',
            kaynak: 'Tahsilat',
            aciklama: 'Nakit',
            borc: null,
            alacak: 2750,
          },
        ]
      : null,
  };
}

/** 3.000 borç − 2.750 alacak = 250 bakiye (devir 0). */
const STATEMENT = {
  cariId: CARI_1,
  cariAd: 'Ayşe Yılmaz',
  bakiye: 250,
  devir: 0,
  yuruyenBakiyeMi: true,
  satirlar: [
    {
      id: 'e1',
      tarih: '2026-08-29T09:00:00Z',
      aciklama: 'Kira faturası',
      kaynak: 'Fatura',
      kaynakId: 'f1',
      doviz: 'TRY',
      tutar: 3000,
      kur: 1,
      borc: 3000,
      alacak: 0,
      yuruyen: 3000,
      kasaIslemId: null,
    },
    {
      id: 'e2',
      tarih: '2026-08-30T09:00:00Z',
      aciklama: 'Nakit',
      kaynak: 'Tahsilat',
      kaynakId: 't1',
      doviz: 'TRY',
      tutar: 2750,
      kur: 1,
      borc: 0,
      alacak: 2750,
      yuruyen: 250,
      kasaIslemId: 't1',
    },
  ],
  ozet: null,
  toplamBorc: 3000,
  toplamAlacak: 2750,
  dovizler: ['TRY'],
  kaynaklar: ['Fatura', 'Tahsilat'],
};

export function survey(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    id: SURVEY_1,
    tarih: '2026-09-01T09:00:00Z',
    cariId: CARI_1,
    musteriAd: 'Ayşe Yılmaz',
    rentalId: RENTAL_1,
    sozlesmeNo: '2026260801001',
    anketTuru: 'Donus',
    durum: 'Yapildi',
    puan: 9,
    kaynak: 'Telefon',
    cikisOfisi: 'Merkez',
    yorum: 'Memnun',
    ...extra,
  };
}

export function complaint(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    id: COMPLAINT_1,
    tarih: '2026-09-02T09:00:00Z',
    konu: 'Araç kirli teslim edildi',
    detay: null,
    durum: 'Acik',
    cozum: null,
    cariId: CARI_1,
    musteriAd: 'Ayşe Yılmaz',
    musteriTel: '05321112233',
    rentalId: RENTAL_1,
    sozlesmeNo: '2026260801001',
    plaka: '34ABC123',
    teslimAlanPersonelId: null,
    teslimAlanAd: null,
    teslimEdenPersonelId: null,
    teslimEdenAd: null,
    puan: 2,
    sikayetKanali: 'Web',
    sikayetYeri: 'Kira',
    cikisOfisi: 'Merkez',
    ...extra,
  };
}

export function assistance(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    id: ASSIST_1,
    zaman: '2026-09-03T20:30:00Z',
    rentalId: null,
    sozlesmeNo: null,
    plaka: '34XYZ99',
    adSoyad: 'Mehmet Kaya',
    cepTel: '05550000000',
    mesaj: 'Lastik patladı',
    sebep: 'Lastik',
    yedekLastikMi: true,
    aracHareketMi: false,
    kapandi: false,
    cozum: null,
    ...extra,
  };
}

export function legalFile(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    id: LEGAL_1,
    dosyaNo: '2026/15',
    tarih: '2026-09-01T21:00:00Z',
    tur: 'Icra',
    durum: 'Acik',
    aktif: true,
    cariId: CARI_1,
    musteriAd: 'Ayşe Yılmaz',
    musteriTel: null,
    avukat: 'Av. Can',
    avukatTel: null,
    avukatMail: null,
    avukat2Ad: null,
    avukat2Tel: null,
    avukat2Mail: null,
    tutar: 10000,
    tahsilat: 2500,
    kalan: 7500,
    faturaNoTemp: 'RNT2026000000001',
    aciklama: null,
    ...extra,
  };
}

const ANALYSIS = {
  musteriSayisi: 1,
  toplamCiro: 12500.75,
  toplamHizmetBedeli: 400,
  segment: page1([
    {
      cariId: CARI_1,
      ad: 'Ayşe Yılmaz',
      mail: 'ayse@example.com',
      tel: '05321112233',
      kiraSayisi: 4,
      toplamCiro: 12500.75,
      ortalamaKiraBedeli: 3125.19,
      ortalamaKm: null,
      hizmetBedeli: 400,
      dogumTarihi: null,
      ilkKiraZamani: '2026-01-10T09:00:00Z',
      sonIslem: '2026-08-20T09:00:00Z',
      segment: 'Sadık',
    },
  ]),
  personel: [{ personelId: 'p1', ad: 'Ali Veli', tahsisSayisi: 3 }],
};

export interface Fakes {
  readonly card?: () => Record<string, unknown>;
  readonly finance?: boolean;
  readonly survey?: () => Record<string, unknown>;
  readonly complaint?: () => Record<string, unknown>;
  /** Yazma isteği: `true` dönerse işlendi sayılır; aksi varsayılan yanıt. */
  readonly write?: (route: Route, path: string, method: string) => Promise<boolean>;
}

/** Tüm cari + CRM uçlarını sahteler; YAZMA isteklerini (gövde + anahtar) sırayla döner. */
export async function customerCrmEndpoints(page: Page, e: Fakes = {}): Promise<KayitliIstek[]> {
  const written: KayitliIstek[] = [];
  const json = (route: Route, body: unknown, status = 200) => route.fulfill({ status, json: body });
  await page.route('**/api/ui/v1/tablo-duzenleri/**', (r) =>
    json(r, { tabloKodu: 'x', duzen: null, guncellemeUtc: null }),
  );
  await page.route(
    (u) => u.pathname.startsWith('/api/ui/v1/secim/'),
    (r) => {
      const p = new URL(r.request().url()).pathname;
      if (p.startsWith('/api/ui/v1/secim/musteri'))
        return json(r, p.endsWith(CARI_1) ? { id: CARI_1, etiket: 'Ayşe Yılmaz' } : []);
      return json(r, []);
    },
  );
  const handled = [
    '/api/ui/v1/cariler',
    '/api/ui/v1/finans/cariler',
    '/api/ui/v1/anketler',
    '/api/ui/v1/sikayetler',
    '/api/ui/v1/assistans-talepleri',
    '/api/ui/v1/hukuk-dosyalari',
    '/api/ui/v1/crm',
  ];
  await page.route(
    (u) => handled.some((h) => u.pathname.startsWith(h)),
    async (r) => {
      const path = new URL(r.request().url()).pathname;
      const method = r.request().method();
      if (method !== 'GET') {
        written.push(kaydet(r.request()));
        if (await e.write?.(r, path, method)) return;
        return json(
          r,
          method === 'DELETE' ? null : (e.card?.() ?? card()),
          method === 'POST' ? 201 : 200,
        );
      }
      return read(r, path);
    },
  );

  async function read(r: Route, path: string) {
    const sv = e.survey?.() ?? survey();
    const cp = e.complaint?.() ?? complaint();
    switch (path) {
      case '/api/ui/v1/cariler':
        return json(
          r,
          page1([
            row(),
            row({
              id: CARI_2,
              ad: 'Anonim müşteri',
              anonim: true,
              cepTel: null,
              email: null,
              uyari: false,
              uyariNedeni: null,
            }),
          ]),
        );
      case `/api/ui/v1/cariler/${CARI_1}`:
        return json(r, e.card?.() ?? card());
      case `/api/ui/v1/cariler/${CARI_1}/detay`:
        return json(r, detail(e.finance ?? true));
      case `/api/ui/v1/finans/cariler/${CARI_1}/ekstre`:
        return json(r, STATEMENT);
      case '/api/ui/v1/anketler':
        return json(r, page1([{ ...sv, surum: null }]));
      case '/api/ui/v1/anketler/varsayilan-sorular':
        return json(r, ['Araç temiz miydi?', 'Personel ilgili miydi?']);
      case `/api/ui/v1/anketler/${SURVEY_1}`:
        return json(r, {
          anket: sv,
          surum: sv['surum'] ?? 'a-1',
          cevaplar: [{ soruNo: 1, soru: 'Araç temiz miydi?', cevap: 'Evet', aciklama: null }],
        });
      case '/api/ui/v1/sikayetler':
        return json(r, page1([cp]));
      case `/api/ui/v1/sikayetler/${COMPLAINT_1}`:
        return json(r, { sikayet: cp, surum: cp['surum'] ?? 's-1' });
      case '/api/ui/v1/assistans-talepleri':
        return json(r, page1([assistance()]));
      case `/api/ui/v1/assistans-talepleri/${ASSIST_1}`:
        return json(r, { talep: assistance(), surum: 't-1' });
      case '/api/ui/v1/hukuk-dosyalari':
        return json(r, page1([legalFile()]));
      case `/api/ui/v1/hukuk-dosyalari/${LEGAL_1}`:
        return json(r, { dosya: legalFile(), surum: 'h-1' });
      case '/api/ui/v1/crm/analiz':
        return json(r, ANALYSIS);
      case '/api/ui/v1/crm/analiz/secenekler':
        return json(r, { kaynaklar: ['Web', 'Telefon'], ofisler: ['Merkez'] });
      case '/api/ui/v1/crm/secim/kira':
        return json(r, [
          {
            id: RENTAL_1,
            sozlesmeNo: '2026260801001',
            plaka: '34ABC123',
            musteriAd: 'Ayşe Yılmaz',
            musteriId: CARI_1,
            cikisOfisi: 'Merkez',
            basTar: '2026-08-26T09:00:00Z',
          },
        ]);
      default:
        if (path.startsWith('/api/ui/v1/cariler/secim/')) return json(r, []);
        return r.fulfill({ status: 404, json: { kod: 'bulunamadi' } });
    }
  }

  return written;
}
