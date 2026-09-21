import { Observable, delay, of, throwError } from 'rxjs';

import { ApiHatasi } from '@core/api/api-hatasi';
import type { SorguParametreleri } from '@core/api/api-istemcisi';
import type { Sayfa } from '@core/api/sayfa';
import type { TabloSutunu } from '@shared/tablo/tablo-modeli';

/**
 * Vitrin verisi: 49 sütunlu, 5.000 satırlık GERÇEKÇİ bir araç listesi (referans sistem "Detaylı Araç
 * Listesi" genişliğinde) ve sunucu gibi davranan bellek içi uç: `sayfa`/`boyut`/`sirala` + senaryo.
 * Tohumlu üreteç → her açılışta aynı veri (e2e ve görsel regresyon kararlı).
 */

export interface AracSatiri {
  readonly id: string;
  readonly plaka: string;
  readonly marka: string;
  readonly model: string;
  readonly modelYili: number;
  readonly renk: string;
  readonly yakit: string;
  readonly vites: string;
  readonly kasaTipi: string;
  readonly segment: string;
  readonly sube: string;
  readonly ofis: string;
  readonly durum: AracDurumu;
  readonly km: number;
  readonly sonBakimKm: number;
  readonly sonrakiBakimKm: number;
  readonly gunlukFiyat: number;
  readonly haftalikFiyat: number;
  readonly aylikFiyat: number;
  readonly depozito: number;
  readonly alisBedeli: number;
  readonly alisParaBirimi: string;
  readonly alisTarihi: string;
  readonly kaskoBitis: string;
  readonly trafikBitis: string;
  readonly muayeneBitis: string;
  readonly mtv: number;
  readonly hgsBakiye: number;
  readonly sasiNo: string;
  readonly motorNo: string;
  readonly ruhsatSeriNo: string;
  readonly koltuk: number;
  readonly kapi: number;
  readonly motorHacmi: number;
  readonly motorGucu: number;
  readonly bagaj: number;
  readonly lastikEbati: string;
  readonly lastikTuru: string;
  readonly aracSahibi: string;
  readonly tedarikci: string;
  readonly kiraSayisi: number;
  readonly doluluk: number;
  readonly toplamGelir: number;
  readonly toplamGider: number;
  readonly netKar: number;
  readonly sonKira: string;
  readonly sonrakiRezervasyon: string;
  readonly gps: string;
  readonly anahtarNo: string;
  readonly aciklama: string;
}

export const ARAC_DURUMLARI = ['Müsait', 'Kirada', 'Serviste', 'Rezerve'] as const;
export type AracDurumu = (typeof ARAC_DURUMLARI)[number];

export const SENARYOLAR = ['normal', 'bos', 'hata', 'yavas'] as const;
export type Senaryo = (typeof SENARYOLAR)[number];

const TOPLAM = 5000;

/** 49 sütun. Plaka sabit (solda), para sütunları sağa yaslı + tr biçimli. */
export const ARAC_SUTUNLARI: readonly TabloSutunu<AracSatiri>[] = [
  s('plaka', 'Plaka', (a) => a.plaka, { sabit: true, genislik: 110, sirala: true }),
  s('marka', 'Marka', (a) => a.marka, { sirala: true }),
  s('model', 'Model', (a) => a.model, { sirala: true }),
  s('modelYili', 'Model yılı', (a) => a.modelYili, {
    tur: 'sayi',
    haneler: '1.0-0',
    genislik: 90,
    sirala: true,
  }),
  s('renk', 'Renk', (a) => a.renk, { genislik: 100 }),
  s('yakit', 'Yakıt', (a) => a.yakit, { genislik: 100, sirala: true }),
  s('vites', 'Vites', (a) => a.vites, { genislik: 100 }),
  s('kasaTipi', 'Kasa tipi', (a) => a.kasaTipi, { genislik: 110 }),
  s('segment', 'Segment', (a) => a.segment, { genislik: 90, sirala: true }),
  s('sube', 'Şube', (a) => a.sube, { sirala: true }),
  s('ofis', 'Ofis', (a) => a.ofis),
  s('durum', 'Durum', (a) => a.durum, { genislik: 110, sirala: true }),
  s('km', 'Km', (a) => a.km, { tur: 'sayi', haneler: '1.0-0', genislik: 100, sirala: true }),
  s('sonBakimKm', 'Son bakım km', (a) => a.sonBakimKm, {
    tur: 'sayi',
    haneler: '1.0-0',
    genislik: 120,
  }),
  s('sonrakiBakimKm', 'Sonraki bakım km', (a) => a.sonrakiBakimKm, {
    tur: 'sayi',
    haneler: '1.0-0',
    genislik: 130,
  }),
  s('gunlukFiyat', 'Günlük fiyat', (a) => a.gunlukFiyat, {
    tur: 'para',
    genislik: 120,
    sirala: true,
  }),
  s('haftalikFiyat', 'Haftalık fiyat', (a) => a.haftalikFiyat, {
    tur: 'para',
    genislik: 130,
    sirala: true,
  }),
  s('aylikFiyat', 'Aylık fiyat', (a) => a.aylikFiyat, { tur: 'para', genislik: 130, sirala: true }),
  s('depozito', 'Depozito', (a) => a.depozito, { tur: 'para', genislik: 120 }),
  s('alisBedeli', 'Alış bedeli', (a) => a.alisBedeli, {
    tur: 'para',
    paraBirimi: (a) => a.alisParaBirimi,
    genislik: 140,
    sirala: true,
  }),
  s('alisTarihi', 'Alış tarihi', (a) => a.alisTarihi, {
    tur: 'tarih',
    genislik: 110,
    sirala: true,
  }),
  s('kaskoBitis', 'Kasko bitiş', (a) => a.kaskoBitis, {
    tur: 'tarih',
    genislik: 110,
    sirala: true,
  }),
  s('trafikBitis', 'Trafik sig. bitiş', (a) => a.trafikBitis, { tur: 'tarih', genislik: 130 }),
  s('muayeneBitis', 'Muayene bitiş', (a) => a.muayeneBitis, { tur: 'tarih', genislik: 120 }),
  s('mtv', 'MTV', (a) => a.mtv, { tur: 'para', genislik: 110 }),
  s('hgsBakiye', 'HGS bakiye', (a) => a.hgsBakiye, { tur: 'para', genislik: 110 }),
  s('sasiNo', 'Şasi no', (a) => a.sasiNo, { genislik: 180 }),
  s('motorNo', 'Motor no', (a) => a.motorNo, { genislik: 130 }),
  s('ruhsatSeriNo', 'Ruhsat seri no', (a) => a.ruhsatSeriNo, { genislik: 130 }),
  s('koltuk', 'Koltuk', (a) => a.koltuk, { tur: 'sayi', genislik: 80 }),
  s('kapi', 'Kapı', (a) => a.kapi, { tur: 'sayi', genislik: 70 }),
  s('motorHacmi', 'Motor hacmi', (a) => a.motorHacmi, {
    tur: 'sayi',
    haneler: '1.0-0',
    genislik: 110,
  }),
  s('motorGucu', 'Motor gücü (hp)', (a) => a.motorGucu, { tur: 'sayi', genislik: 120 }),
  s('bagaj', 'Bagaj (lt)', (a) => a.bagaj, { tur: 'sayi', genislik: 90 }),
  s('lastikEbati', 'Lastik ebatı', (a) => a.lastikEbati, { genislik: 120 }),
  s('lastikTuru', 'Lastik türü', (a) => a.lastikTuru, { genislik: 100 }),
  s('aracSahibi', 'Araç sahibi', (a) => a.aracSahibi, { genislik: 160 }),
  s('tedarikci', 'Tedarikçi', (a) => a.tedarikci, { genislik: 160 }),
  s('kiraSayisi', 'Kira sayısı', (a) => a.kiraSayisi, { tur: 'sayi', genislik: 100, sirala: true }),
  s('doluluk', 'Doluluk %', (a) => a.doluluk, {
    tur: 'sayi',
    haneler: '1.1-1',
    genislik: 100,
    sirala: true,
  }),
  s('toplamGelir', 'Toplam gelir', (a) => a.toplamGelir, {
    tur: 'para',
    genislik: 140,
    sirala: true,
  }),
  s('toplamGider', 'Toplam gider', (a) => a.toplamGider, { tur: 'para', genislik: 140 }),
  s('netKar', 'Net kâr', (a) => a.netKar, { tur: 'para', genislik: 140, sirala: true }),
  s('sonKira', 'Son kira', (a) => a.sonKira, { tur: 'tarihSaat', genislik: 140 }),
  s('sonrakiRezervasyon', 'Sonraki rezervasyon', (a) => a.sonrakiRezervasyon, {
    tur: 'tarihSaat',
    genislik: 150,
  }),
  s('gps', 'GPS cihazı', (a) => a.gps, { genislik: 110 }),
  s('anahtarNo', 'Anahtar no', (a) => a.anahtarNo, { genislik: 100 }),
  s('aciklama', 'Açıklama', (a) => a.aciklama, { genislik: 220 }),
  s('kayitNo', 'Kayıt no', (a) => a.id, { genislik: 110, gizli: true }),
];

function s(
  kod: string,
  baslik: string,
  deger: (a: AracSatiri) => unknown,
  ek: Omit<TabloSutunu<AracSatiri>, 'kod' | 'baslik' | 'deger'> = {},
): TabloSutunu<AracSatiri> {
  return { kod, baslik, deger, ...ek };
}

// ------------------------------------------------------------------ üretim

/** Mulberry32: küçük, hızlı, tohumlu. */
function uretec(tohum: number): () => number {
  let t = tohum >>> 0;
  return () => {
    t = (t + 0x6d2b79f5) >>> 0;
    let r = Math.imul(t ^ (t >>> 15), 1 | t);
    r = (r + Math.imul(r ^ (r >>> 7), 61 | r)) ^ r;
    return ((r ^ (r >>> 14)) >>> 0) / 4294967296;
  };
}

const MARKALAR: readonly (readonly [string, readonly string[]])[] = [
  ['Renault', ['Clio', 'Megane', 'Taliant', 'Captur']],
  ['Fiat', ['Egea', 'Doblo', '500']],
  ['Toyota', ['Corolla', 'C-HR', 'Yaris']],
  ['Volkswagen', ['Polo', 'Passat', 'T-Roc', 'Caddy']],
  ['Hyundai', ['i20', 'Tucson', 'Bayon']],
  ['Peugeot', ['208', '2008', '3008', 'Rifter']],
  ['Škoda', ['Octavia', 'Superb', 'Kamiq']],
  ['Dacia', ['Duster', 'Sandero']],
];
const RENKLER = ['Beyaz', 'Siyah', 'Gri', 'Kırmızı', 'Lacivert', 'Gümüş', 'İnci beyazı'];
const YAKITLAR = ['Benzin', 'Dizel', 'Hibrit', 'Elektrik', 'LPG'];
const VITESLER = ['Manuel', 'Otomatik', 'Yarı otomatik'];
const KASALAR = ['Sedan', 'Hatchback', 'SUV', 'Station', 'Panelvan'];
const SEGMENTLER = ['A', 'B', 'C', 'D', 'E', 'SUV'];
const SUBELER = ['İstanbul Anadolu', 'İstanbul Avrupa', 'Ankara', 'İzmir', 'Antalya', 'Muğla'];
const ILLER = ['34', '06', '35', '07', '48', '16'];
const LASTIKLER = ['195/65 R15', '205/55 R16', '215/60 R17', '225/45 R18'];
const SAHIPLER = ['Firma', 'Ortak – Çağrı Öztürk', 'Leasing – İş Finans', 'Ortak – Şule Işık'];
const TEDARIKCILER = ['Oyak Renault', 'Toyota Plaza Işıklar', 'Doğuş Oto', 'Borusan Oto'];
const HARFLER = 'ABCDEFGHJKLMNPRSTUVYZ';

function sec<T>(r: () => number, liste: readonly T[]): T {
  return liste[Math.floor(r() * liste.length)];
}

function yuvarla(tutar: number): number {
  return Math.round(tutar * 100) / 100;
}

function gun(r: () => number, bas: number, aralik: number): string {
  const tarih = new Date(Date.UTC(2020, 0, 1) + Math.floor(bas + r() * aralik) * 86_400_000);
  return tarih.toISOString().slice(0, 10);
}

function an(r: () => number, bas: number, aralik: number): string {
  return new Date(
    Date.UTC(2026, 0, 1) + Math.floor((bas + r() * aralik) * 3_600_000),
  ).toISOString();
}

let onbellek: readonly AracSatiri[] | null = null;

export function araclar(): readonly AracSatiri[] {
  if (onbellek !== null) return onbellek;
  const r = uretec(20260826);
  const liste: AracSatiri[] = [];
  for (let i = 0; i < TOPLAM; i++) {
    const [marka, modeller] = sec(r, MARKALAR);
    const gunluk = yuvarla(650 + r() * 3350);
    const km = Math.floor(r() * 180_000);
    const gelir = yuvarla(20_000 + r() * 900_000);
    const gider = yuvarla(5_000 + r() * 300_000);
    const sube = Math.floor(r() * SUBELER.length);
    const harf = Array.from({ length: 1 + Math.floor(r() * 3) }, () => sec(r, [...HARFLER])).join(
      '',
    );
    liste.push({
      id: `ARC-${String(i + 1).padStart(5, '0')}`,
      plaka: `${ILLER[sube]} ${harf} ${100 + Math.floor(r() * 9899)}`,
      marka,
      model: sec(r, modeller),
      modelYili: 2019 + Math.floor(r() * 8),
      renk: sec(r, RENKLER),
      yakit: sec(r, YAKITLAR),
      vites: sec(r, VITESLER),
      kasaTipi: sec(r, KASALAR),
      segment: sec(r, SEGMENTLER),
      sube: SUBELER[sube],
      ofis: `${SUBELER[sube]} ${1 + Math.floor(r() * 3)}. ofis`,
      durum: sec(r, ARAC_DURUMLARI),
      km,
      sonBakimKm: Math.max(0, km - Math.floor(r() * 15_000)),
      sonrakiBakimKm: km + Math.floor(r() * 15_000),
      gunlukFiyat: gunluk,
      haftalikFiyat: yuvarla(gunluk * 6.3),
      aylikFiyat: yuvarla(gunluk * 24.5),
      depozito: yuvarla(Math.round((gunluk * 3) / 500) * 500),
      alisBedeli: yuvarla(r() < 0.2 ? 18_000 + r() * 40_000 : 650_000 + r() * 2_400_000),
      alisParaBirimi: r() < 0.2 ? 'EUR' : 'TRY',
      alisTarihi: gun(r, 0, 2400),
      kaskoBitis: gun(r, 2200, 800),
      trafikBitis: gun(r, 2200, 800),
      muayeneBitis: gun(r, 2200, 900),
      mtv: yuvarla(1_800 + r() * 14_000),
      hgsBakiye: yuvarla(r() * 1_500 - 150),
      sasiNo: `VF1${Array.from({ length: 14 }, () => sec(r, [...'0123456789ABCDEFGHJKLMNPRSTUVWXYZ'])).join('')}`,
      motorNo: `M${Math.floor(r() * 1e9)}`,
      ruhsatSeriNo: `${sec(r, ['EA', 'EB', 'FA'])} ${100000 + Math.floor(r() * 899_999)}`,
      koltuk: sec(r, [2, 5, 5, 5, 7, 9]),
      kapi: sec(r, [2, 3, 4, 5]),
      motorHacmi: sec(r, [999, 1197, 1332, 1461, 1498, 1598, 1968]),
      motorGucu: sec(r, [75, 90, 100, 115, 130, 150, 190]),
      bagaj: 250 + Math.floor(r() * 400),
      lastikEbati: sec(r, LASTIKLER),
      lastikTuru: sec(r, ['Yaz', 'Kış', 'Dört mevsim']),
      aracSahibi: sec(r, SAHIPLER),
      tedarikci: sec(r, TEDARIKCILER),
      kiraSayisi: Math.floor(r() * 180),
      doluluk: Math.round(r() * 1000) / 10,
      toplamGelir: gelir,
      toplamGider: gider,
      netKar: yuvarla(gelir - gider),
      sonKira: an(r, -4000, 4000),
      sonrakiRezervasyon: an(r, 0, 2000),
      gps: r() < 0.8 ? `Arvento ${1000 + Math.floor(r() * 9000)}` : '',
      anahtarNo: `K-${1 + Math.floor(r() * 400)}`,
      aciklama:
        r() < 0.3 ? 'Ön tampon çizik; iç temizlik gerekli. İlk kirada kontrol edilecek.' : '',
    });
  }
  onbellek = liste;
  return liste;
}

// ------------------------------------------------------------------ sahte uç

const karsilastirici = new Intl.Collator('tr', { numeric: true, sensitivity: 'base' });

/**
 * `GET /api/ui/v1/...` taklidi: `sayfa`, `boyut`, `sirala` (`alan` / `-alan`) ve `senaryo`.
 * Sunucu gibi bilinmeyen sıralama alanını reddeder (400 `dogrulama`).
 */
export function sahteAracUcu(parametreler: SorguParametreleri): Observable<Sayfa<AracSatiri>> {
  const senaryo = (parametreler['senaryo'] as Senaryo | undefined) ?? 'normal';
  const sayfa = Number(parametreler['sayfa'] ?? 1);
  const boyut = Number(parametreler['boyut'] ?? 100);
  if (senaryo === 'hata') {
    return throwError(
      () =>
        new ApiHatasi({
          status: 503,
          kod: 'sunucu',
          detay: 'Vitrin: sunucu şu an yanıt vermiyor (hata senaryosu).',
        }),
    ).pipe(delay(200));
  }
  let liste = senaryo === 'bos' ? [] : [...araclar()];
  const sirala = typeof parametreler['sirala'] === 'string' ? parametreler['sirala'] : null;
  if (sirala !== null) {
    const azalan = sirala.startsWith('-');
    const alan = azalan ? sirala.slice(1) : sirala;
    const sutun = ARAC_SUTUNLARI.find((x) => x.kod === alan && x.sirala);
    if (sutun === undefined) {
      return throwError(
        () =>
          new ApiHatasi({ status: 400, kod: 'dogrulama', detay: `Bilinmeyen sıralama: ${alan}` }),
      );
    }
    const yon = azalan ? -1 : 1;
    liste = liste
      .map((satir, i) => ({ satir, i, d: sutun.deger(satir) }))
      .sort((a, b) => yon * karsilastir(a.d, b.d) || a.i - b.i)
      .map((x) => x.satir);
  }
  const kayitlar = liste.slice((sayfa - 1) * boyut, sayfa * boyut);
  return of<Sayfa<AracSatiri>>({ kayitlar, toplam: liste.length, sayfaNo: sayfa, boyut }).pipe(
    delay(senaryo === 'yavas' ? 2000 : 150),
  );
}

function karsilastir(a: unknown, b: unknown): number {
  if (typeof a === 'number' && typeof b === 'number') return a - b;
  return karsilastirici.compare(String(a ?? ''), String(b ?? ''));
}
