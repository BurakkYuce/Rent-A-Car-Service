import { Observable, delay, of, throwError } from 'rxjs';

import { ApiHatasi } from '@core/api/api-hatasi';
import type { QueryParameters } from '@core/api/api-istemcisi';
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
  readonly durum: VehicleStatus;
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

export const VEHICLE_STATUSES = ['Müsait', 'Kirada', 'Serviste', 'Rezerve'] as const;
export type VehicleStatus = (typeof VEHICLE_STATUSES)[number];

export const SCENARIOS = ['normal', 'bos', 'hata', 'yavas'] as const;
export type Scenario = (typeof SCENARIOS)[number];

const TOTAL = 5000;

/** 49 sütun. Plaka sabit (solda), para sütunları sağa yaslı + tr biçimli. */
export const VEHICLE_COLUMNS: readonly TabloSutunu<AracSatiri>[] = [
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
  code: string,
  title: string,
  value: (a: AracSatiri) => unknown,
  extra: Omit<TabloSutunu<AracSatiri>, 'kod' | 'baslik' | 'deger'> = {},
): TabloSutunu<AracSatiri> {
  return { kod: code, baslik: title, deger: value, ...extra };
}

// ------------------------------------------------------------------ üretim

/** Mulberry32: küçük, hızlı, tohumlu. */
function generator(seed: number): () => number {
  let t = seed >>> 0;
  return () => {
    t = (t + 0x6d2b79f5) >>> 0;
    let r = Math.imul(t ^ (t >>> 15), 1 | t);
    r = (r + Math.imul(r ^ (r >>> 7), 61 | r)) ^ r;
    return ((r ^ (r >>> 14)) >>> 0) / 4294967296;
  };
}

const BRANDS: readonly (readonly [string, readonly string[]])[] = [
  ['Renault', ['Clio', 'Megane', 'Taliant', 'Captur']],
  ['Fiat', ['Egea', 'Doblo', '500']],
  ['Toyota', ['Corolla', 'C-HR', 'Yaris']],
  ['Volkswagen', ['Polo', 'Passat', 'T-Roc', 'Caddy']],
  ['Hyundai', ['i20', 'Tucson', 'Bayon']],
  ['Peugeot', ['208', '2008', '3008', 'Rifter']],
  ['Škoda', ['Octavia', 'Superb', 'Kamiq']],
  ['Dacia', ['Duster', 'Sandero']],
];
const COLORS = ['Beyaz', 'Siyah', 'Gri', 'Kırmızı', 'Lacivert', 'Gümüş', 'İnci beyazı'];
const FUELS = ['Benzin', 'Dizel', 'Hibrit', 'Elektrik', 'LPG'];
const TRANSMISSIONS = ['Manuel', 'Otomatik', 'Yarı otomatik'];
const BODY_TYPES = ['Sedan', 'Hatchback', 'SUV', 'Station', 'Panelvan'];
const SEGMENTS = ['A', 'B', 'C', 'D', 'E', 'SUV'];
const BRANCHES = ['İstanbul Anadolu', 'İstanbul Avrupa', 'Ankara', 'İzmir', 'Antalya', 'Muğla'];
const ILLER = ['34', '06', '35', '07', '48', '16'];
const TIRES = ['195/65 R15', '205/55 R16', '215/60 R17', '225/45 R18'];
const OWNERS = ['Firma', 'Ortak – Çağrı Öztürk', 'Leasing – İş Finans', 'Ortak – Şule Işık'];
const SUPPLIERS = ['Oyak Renault', 'Toyota Plaza Işıklar', 'Doğuş Oto', 'Borusan Oto'];
const LETTERS = 'ABCDEFGHJKLMNPRSTUVYZ';

function select<T>(r: () => number, list: readonly T[]): T {
  return list[Math.floor(r() * list.length)];
}

function round(amount: number): number {
  return Math.round(amount * 100) / 100;
}

function day(r: () => number, start: number, range: number): string {
  const date = new Date(Date.UTC(2020, 0, 1) + Math.floor(start + r() * range) * 86_400_000);
  return date.toISOString().slice(0, 10);
}

function an(r: () => number, start: number, range: number): string {
  return new Date(
    Date.UTC(2026, 0, 1) + Math.floor((start + r() * range) * 3_600_000),
  ).toISOString();
}

let cache: readonly AracSatiri[] | null = null;

export function araclar(): readonly AracSatiri[] {
  if (cache !== null) return cache;
  const r = generator(20260826);
  const list: AracSatiri[] = [];
  for (let i = 0; i < TOTAL; i++) {
    const [brand, modeller] = select(r, BRANDS);
    const daily = round(650 + r() * 3350);
    const km = Math.floor(r() * 180_000);
    const revenue = round(20_000 + r() * 900_000);
    const expense = round(5_000 + r() * 300_000);
    const branch = Math.floor(r() * BRANCHES.length);
    const letter = Array.from({ length: 1 + Math.floor(r() * 3) }, () =>
      select(r, [...LETTERS]),
    ).join('');
    list.push({
      id: `ARC-${String(i + 1).padStart(5, '0')}`,
      plaka: `${ILLER[branch]} ${letter} ${100 + Math.floor(r() * 9899)}`,
      marka: brand,
      model: select(r, modeller),
      modelYili: 2019 + Math.floor(r() * 8),
      renk: select(r, COLORS),
      yakit: select(r, FUELS),
      vites: select(r, TRANSMISSIONS),
      kasaTipi: select(r, BODY_TYPES),
      segment: select(r, SEGMENTS),
      sube: BRANCHES[branch],
      ofis: `${BRANCHES[branch]} ${1 + Math.floor(r() * 3)}. ofis`,
      durum: select(r, VEHICLE_STATUSES),
      km,
      sonBakimKm: Math.max(0, km - Math.floor(r() * 15_000)),
      sonrakiBakimKm: km + Math.floor(r() * 15_000),
      gunlukFiyat: daily,
      haftalikFiyat: round(daily * 6.3),
      aylikFiyat: round(daily * 24.5),
      depozito: round(Math.round((daily * 3) / 500) * 500),
      alisBedeli: round(r() < 0.2 ? 18_000 + r() * 40_000 : 650_000 + r() * 2_400_000),
      alisParaBirimi: r() < 0.2 ? 'EUR' : 'TRY',
      alisTarihi: day(r, 0, 2400),
      kaskoBitis: day(r, 2200, 800),
      trafikBitis: day(r, 2200, 800),
      muayeneBitis: day(r, 2200, 900),
      mtv: round(1_800 + r() * 14_000),
      hgsBakiye: round(r() * 1_500 - 150),
      sasiNo: `VF1${Array.from({ length: 14 }, () => select(r, [...'0123456789ABCDEFGHJKLMNPRSTUVWXYZ'])).join('')}`,
      motorNo: `M${Math.floor(r() * 1e9)}`,
      ruhsatSeriNo: `${select(r, ['EA', 'EB', 'FA'])} ${100000 + Math.floor(r() * 899_999)}`,
      koltuk: select(r, [2, 5, 5, 5, 7, 9]),
      kapi: select(r, [2, 3, 4, 5]),
      motorHacmi: select(r, [999, 1197, 1332, 1461, 1498, 1598, 1968]),
      motorGucu: select(r, [75, 90, 100, 115, 130, 150, 190]),
      bagaj: 250 + Math.floor(r() * 400),
      lastikEbati: select(r, TIRES),
      lastikTuru: select(r, ['Yaz', 'Kış', 'Dört mevsim']),
      aracSahibi: select(r, OWNERS),
      tedarikci: select(r, SUPPLIERS),
      kiraSayisi: Math.floor(r() * 180),
      doluluk: Math.round(r() * 1000) / 10,
      toplamGelir: revenue,
      toplamGider: expense,
      netKar: round(revenue - expense),
      sonKira: an(r, -4000, 4000),
      sonrakiRezervasyon: an(r, 0, 2000),
      gps: r() < 0.8 ? `Arvento ${1000 + Math.floor(r() * 9000)}` : '',
      anahtarNo: `K-${1 + Math.floor(r() * 400)}`,
      aciklama:
        r() < 0.3 ? 'Ön tampon çizik; iç temizlik gerekli. İlk kirada kontrol edilecek.' : '',
    });
  }
  cache = list;
  return list;
}

// ------------------------------------------------------------------ sahte uç

const comparer = new Intl.Collator('tr', { numeric: true, sensitivity: 'base' });

/**
 * `GET /api/ui/v1/...` taklidi: `sayfa`, `boyut`, `sirala` (`alan` / `-alan`) ve `senaryo`.
 * Sunucu gibi bilinmeyen sıralama alanını reddeder (400 `dogrulama`).
 */
export function fakeVehicleEndpoint(parameters: QueryParameters): Observable<Sayfa<AracSatiri>> {
  const scenario = (parameters['senaryo'] as Scenario | undefined) ?? 'normal';
  const page = Number(parameters['sayfa'] ?? 1);
  const size = Number(parameters['boyut'] ?? 100);
  if (scenario === 'hata') {
    return throwError(
      () =>
        new ApiHatasi({
          status: 503,
          kod: 'sunucu',
          detay: 'Vitrin: sunucu şu an yanıt vermiyor (hata senaryosu).',
        }),
    ).pipe(delay(200));
  }
  let list = scenario === 'bos' ? [] : [...araclar()];
  const sort = typeof parameters['sirala'] === 'string' ? parameters['sirala'] : null;
  if (sort !== null) {
    const descending = sort.startsWith('-');
    const alan = descending ? sort.slice(1) : sort;
    const column = VEHICLE_COLUMNS.find((x) => x.kod === alan && x.sirala);
    if (column === undefined) {
      return throwError(
        () =>
          new ApiHatasi({ status: 400, kod: 'dogrulama', detay: `Bilinmeyen sıralama: ${alan}` }),
      );
    }
    const yon = descending ? -1 : 1;
    list = list
      .map((row, i) => ({ satir: row, i, d: column.deger(row) }))
      .sort((a, b) => yon * compare(a.d, b.d) || a.i - b.i)
      .map((x) => x.satir);
  }
  const records = list.slice((page - 1) * size, page * size);
  return of<Sayfa<AracSatiri>>({
    kayitlar: records,
    toplam: list.length,
    sayfaNo: page,
    boyut: size,
  }).pipe(delay(scenario === 'yavas' ? 2000 : 150));
}

function compare(a: unknown, b: unknown): number {
  if (typeof a === 'number' && typeof b === 'number') return a - b;
  return comparer.compare(String(a ?? ''), String(b ?? ''));
}
