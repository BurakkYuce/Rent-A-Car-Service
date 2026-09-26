import type { Sayfa } from '@core/api/sayfa';
import type { StoreState } from '@core/veri/temel-store';

/**
 * Tablo motoru sözleşmesi (F3.5). Özellik ekranları yalnız bu tipleri ve `<rc-tablo>`'yu kullanır;
 * TanStack tipleri dışarı sızmaz (motor değişirse ekranlar değişmez).
 */

/** Hücre biçimi. `para` ve `sayi` sağa yaslanır ve tabular rakamla yazılır. */
export type TableColumnType = 'metin' | 'para' | 'sayi' | 'tarih' | 'tarihSaat';

export type TableAlignment = 'bas' | 'son' | 'orta';

/**
 * Sütun tanımı. `kod` kalıcıdır: kullanıcının kayıtlı düzeni (`TabloDuzenleri`) ve hücre şablonu
 * (`<ng-template rcTabloHucre="kod">`) bununla eşlenir — kodu değiştirmek kullanıcıların o sütun
 * için kayıtlı genişlik/sıra/görünürlüğünü düşürür.
 */
export interface TabloSutunu<T> {
  /** `^[A-Za-z0-9_.-]{1,64}$` (sunucu aynı kuralı uygular). */
  readonly kod: string;
  /** Görünen başlık (çevrilmiş metin). */
  readonly baslik: string;
  /** Hücre değeri. Şablonsuz sütunda `tur`'a göre biçimlenir. */
  readonly deger: (row: T) => unknown;
  /** Varsayılan `metin`. */
  readonly tur?: TableColumnType;
  /** `para` için para birimi (ISO kodu); sabit ya da satırdan. Varsayılan `TRY`. */
  readonly paraBirimi?: string | ((row: T) => string);
  /** `sayi` için Angular `digitsInfo` (varsayılan `1.0-2`). */
  readonly haneler?: string;
  /** Varsayılan genişlik (px). */
  readonly genislik?: number;
  /** Elle daraltmada alt sınır (px). */
  readonly enAzGenislik?: number;
  /**
   * Sunucu sıralaması: `true` → alan adı `kod`; metin → o alan (sunucunun `SiralamaHaritasi`
   * beyaz listesindeki ad). Verilmezse sütun sıralanamaz.
   */
  readonly sirala?: boolean | string;
  /** İlk açılışta gizli (sütun seçicide yine listelenir). */
  readonly gizli?: boolean;
  /** Gizlenemez (ör. plaka). Sabit sütunlar da gizlenemez. */
  readonly gizlenemez?: boolean;
  /**
   * Solda sabit: yatay kaydırmada yerinde kalır. Sabit sütunlar tanım sırasıyla en solda durur,
   * taşınamaz ve gizlenemez.
   */
  readonly sabit?: boolean;
  /** Varsayılan hizalama türden gelir (`para`/`sayi` → `son`). */
  readonly hizala?: TableAlignment;
}

/**
 * Tablonun veri kaynağı: F3.4 `TemelStore`'un durumu olduğu gibi (`store.liste.durum()`). Dört durum
 * AYRI çizilir: `bos` (henüz yüklenmedi — mesaj yok), `yukleniyor` (iskelet ya da `onceki` veri
 * soluk), `hazir` (satırlar; sıfır kayıtsa "Kayıt bulunamadı"), `hata` (hata bandı + yeniden dene —
 * ASLA "kayıt yok" gibi görünmez). `Sayfa<T>` sunucu sayfalamasını, dizi sayfasız listeyi çizer.
 */
export type TableSource<T> = StoreState<Sayfa<T>> | StoreState<readonly T[]>;

/** Sayfalama çubuğunun girdisi (`Sayfa<T>`'den). */
export interface TabloSayfasi {
  /** 1 tabanlı. */
  readonly sayfa: number;
  readonly boyut: number;
  /** Filtreye uyan toplam kayıt. */
  readonly toplam: number;
}

/**
 * Kullanıcı düzeni (istemci modeli). Sunucu biçimi `@core/api/ui-tipleri` `TabloDuzeniVerisi`'dir;
 * bu model ona ATANABİLİR (PUT gövdesi) ve okunan yanıt `duzeniBirlestir` ile buna normalize edilir.
 */
export interface TabloSutunDuzeni {
  readonly kod: string;
  readonly gorunur: boolean;
  /** `null` = tanımdaki varsayılan genişlik. */
  readonly genislik: number | null;
}

export interface TabloSiralamaDuzeni {
  readonly kod: string;
  readonly azalan: boolean;
}

export interface TabloDuzeni {
  /** Dizi sırası = sütun sırası. */
  readonly sutunlar: readonly TabloSutunDuzeni[];
  readonly siralama: readonly TabloSiralamaDuzeni[];
}

/** Satır seçim sütununun iç kimliği (sütun kodlarıyla çakışmaz: `_` ile başlayan kod sunucuda da geçerli ama motor ayırır). */
export const SELECTION_COLUMN = '__secim';

/** Sunucu doğrulamasıyla aynı sınırlar (`TabloDuzeniService`). */
export const TABLE_LIMITS = {
  enAzGenislik: 24,
  enFazlaGenislik: 2000,
  enFazlaSutun: 200,
  enFazlaSiralama: 5,
  kodDeseni: /^[A-Za-z0-9_.-]{1,64}$/,
  tabloKoduDeseni: /^[a-z0-9]+([.-][a-z0-9]+)*$/,
} as const;

export const DEFAULT_COLUMN_WIDTH = 140;
export const DEFAULT_MIN_WIDTH = 48;
export const SELECTION_COLUMN_WIDTH = 36;
