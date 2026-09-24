import type { SecimUcu } from '@core/api/ui-tipleri';
// Rapor tanımları uç şemasına YOLDAN bağlanır (sütun alanı, özet alanı, süzgeç parametresi): `ui-tipleri`
// yol tablosunu dışa açmadığı için yol tipleri üretilen dosyadan alınır (yalnız tip; çalışma zamanı yok).
import type { paths } from '@core/api/uretilen/ui-v1';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import type { Izin } from '@core/oturum/oturum-tipleri';

/**
 * F10.2 ORTAK RAPOR ŞABLONU — saf model. 26 rapor ekranı tek bileşenin (`ReportPage`) YAPILANDIRMASIDIR: her rapor
 * bir ya da birkaç görünüm (uç) tanımlar; görünüm süzgeçlerini, özet kartlarını, özet içi tablolarını ve sayfalı
 * satır tablosunu bildirir. Tanımlar uç YOLUNA tiplidir (`defineView('/api/ui/v1/raporlar/…')`): sütun alanı satır
 * şemasında, süzgeç parametresi uç sorgusunda yoksa DERLEME HATASI (üretilen tiplerden).
 */

export type ReportPath = Extract<keyof paths, `/api/ui/v1/raporlar/${string}`>;

type Response<P extends ReportPath> =
  paths[P]['get']['responses'][200]['content']['application/json'];

/** Zarflı uçta `ozet`, zarfsız uçta (personel çalışma) yanıtın kendisi. */
export type SummaryOf<P extends ReportPath> =
  Response<P> extends { ozet: infer S; donem: unknown } ? NonNullable<S> : Response<P>;

/** Sayfalı satır tipi (`satirlar.kayitlar`); satırsız uçta `never`. */
export type RowOf<P extends ReportPath> =
  Response<P> extends { satirlar: { kayitlar: readonly (infer R)[] } } ? R : never;

/** Uç sorgusunun parametre adları (sayfa/boyut/sirala dahil). */
export type QueryOf<P extends ReportPath> = paths[P]['get']['parameters'] extends {
  query?: infer Q;
}
  ? keyof NonNullable<Q> & string
  : never;

/** Değer biçimi: kartlarda, özet tablolarında ve satır tablosunda aynı. */
export type ValueKind =
  'para' | 'sayi' | 'tamsayi' | 'yuzde' | 'tarih' | 'tarihSaat' | 'metin' | 'bayrak';

export interface ReportColumn<R> {
  /** Sütun kodu; `sirala: true` ise sunucu sıralama alanı da budur. */
  readonly kod: string;
  readonly baslik: CeviriAnahtari;
  /** Veriden gelen başlık (pivot hizmet adı, ay, gün); verilirse `baslik` yerine gösterilir. */
  readonly baslikMetni?: string;
  readonly tur: ValueKind;
  readonly deger: (row: R) => unknown;
  /** `para` için ISO kod (satırdan); verilmezse TRY. */
  readonly paraBirimi?: (row: R) => string | null | undefined;
  readonly sirala?: boolean;
  readonly gizli?: boolean;
  readonly sabit?: boolean;
  /** Uygulama içi bağlantı (router komutları); `null` → düz metin. */
  readonly bag?: (row: R) => readonly string[] | null;
}

export interface ReportCard<S> {
  readonly baslik: CeviriAnahtari;
  readonly tur: ValueKind;
  readonly deger: (summary: S) => unknown;
  /** Negatifse hata rengi (net kâr, bakiye). */
  readonly isaretli?: boolean;
}

/** Özetin içindeki dizi (KDV oranları, kırılımlar, pivot, matris) — sayfasız düz tablo. */
export interface ReportSection<S> {
  readonly kod: string;
  readonly baslik: CeviriAnahtari;
  readonly satirlar: (summary: S) => readonly unknown[] | null | undefined;
  readonly sutunlar: (summary: S) => readonly ReportColumn<unknown>[];
  /** Alt toplam satırı (sütun kodu → değer). */
  readonly toplam?: (summary: S) => Readonly<Record<string, unknown>> | null;
}

/** Özetten türeyen bilgi/uyarı satırı (kırpıldı, atlanan dövizli alış…). */
export interface ReportNotice<S> {
  readonly metin: CeviriAnahtari;
  readonly goster: (summary: S) => boolean;
  readonly parametreler?: (summary: S) => Record<string, unknown>;
}

export interface FilterOption {
  readonly deger: string;
  readonly etiket: CeviriAnahtari;
}

/** Seçenekleri sunucu listesinden gelen kimlik süzgeçleri (`{ id, etiket }`). */
export type ListSource = 'finansHesap' | 'sube';

/**
 * `ad` = URL anahtarı (raporun görünümleri arasında ortak), `param` = API parametresi. `param` yoksa `ad` uç
 * sorgusunda olmalı (derleme); URL adı ayrılmışsa (`boyut` = sayfa boyutu) `param` ile eşlenir.
 */
type FilterBase<Q extends string> = (
  { readonly ad: Q; readonly param?: undefined } | { readonly ad: string; readonly param: Q }
) & {
  readonly baslik: CeviriAnahtari;
  readonly ipucu?: CeviriAnahtari;
};

export type ReportFilter<S, Q extends string = string> =
  /** `bas`/`bit` — İstanbul takvim günü aralığı (tek seçici). */
  | { readonly tur: 'donem'; readonly ipucu?: CeviriAnahtari }
  | (FilterBase<Q> & { readonly tur: 'gun' })
  | (FilterBase<Q> & {
      readonly tur: 'metin';
      readonly enFazla?: number;
      /** Seç-veya-yaz önerileri (son yanıtın özetinden). */
      readonly oneriler?: (summary: S) => readonly string[];
    })
  | (FilterBase<Q> & {
      readonly tur: 'secim';
      readonly secenekler: readonly FilterOption[];
      readonly bosEtiket?: CeviriAnahtari;
    })
  | (FilterBase<Q> & { readonly tur: 'bayrak' })
  | (FilterBase<Q> & { readonly tur: 'sayi'; readonly enAz?: number; readonly enFazla?: number })
  | (FilterBase<Q> & { readonly tur: 'arama'; readonly kaynak: SecimUcu })
  | (FilterBase<Q> & { readonly tur: 'liste'; readonly kaynak: ListSource })
  /** Şube adı: kapsamsız kullanıcıda seç-veya-yaz, şube kapsamlıda KENDİ şubesi (sabit, gönderilmez). */
  | (FilterBase<Q> & { readonly tur: 'sube' });

export interface ReportRows {
  readonly sutunlar: readonly ReportColumn<unknown>[];
  /** Sunucunun `SiralamaHaritasi` beyaz listesi (sütunların `sirala`sı bunun alt kümesi). */
  readonly siralanabilir: readonly string[];
  readonly satirKimligi: (row: unknown) => string;
  /** Kullanıcı düzeni anahtarı (`rapor.<rapor>.<görünüm>`). */
  readonly tabloKodu: string;
}

/** Tipten arındırılmış görünüm (bileşen bunu çizer). */
export interface ReportView {
  readonly kod: string;
  readonly baslik?: CeviriAnahtari;
  readonly uc: ReportPath;
  readonly filtreler: readonly ReportFilter<unknown>[];
  /** Her istekte sabit parametreler (ör. araç durum takip `gorunum=arac`). */
  readonly sabit?: Readonly<Record<string, string>>;
  readonly kartlar: readonly ReportCard<unknown>[];
  readonly bolumler: readonly ReportSection<unknown>[];
  readonly uyarilar: readonly ReportNotice<unknown>[];
  readonly satirlar: ReportRows | null;
  /** Yanıt zarfsız (`{ donem, ozet, satirlar, export }` değil). */
  readonly zarfsiz: boolean;
}

export type ReportGroup = 'finans' | 'cari' | 'satis' | 'filo' | 'operasyon';

export interface ReportDefinition {
  /** Rota ve kod: `/raporlar/<kod>` (Blazor yoluyla aynı). */
  readonly kod: string;
  readonly baslik: CeviriAnahtari;
  readonly aciklama?: CeviriAnahtari;
  readonly grup: ReportGroup;
  /** Uçla BİREBİR: `ViewReports` ya da `OperationsWrite ∨ ViewReports`. */
  readonly izinler: readonly Izin[];
  /** Uç firma geneli (şube kapsamlıya 403). */
  readonly firmaGeneli: boolean;
  /** Rota `:id` taşır (araç karnesi); uçtaki `{id}` bununla dolar. */
  readonly kimlikli?: boolean;
  readonly gorunumler: readonly ReportView[];
}

export const VIEW_REPORTS: readonly Izin[] = ['ViewReports'];
export const OPS_OR_VIEW: readonly Izin[] = ['OperationsWrite', 'ViewReports'];

// ─── Tanım yardımcıları (tip → düz model) ───────────────────────────────────────────────────

const OBJECT_KEYS = new WeakMap<object, string>();
let objectKeySeq = 0;

/** Kimliksiz satırın (defter satırı, vardiya matrisi) nesne başına kararlı anahtarı. */
export function objectKey(row: unknown): string {
  if (typeof row !== 'object' || row === null) return String(row);
  let key = OBJECT_KEYS.get(row);
  if (key === undefined) {
    key = `s${++objectKeySeq}`;
    OBJECT_KEYS.set(row, key);
  }
  return key;
}

/** Oran (0,20) → yüzde (20). */
export function fractionToPercent(value: unknown): number | null {
  const n = typeof value === 'number' ? value : typeof value === 'string' ? Number(value) : NaN;
  return Number.isFinite(n) ? n * 100 : null;
}

/** Pay/payda yüzdesi; payda ≤ 0 → `null` (yanıltıcı oran yok). */
export function ratioPercent(part: unknown, whole: unknown): number | null {
  const p = Number(part);
  const w = Number(whole);
  return Number.isFinite(p) && Number.isFinite(w) && w > 0 ? (p / w) * 100 : null;
}

/** Alan başlığı: `rapor.alan.<alan>` (ortak sözlük; aynı alan her raporda aynı adla). */
export function fieldLabel(field: string): CeviriAnahtari {
  return `rapor.alan.${field}` as CeviriAnahtari;
}

type ColumnOptions<R> = Partial<Omit<ReportColumn<R>, 'kod' | 'tur' | 'deger'>>;

/** Satır tipine bağlı sütun kurucu: `alan` satır şemasında olmalı (derleme). */
export function columnsFor<R>() {
  return {
    field<K extends keyof R & string>(
      alan: K,
      tur: ValueKind,
      options: ColumnOptions<R> = {},
    ): ReportColumn<R> {
      return { baslik: fieldLabel(alan), ...options, kod: alan, tur, deger: (r) => r[alan] };
    },
    computed(
      kod: string,
      tur: ValueKind,
      deger: (row: R) => unknown,
      options: ColumnOptions<R> & { readonly baslik: CeviriAnahtari },
    ): ReportColumn<R> {
      return { ...options, kod, tur, deger };
    },
  };
}

/** Özet tipine bağlı kart kurucu. */
export function cardsFor<S>() {
  return {
    field<K extends keyof S & string>(
      alan: K,
      tur: ValueKind,
      options: { readonly baslik?: CeviriAnahtari; readonly isaretli?: boolean } = {},
    ): ReportCard<S> {
      return {
        baslik: options.baslik ?? fieldLabel(alan),
        tur,
        isaretli: options.isaretli,
        deger: (s) => s[alan],
      };
    },
    computed(
      baslik: CeviriAnahtari,
      tur: ValueKind,
      deger: (summary: S) => unknown,
      isaretli = false,
    ): ReportCard<S> {
      return { baslik, tur, deger, isaretli };
    },
  };
}

/** Özet içi tablo (satır tipi burada bağlanır, sonra silinir). */
export function section<S, R>(def: {
  readonly kod: string;
  readonly baslik: CeviriAnahtari;
  readonly satirlar: (summary: S) => readonly R[] | null | undefined;
  readonly sutunlar: readonly ReportColumn<R>[] | ((summary: S) => readonly ReportColumn<R>[]);
  readonly toplam?: (summary: S) => Readonly<Record<string, unknown>> | null;
}): ReportSection<S> {
  const cols = def.sutunlar;
  return {
    kod: def.kod,
    baslik: def.baslik,
    satirlar: def.satirlar,
    sutunlar: (s) =>
      (typeof cols === 'function' ? cols(s) : cols) as readonly ReportColumn<unknown>[],
    toplam: def.toplam,
  };
}

type Summary<P extends ReportPath> = SummaryOf<P>;

/** Uç yoluna tipli görünüm tanımı → düz `ReportView`. */
export function defineView<P extends ReportPath>(
  uc: P,
  def: {
    readonly kod?: string;
    readonly baslik?: CeviriAnahtari;
    readonly filtreler?: readonly ReportFilter<Summary<P>, QueryOf<P>>[];
    readonly sabit?: Readonly<Partial<Record<QueryOf<P>, string>>>;
    readonly kartlar?: readonly ReportCard<Summary<P>>[];
    readonly bolumler?: readonly ReportSection<Summary<P>>[];
    readonly uyarilar?: readonly ReportNotice<Summary<P>>[];
    readonly satirlar?: [RowOf<P>] extends [never]
      ? never
      : {
          readonly sutunlar: readonly ReportColumn<RowOf<P>>[];
          readonly siralanabilir: readonly QueryOf<P>[] | readonly string[];
          readonly satirKimligi: (row: RowOf<P>) => string;
        };
    readonly zarfsiz?: boolean;
  },
): (reportCode: string) => ReportView {
  return (reportCode) => {
    const kod = def.kod ?? 'ana';
    const rows = def.satirlar as
      | {
          readonly sutunlar: readonly ReportColumn<unknown>[];
          readonly siralanabilir: readonly string[];
          readonly satirKimligi: (row: unknown) => string;
        }
      | undefined;
    return {
      kod,
      baslik: def.baslik,
      uc,
      filtreler: (def.filtreler ?? []) as readonly ReportFilter<unknown>[],
      sabit: def.sabit as Readonly<Record<string, string>> | undefined,
      kartlar: (def.kartlar ?? []) as readonly ReportCard<unknown>[],
      bolumler: (def.bolumler ?? []) as readonly ReportSection<unknown>[],
      uyarilar: (def.uyarilar ?? []) as readonly ReportNotice<unknown>[],
      satirlar: rows ? { ...rows, tabloKodu: `rapor.${reportCode}.${kod}` } : null,
      zarfsiz: def.zarfsiz ?? false,
    };
  };
}

/** Rapor tanımı: görünüm kurucularına rapor kodu verilir (tablo düzeni anahtarı). */
export function defineReport(
  def: Omit<ReportDefinition, 'gorunumler'> & {
    readonly gorunumler: readonly ((reportCode: string) => ReportView)[];
  },
): ReportDefinition {
  return { ...def, gorunumler: def.gorunumler.map((v) => v(def.kod)) };
}
