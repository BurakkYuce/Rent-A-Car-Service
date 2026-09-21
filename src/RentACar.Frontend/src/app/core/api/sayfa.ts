/**
 * Backend `/api/ui/v1` liste sözleşmesinin istemci karşılığı (F1.3, `Application/Common`).
 * Alan adları JSON'daki (camelCase) hâliyle; değişirse OpenAPI anlık görüntüsüyle birlikte değişir.
 */

/** İstek yarısı (`ListeIstegi`): sorgu parametresi olarak `sayfa`, `boyut`, `sirala`. */
export interface ListeIstegi {
  /** 1 tabanlı; sunucu < 1'i 1'e çeker. */
  readonly sayfa: number;
  /** 1..200; sunucu aralık dışını kırpar. */
  readonly boyut: number;
  /** `"alan"` (artan) ya da `"-alan"` (azalan); alan sunucuda beyaz listeden geçer, bilinmeyen alan 400. */
  readonly sirala: string | null;
}

/** Yanıt yarısı (`Sayfa<T>`): o sayfanın kayıtları + filtreye uyan toplam. */
export interface Sayfa<T> {
  readonly kayitlar: readonly T[];
  readonly toplam: number;
  readonly sayfaNo: number;
  readonly boyut: number;
}

/** Sunucu sabitleri (`ListeIstegi.EnFazlaBoyut`, varsayılan boyut). */
export const EN_FAZLA_BOYUT = 200;
export const VARSAYILAN_BOYUT = 50;

/** Toplam sayfa sayısı (backend `Sayfa<T>.ToplamSayfa` ile aynı kural); kayıt yoksa 0. */
export function toplamSayfa(sayfa: Pick<Sayfa<unknown>, 'toplam' | 'boyut'>): number {
  return sayfa.boyut <= 0 ? 0 : Math.ceil(sayfa.toplam / sayfa.boyut);
}
