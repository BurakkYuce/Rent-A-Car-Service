/**
 * Izgara klavye gezinmesi (saf, WAI-ARIA APG "Data Grid" deseni). Konum 0 tabanlı: `satir` 0 başlık
 * satırı, 1..n veri satırları (sayfadaki sıra); `sutun` görünür sütunlar (seçim sütunu dahil).
 *
 * | Tuş | Hareket |
 * |---|---|
 * | ← → ↑ ↓ | bir hücre (kenarda durur, sarmaz) |
 * | Home / End | satırın ilk / son hücresi |
 * | Ctrl(⌘)+Home / Ctrl(⌘)+End | ızgaranın ilk / son hücresi |
 * | PageUp / PageDown | `sayfaAdimi` satır (başlığa çıkmaz, veri satırında kalır) |
 */

export interface HucreKonumu {
  readonly satir: number;
  readonly sutun: number;
}

export interface IzgaraBoyutu {
  /** Başlık dahil satır sayısı (veri yoksa 1). */
  readonly satirSayisi: number;
  readonly sutunSayisi: number;
  /** PageUp/PageDown adımı (görünür satır sayısı). */
  readonly sayfaAdimi: number;
}

export interface GezinmeTusu {
  readonly key: string;
  readonly ctrlKey?: boolean;
  readonly metaKey?: boolean;
  readonly altKey?: boolean;
  readonly shiftKey?: boolean;
}

/** Konumu ızgaraya sığdırır (satır/sütun sayısı değişince aktif hücre dışarıda kalmasın). */
export function clampPosition(location: HucreKonumu, size: IzgaraBoyutu): HucreKonumu {
  return {
    satir: Math.min(Math.max(0, location.satir), Math.max(0, size.satirSayisi - 1)),
    sutun: Math.min(Math.max(0, location.sutun), Math.max(0, size.sutunSayisi - 1)),
  };
}

/**
 * Yeni konum; tuş gezinme tuşu değilse (ya da Alt/Shift ile — sütun taşıma/boyutlama kısayolları)
 * `null`. Konum değişmese bile (kenar) konum döner → çağıran olayı tüketir, sayfa kaymaz.
 */
export function navigateCell(
  location: HucreKonumu,
  tus: GezinmeTusu,
  size: IzgaraBoyutu,
): HucreKonumu | null {
  if (tus.altKey || tus.shiftKey || size.sutunSayisi === 0) return null;
  const k = clampPosition(location, size);
  const lastRow = size.satirSayisi - 1;
  const lastColumn = size.sutunSayisi - 1;
  const check = tus.ctrlKey === true || tus.metaKey === true;
  const step = Math.max(1, size.sayfaAdimi);
  switch (tus.key) {
    case 'ArrowRight':
      return { ...k, sutun: Math.min(lastColumn, k.sutun + 1) };
    case 'ArrowLeft':
      return { ...k, sutun: Math.max(0, k.sutun - 1) };
    case 'ArrowDown':
      return { ...k, satir: Math.min(lastRow, k.satir + 1) };
    case 'ArrowUp':
      return { ...k, satir: Math.max(0, k.satir - 1) };
    case 'Home':
      return check ? { satir: 0, sutun: 0 } : { ...k, sutun: 0 };
    case 'End':
      return check ? { satir: lastRow, sutun: lastColumn } : { ...k, sutun: lastColumn };
    case 'PageDown':
      return { ...k, satir: Math.min(lastRow, k.satir + step) };
    case 'PageUp':
      return { ...k, satir: k.satir === 0 ? 0 : Math.max(1, k.satir - step) };
    default:
      return null;
  }
}
