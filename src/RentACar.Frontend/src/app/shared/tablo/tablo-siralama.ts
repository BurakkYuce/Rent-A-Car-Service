import type { TabloSiralamaDuzeni, TabloSutunu } from './tablo-modeli';

/**
 * Sunucu sıralaması ↔ tablo durumu (saf). Biçim F3.4 liste sorgusuyla (`ListeIstegi.sirala`) aynı:
 * `"alan"` artan, `"-alan"` azalan; alan sunucunun beyaz listesindeki ad. Sunucu bilinmeyen alanı
 * sessizce yok saymaz (400) — bu yüzden yalnız `sirala` tanımlı sütunlar metne çevrilir.
 */

type Columns = readonly TabloSutunu<never>[];

/** Sütunun sunucu sıralama alanı; sıralanamazsa `null`. */
export function sortField(column: TabloSutunu<never>): string | null {
  if (column.sirala === true) return column.kod;
  if (typeof column.sirala === 'string' && column.sirala.trim() !== '') return column.sirala.trim();
  return null;
}

/** `"-gunlukFiyat"` → `{ kod: 'gunluk', azalan: true }` (alan → sütun kodu). Bilinmeyen → `null`. */
export function parseSort(columns: Columns, sort: string | null): TabloSiralamaDuzeni | null {
  if (sort === null) return null;
  const text = sort.trim();
  const descending = text.startsWith('-');
  const alan = descending ? text.slice(1) : text;
  if (alan === '') return null;
  const column = columns.find((s) => sortField(s) === alan);
  return column === undefined ? null : { kod: column.kod, azalan: descending };
}

/** `{ kod, azalan }` → sunucu metni. Sütun yoksa ya da sıralanamazsa `null`. */
export function sortText(columns: Columns, sort: TabloSiralamaDuzeni | null): string | null {
  if (sort === null) return null;
  const column = columns.find((s) => s.kod === sort.kod);
  const alan = column === undefined ? null : sortField(column);
  if (alan === null) return null;
  return sort.azalan ? `-${alan}` : alan;
}

/**
 * Başlığa tıklama döngüsü: başka sütun/yok → artan → azalan → yok (`null` = sayfanın varsayılan
 * sıralaması). Sıralanamaz sütunda mevcut durum değişmez.
 */
export function nextSort(
  columns: Columns,
  existing: TabloSiralamaDuzeni | null,
  code: string,
): TabloSiralamaDuzeni | null {
  const column = columns.find((s) => s.kod === code);
  if (column === undefined || sortField(column) === null) return existing;
  if (existing === null || existing.kod !== code) return { kod: code, azalan: false };
  if (!existing.azalan) return { kod: code, azalan: true };
  return null;
}

/** `aria-sort` değeri. */
export function ariaSort(
  sort: TabloSiralamaDuzeni | null,
  code: string,
): 'ascending' | 'descending' | null {
  if (sort?.kod !== code) return null;
  return sort.azalan ? 'descending' : 'ascending';
}
