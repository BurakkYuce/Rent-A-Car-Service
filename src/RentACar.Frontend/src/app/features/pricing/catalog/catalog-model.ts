import type { ApiPath } from '@core/api/api-istemcisi';
import { invariantDecimal } from '@core/form/ondalik';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import {
  type FilterCatalog,
  type FilterDefinition,
  type ListeTanimi,
  listDefinition,
} from '@core/veri/liste-sorgusu';
import { momentValue, dayValue, textValue } from '@features/planlama-ortak/form-yardimcilari';

/**
 * F9.2 fiyat/tarife tanım ekranlarının (tarifeler, tarife grupları, sigorta ürünleri, ek hizmetler, tarife matrisi,
 * kira kuralları, broker yasakları, servis tanımları) tek sözleşmesi. Sunucu uçları aynı kalıpta (F9.1
 * `CatalogCrud`): liste `q` + sayfa/sıralama, tekil GET `surum`'lu, POST, tam değiştirme PUT (`surum` zorunlu → 409
 * `cakisma`), DELETE. Bu dosya SAF: form ↔ DTO dönüşümü ve liste tanımı (Vitest ile kilitli).
 */

/** `lookup`: seçenekleri bir seçim ucundan (`id` + etiket) gelen tekli seçim; değer kimlik. */
export type CatalogFieldKind =
  | 'text'
  | 'textarea'
  | 'int'
  | 'money'
  | 'ratio'
  | 'date'
  | 'bool'
  | 'select'
  | 'password'
  | 'weekdays'
  | 'lookup'
  | 'readonly';

/** Seç-veya-yaz önerisi: seçim ucu + öneride yazılacak değer (kod mu ad mı — Blazor alanın sakladığı). */
export interface CatalogSuggestion {
  readonly endpoint: 'arac-grubu' | 'sube' | 'rezervasyon-kaynagi' | 'lokasyon';
  readonly value: 'kod' | 'etiket';
}

export interface CatalogField {
  /** JSON alan adı (sunucu). */
  readonly name: string;
  readonly label: CeviriAnahtari;
  readonly kind: CatalogFieldKind;
  readonly required?: boolean;
  readonly maxLength?: number;
  readonly hint?: CeviriAnahtari;
  /** `select`: sunucu enum ADLARI. */
  readonly options?: readonly string[];
  /** `select` seçenek etiketlerinin i18n öneki (`<önek>.<ad>`); yoksa ad olduğu gibi. */
  readonly optionLabels?: string;
  /** `select` boş (null) seçilebilir. */
  readonly nullable?: boolean;
  readonly suggestion?: CatalogSuggestion;
  /** `lookup`: seçim ucu. */
  readonly lookup?: 'tarife-grubu';
  /** Bölüm başlığı (fieldset) — aynı gruptaki alanlar birlikte çizilir. */
  readonly group?: CeviriAnahtari;
  /** `money` / `ratio` kesir hanesi (katalog fiyatı sütun ölçeği 4; oran 4). */
  readonly decimals?: number;
  readonly negative?: boolean;
  /** Yeni kayıtta varsayılan (form değeri biçiminde). */
  readonly defaultValue?: unknown;
  /** Bu satır bayrağı `true` ise düzenlemede alan salt okunur (ör. `SYS-*` ek hizmet kodu). */
  readonly lockedBy?: string;
}

/** Tablo sütunu: bir form alanı ya da yalnız DTO'da olan bir alan (ör. `sifreVar`). */
export interface CatalogColumn {
  readonly field: string;
  readonly label?: CeviriAnahtari;
  readonly kind?: 'text' | 'money' | 'number' | 'date' | 'bool' | 'select';
  readonly sortable?: boolean;
  /** Para sütununun dövizi hangi alanda (yoksa TRY). */
  readonly currencyField?: string;
  readonly width?: number;
}

export interface CatalogFilter {
  readonly name: string;
  readonly label: CeviriAnahtari;
  readonly kind: 'text' | 'select' | 'date' | 'bool';
  readonly options?: readonly string[];
  readonly optionLabels?: string;
}

export interface CatalogConfig {
  /** Rota verisi ve tablo kodu (`fiyat.<key>`). */
  readonly key: string;
  readonly root: ApiPath;
  /** Liste ucu farklıysa (kira kuralları: `/kira-kurallari/ara`). */
  readonly listPath?: ApiPath;
  readonly title: CeviriAnahtari;
  readonly description: CeviriAnahtari;
  /** Düzenleme bölümü başlığı: "Yeni …" / "… Düzenle". */
  readonly newTitle: CeviriAnahtari;
  readonly editTitle: CeviriAnahtari;
  readonly fields: readonly CatalogField[];
  readonly columns: readonly CatalogColumn[];
  readonly defaultSort: string;
  readonly filters?: readonly CatalogFilter[];
  /** Satır bayrağı: `true` ise silme yok (ör. `sistem`). */
  readonly lockFlag?: string;
  /** Satırı tanıtan alan (silme onayı, bildirim). */
  readonly titleField: string;
}

export type CatalogRow = Readonly<Record<string, unknown>> & { readonly id: string };

const toNum = (v: unknown): number | null => {
  if (v === null || v === undefined || v === '') return null;
  const n = typeof v === 'number' ? v : Number(v);
  return Number.isFinite(n) ? n : null;
};

/** Varsayılan kesir: katalog fiyatı 4 (sütun ölçeği `numeric(19,4)`), oran 4. */
export function decimalsOf(f: CatalogField): number {
  return f.decimals ?? 4;
}

/** DTO alanı → form değeri (tarih İstanbul günü, tutar invariant metin, sayı `number`). */
export function toFormValue(f: CatalogField, v: unknown): unknown {
  if (v === undefined) return f.kind === 'bool' ? false : null;
  switch (f.kind) {
    case 'money':
      return v === null || v === ''
        ? null
        : invariantDecimal(v as string | number, { kesir: decimalsOf(f) });
    case 'date':
      return dayValue(typeof v === 'string' ? v : null);
    case 'int':
    case 'ratio':
      return toNum(v);
    case 'bool':
      return v === true;
    case 'password':
      return null; // yalnız yazılır, sunucu dönmez
    default:
      return v ?? null;
  }
}

/** Form değeri → istek alanı. Dokunulmayan tarih sunucunun anıyla (gün yuvarlaması yok). */
export function toRequestValue(f: CatalogField, v: unknown, original: unknown): unknown {
  switch (f.kind) {
    case 'date':
      return momentValue(
        (v as string | null) ?? null,
        typeof original === 'string' ? original : null,
      );
    case 'bool':
      return v === true;
    case 'int':
    case 'ratio':
      return toNum(v);
    case 'money':
      return v === '' ? null : (v ?? null);
    case 'text':
    case 'textarea':
    case 'password':
    case 'weekdays':
      return textValue(typeof v === 'string' ? v : null);
    default:
      return v === '' ? null : (v ?? null);
  }
}

/** Yeni kayıt formunun başlangıcı. */
export function emptyFormValue(fields: readonly CatalogField[]): Record<string, unknown> {
  const value: Record<string, unknown> = {};
  for (const f of fields) {
    if (f.kind === 'readonly') continue;
    value[f.name] = f.defaultValue ?? (f.kind === 'bool' ? false : null);
  }
  return value;
}

/** Kayıt → form değeri. */
export function rowToForm(
  fields: readonly CatalogField[],
  row: CatalogRow,
): Record<string, unknown> {
  const value: Record<string, unknown> = {};
  for (const f of fields) {
    if (f.kind === 'readonly') continue;
    value[f.name] = toFormValue(f, row[f.name]);
  }
  return value;
}

/**
 * Form → POST/PUT gövdesi. `original` düzenlenen kayıt (null = yeni); PUT'ta `surum` (sunucu karşılaştırır).
 * Salt okunur alanlar (sunucunun damgaladığı onaylayan/zaman) GÖNDERİLMEZ.
 */
export function formToBody(
  fields: readonly CatalogField[],
  value: Readonly<Record<string, unknown>>,
  original: CatalogRow | null,
): Record<string, unknown> {
  const body: Record<string, unknown> = {};
  for (const f of fields) {
    if (f.kind === 'readonly') continue;
    body[f.name] = toRequestValue(f, value[f.name], original?.[f.name]);
  }
  if (original !== null) body['surum'] = original['surum'] ?? null;
  return body;
}

function filterDefinition(f: CatalogFilter): FilterDefinition {
  switch (f.kind) {
    case 'select':
      return { tur: 'secim', degerler: f.options ?? [] };
    case 'date':
      return { tur: 'tarih' };
    case 'bool':
      return { tur: 'secim', degerler: ['true', 'false'] };
    default:
      return { tur: 'metin', enFazla: 100 };
  }
}

/** Liste sorgusu: `q` + ekran süzgeçleri; sıralama beyaz listesi sunucunun `Sort` haritasıyla aynı sütunlar. */
export function catalogListDefinition(c: CatalogConfig): ListeTanimi<FilterCatalog> {
  const filters: Record<string, FilterDefinition> = { q: { tur: 'metin', enFazla: 100 } };
  for (const f of c.filters ?? []) filters[f.name] = filterDefinition(f);
  return listDefinition({
    filtreler: filters,
    siralanabilir: c.columns.filter((x) => x.sortable).map((x) => x.field),
    varsayilanSirala: c.defaultSort,
  });
}
