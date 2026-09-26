import { QueryParameters } from '@core/api/api-istemcisi';
import { MAX_SIZE, ListeIstegi, DEFAULT_SIZE } from '@core/api/sayfa';

/**
 * Tipli liste sorgusu (saf fonksiyonlar). Revlo'nun POST gövdeli list-query'si yerine backend
 * `ListeIstegi` sözleşmesi: GET sorgu parametreleri `sayfa`, `boyut`, `sirala` + uç başına filtreler.
 * URL ile API AYNI parametre adlarını kullanır; URL'deki her değer katalogdan geçerek okunur.
 *
 * **Sağlam ayrıştırma:** bozuk/elle yazılmış URL asla 400 üretmez — geçersiz değer varsayılana düşer
 * (sayfa 1, boyut varsayılan, sıralama varsayılan, filtre yok). Özellikle `sirala` beyaz listeden
 * geçer: sunucu bilinmeyen alanı sessizce yok saymaz, 400 döner.
 */

/** Filtre türleri. Değerler URL'de ve API'de invariant biçimde (nokta ondalık, `yyyy-MM-dd`). */
export type FilterDefinition =
  /** Serbest metin; kırpılır, NFC'ye normalize edilir, `enFazla` karakterde (varsayılan 200) kesilir. */
  | { readonly tur: 'metin'; readonly enFazla?: number }
  | { readonly tur: 'tamsayi'; readonly enAz?: number; readonly enFazla?: number }
  | { readonly tur: 'ondalik' }
  /** Takvim günü `yyyy-MM-dd` (var olmayan gün, ör. 2026-02-30, reddedilir). */
  | { readonly tur: 'tarih' }
  | { readonly tur: 'secim'; readonly degerler: readonly string[] }
  | { readonly tur: 'bayrak' }
  /** Guid. */
  | { readonly tur: 'kimlik' };

export type FilterCatalog = Readonly<Record<string, FilterDefinition>>;

type FilterValue<D extends FilterDefinition> = D extends {
  readonly tur: 'secim';
  readonly degerler: readonly (infer S extends string)[];
}
  ? S
  : D extends { readonly tur: 'tamsayi' | 'ondalik' }
    ? number
    : D extends { readonly tur: 'bayrak' }
      ? boolean
      : string;

/** Etkin filtreler; olmayan anahtar = filtre yok. */
export type Filters<K extends FilterCatalog> = {
  readonly [A in keyof K]?: FilterValue<K[A]>;
};

export interface ListeTanimi<K extends FilterCatalog> {
  readonly filtreler: K;
  /** Sunucunun `SiralamaHaritasi` beyaz listesindeki alanlar (azalan için başına `-`). */
  readonly siralanabilir: readonly string[];
  readonly varsayilanSirala: string | null;
  readonly varsayilanBoyut: number;
}

export interface ListeSorgusu<K extends FilterCatalog> extends ListeIstegi {
  readonly filtreler: Filters<K>;
}

/** Sayfa/boyut/sıralama/filtre değişikliği. Filtreyi kaldırmak için değeri `undefined` verin. */
export interface SorguDegisikligi<K extends FilterCatalog> {
  readonly sayfa?: number;
  readonly boyut?: number;
  readonly sirala?: string | null;
  readonly filtreler?: Filters<K>;
}

/** `ParamMap` ile uyumlu en küçük okuma arayüzü (router'a bağımlılık yok). */
export interface ParameterReader {
  get(name: string): string | null;
}

export type ParameterSource =
  ParameterReader | Readonly<Record<string, string | readonly string[] | null | undefined>>;

const RESERVED_NAMES: readonly string[] = ['sayfa', 'boyut', 'sirala'];
const INT32_MAX = 2_147_483_647;
const TEXT_DEFAULT_MAX = 200;
const INTEGER = /^[+-]?\d+$/;
const DECIMAL = /^-?\d+(\.\d+)?(e[+-]?\d+)?$/i;
const DATE = /^(\d{4})-(\d{2})-(\d{2})$/;
const IDENTITY = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Liste tanımı kurar ve TANIM ANINDA doğrular (yanlış katalog modül yüklenirken patlar, kullanıcıda
 * değil). `const` tip parametresi `secim` değerlerini literal birleşim olarak korur.
 */
export function listDefinition<const K extends FilterCatalog>(definition: {
  readonly filtreler: K;
  readonly siralanabilir?: readonly string[];
  readonly varsayilanSirala?: string | null;
  readonly varsayilanBoyut?: number;
}): ListeTanimi<K> {
  for (const name of Object.keys(definition.filtreler)) {
    if (RESERVED_NAMES.includes(name) || name.trim() === '') {
      throw new Error(`Filtre adı "${name}" ayrılmış (sayfa/boyut/sirala) ya da boş.`);
    }
  }
  const result: ListeTanimi<K> = {
    filtreler: definition.filtreler,
    siralanabilir: definition.siralanabilir ?? [],
    varsayilanSirala: definition.varsayilanSirala ?? null,
    varsayilanBoyut: definition.varsayilanBoyut ?? DEFAULT_SIZE,
  };
  if (result.varsayilanSirala !== null && parseSort(result, result.varsayilanSirala) === null) {
    throw new Error(`Varsayılan sıralama "${result.varsayilanSirala}" sıralanabilir listede yok.`);
  }
  if (clampSize(result.varsayilanBoyut) !== result.varsayilanBoyut) {
    throw new Error(`Varsayılan boyut 1..${MAX_SIZE} aralığında olmalı.`);
  }
  return result;
}

/** URL (ya da herhangi bir parametre kaynağı) → sorgu. Hiçbir girdi için fırlatmaz. */
export function parseQuery<K extends FilterCatalog>(
  definition: ListeTanimi<K>,
  source: ParameterSource,
): ListeSorgusu<K> {
  const filters: Record<string, unknown> = {};
  for (const [name, filter] of Object.entries(definition.filtreler)) {
    const value = parseFilter(filter, initialValue(source, name));
    if (value !== undefined) filters[name] = value;
  }
  return {
    sayfa: parsePage(initialValue(source, 'sayfa')),
    boyut: resolveSize(initialValue(source, 'boyut'), definition.varsayilanBoyut),
    sirala: parseSort(definition, initialValue(source, 'sirala')) ?? definition.varsayilanSirala,
    filtreler: filters as Filters<K>,
  };
}

/**
 * Sorgu → URL sorgu parametreleri. YÖNETİLEN her anahtar döner; varsayılan/boş olanlar `null`
 * (router `queryParamsHandling: 'merge'` ile onları URL'den siler → temiz URL). Yazılan her değer
 * `sorguyuCoz` ile aynı değere geri okunur (gidiş-dönüş testi kilitli).
 */
export function urlParameters<K extends FilterCatalog>(
  definition: ListeTanimi<K>,
  query: ListeSorgusu<K>,
): Record<string, string | null> {
  const page = parsePage(String(query.sayfa));
  const size = resolveSize(String(query.boyut), definition.varsayilanBoyut);
  const sort = parseSort(definition, query.sirala) ?? definition.varsayilanSirala;
  const result: Record<string, string | null> = {
    sayfa: page === 1 ? null : String(page),
    boyut: size === definition.varsayilanBoyut ? null : String(size),
    sirala: sort === definition.varsayilanSirala ? null : sort,
  };
  const filters = query.filtreler as Readonly<Record<string, unknown>>;
  for (const [name, filter] of Object.entries(definition.filtreler)) {
    result[name] = writeFilter(filter, filters[name]);
  }
  return result;
}

/** Sorgu → API sorgu parametreleri (`ListeIstegi` + dolu filtreler; sayfa ve boyut her zaman gider). */
export function apiParams<K extends FilterCatalog>(
  definition: ListeTanimi<K>,
  query: ListeSorgusu<K>,
): QueryParameters {
  const url = urlParameters(definition, query);
  const result: Record<string, string | number> = {
    sayfa: parsePage(String(query.sayfa)),
    boyut: resolveSize(String(query.boyut), definition.varsayilanBoyut),
  };
  const sort = parseSort(definition, query.sirala) ?? definition.varsayilanSirala;
  if (sort !== null) result['sirala'] = sort;
  for (const name of Object.keys(definition.filtreler)) {
    const value = url[name];
    if (value !== null && value !== undefined) result[name] = value;
  }
  return result;
}

/**
 * Değişikliği uygular. Filtre, sıralama ya da boyut değişip sayfa açıkça verilmediyse sayfa 1'e
 * döner (5. sayfadayken filtre daralınca boş sayfa görülmesin).
 */
export function changeQuery<K extends FilterCatalog>(
  definition: ListeTanimi<K>,
  existing: ListeSorgusu<K>,
  change: SorguDegisikligi<K>,
): ListeSorgusu<K> {
  const candidate: ListeSorgusu<K> = {
    sayfa: change.sayfa ?? existing.sayfa,
    boyut: change.boyut ?? existing.boyut,
    sirala: change.sirala === undefined ? existing.sirala : change.sirala,
    filtreler:
      change.filtreler === undefined
        ? existing.filtreler
        : { ...existing.filtreler, ...change.filtreler },
  };
  // Normalize: URL'e yazılıp geri okunmuş hâli (geçersiz değerler burada düşer).
  const newItem = parseQuery(definition, urlParameters(definition, candidate));
  const pageResets =
    change.sayfa === undefined &&
    (newItem.boyut !== existing.boyut ||
      newItem.sirala !== existing.sirala ||
      filterKey(definition, newItem) !== filterKey(definition, existing));
  return pageResets ? { ...newItem, sayfa: 1 } : newItem;
}

/** Tüm sorgunun kararlı anahtarı (eşitlik: aynı anahtar = aynı istek). */
export function queryKey<K extends FilterCatalog>(
  definition: ListeTanimi<K>,
  query: ListeSorgusu<K>,
): string {
  return JSON.stringify(urlParameters(definition, query));
}

/** Etkin filtre sayısı (katlanır filtre başlığında gösterilir). */
export function activeFilterCount<K extends FilterCatalog>(
  definition: ListeTanimi<K>,
  query: ListeSorgusu<K>,
): number {
  const url = urlParameters(definition, query);
  return Object.keys(definition.filtreler).filter((name) => url[name] !== null).length;
}

function filterKey<K extends FilterCatalog>(
  definition: ListeTanimi<K>,
  query: ListeSorgusu<K>,
): string {
  const url = urlParameters(definition, query);
  return JSON.stringify(Object.keys(definition.filtreler).map((name) => url[name]));
}

function initialValue(source: ParameterSource, name: string): string | null {
  if (isReader(source)) return source.get(name);
  const value = Object.prototype.hasOwnProperty.call(source, name) ? source[name] : undefined;
  if (typeof value === 'string') return value;
  if (Array.isArray(value)) {
    const first: unknown = value[0];
    return typeof first === 'string' ? first : null;
  }
  return null;
}

function isReader(source: ParameterSource): source is ParameterReader {
  return typeof (source as Partial<ParameterReader>).get === 'function';
}

function parseInteger(raw: string | null): number | null {
  if (raw === null) return null;
  const text = raw.trim();
  if (!INTEGER.test(text)) return null;
  const count = Number(text);
  return Number.isNaN(count) ? null : count;
}

/** Sunucu kuralı: < 1 → 1. Ek olarak int32 dışı (sunucuda bağlanamaz → 400) → 1. */
function parsePage(raw: string | null): number {
  const count = parseInteger(raw);
  if (count === null || count < 1 || count > INT32_MAX) return 1;
  return count;
}

/** Sunucu kuralı: tamsayı → 1..200'e kırpılır; tamsayı değilse varsayılan. */
function resolveSize(raw: string | null, defaultValue: number): number {
  const count = parseInteger(raw);
  return count === null ? defaultValue : clampSize(count);
}

function clampSize(count: number): number {
  return Math.min(MAX_SIZE, Math.max(1, Math.trunc(count)));
}

/** `"alan"` / `"-alan"`; alan beyaz listede değilse `null`. */
function parseSort<K extends FilterCatalog>(
  definition: ListeTanimi<K>,
  raw: string | null,
): string | null {
  if (raw === null) return null;
  const text = raw.trim();
  const alan = text.startsWith('-') ? text.slice(1) : text;
  return alan !== '' && definition.siralanabilir.includes(alan) ? text : null;
}

function parseFilter(
  filter: FilterDefinition,
  raw: string | null,
): string | number | boolean | undefined {
  if (raw === null) return undefined;
  switch (filter.tur) {
    case 'metin': {
      const text = Array.from(raw.normalize('NFC').trim())
        .slice(0, filter.enFazla ?? TEXT_DEFAULT_MAX)
        .join('')
        .trim();
      return text === '' ? undefined : text;
    }
    case 'tamsayi': {
      const count = parseInteger(raw);
      if (count === null || !Number.isSafeInteger(count)) return undefined;
      if (filter.enAz !== undefined && count < filter.enAz) return undefined;
      if (filter.enFazla !== undefined && count > filter.enFazla) return undefined;
      return count;
    }
    case 'ondalik': {
      const text = raw.trim();
      const count = DECIMAL.test(text) ? Number(text) : Number.NaN;
      return Number.isFinite(count) ? count : undefined;
    }
    case 'tarih':
      return isValidDate(raw.trim()) ? raw.trim() : undefined;
    case 'secim':
      return filter.degerler.includes(raw) ? raw : undefined;
    case 'bayrak':
      if (raw === 'true' || raw === '1') return true;
      if (raw === 'false' || raw === '0') return false;
      return undefined;
    case 'kimlik':
      return IDENTITY.test(raw.trim()) ? raw.trim() : undefined;
  }
}

/** Değer → URL metni; geçersizse `null`. Yazılan metin `filtreCoz` ile aynı değere döner. */
function writeFilter(filter: FilterDefinition, value: unknown): string | null {
  if (value === undefined || value === null) return null;
  if (typeof value !== 'string' && typeof value !== 'number' && typeof value !== 'boolean') {
    return null;
  }
  const resolved = parseFilter(filter, String(value));
  return resolved === undefined ? null : String(resolved);
}

function isValidDate(text: string): boolean {
  const match = DATE.exec(text);
  if (match === null) return false;
  const [, yearText, monthText, dayText] = match;
  const year = Number(yearText);
  const month = Number(monthText);
  const day = Number(dayText);
  const date = new Date(Date.UTC(year, month - 1, day));
  return (
    date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day
  );
}
