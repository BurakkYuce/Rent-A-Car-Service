import type { TabloSiralamaDuzeni, TabloSutunu } from './tablo-modeli';

/**
 * Sunucu sıralaması ↔ tablo durumu (saf). Biçim F3.4 liste sorgusuyla (`ListeIstegi.sirala`) aynı:
 * `"alan"` artan, `"-alan"` azalan; alan sunucunun beyaz listesindeki ad. Sunucu bilinmeyen alanı
 * sessizce yok saymaz (400) — bu yüzden yalnız `sirala` tanımlı sütunlar metne çevrilir.
 */

type Sutunlar = readonly TabloSutunu<never>[];

/** Sütunun sunucu sıralama alanı; sıralanamazsa `null`. */
export function siralamaAlani(sutun: TabloSutunu<never>): string | null {
  if (sutun.sirala === true) return sutun.kod;
  if (typeof sutun.sirala === 'string' && sutun.sirala.trim() !== '') return sutun.sirala.trim();
  return null;
}

/** `"-gunlukFiyat"` → `{ kod: 'gunluk', azalan: true }` (alan → sütun kodu). Bilinmeyen → `null`. */
export function siralamaCoz(sutunlar: Sutunlar, sirala: string | null): TabloSiralamaDuzeni | null {
  if (sirala === null) return null;
  const metin = sirala.trim();
  const azalan = metin.startsWith('-');
  const alan = azalan ? metin.slice(1) : metin;
  if (alan === '') return null;
  const sutun = sutunlar.find((s) => siralamaAlani(s) === alan);
  return sutun === undefined ? null : { kod: sutun.kod, azalan };
}

/** `{ kod, azalan }` → sunucu metni. Sütun yoksa ya da sıralanamazsa `null`. */
export function siralamaMetni(
  sutunlar: Sutunlar,
  siralama: TabloSiralamaDuzeni | null,
): string | null {
  if (siralama === null) return null;
  const sutun = sutunlar.find((s) => s.kod === siralama.kod);
  const alan = sutun === undefined ? null : siralamaAlani(sutun);
  if (alan === null) return null;
  return siralama.azalan ? `-${alan}` : alan;
}

/**
 * Başlığa tıklama döngüsü: başka sütun/yok → artan → azalan → yok (`null` = sayfanın varsayılan
 * sıralaması). Sıralanamaz sütunda mevcut durum değişmez.
 */
export function sonrakiSiralama(
  sutunlar: Sutunlar,
  mevcut: TabloSiralamaDuzeni | null,
  kod: string,
): TabloSiralamaDuzeni | null {
  const sutun = sutunlar.find((s) => s.kod === kod);
  if (sutun === undefined || siralamaAlani(sutun) === null) return mevcut;
  if (mevcut === null || mevcut.kod !== kod) return { kod, azalan: false };
  if (!mevcut.azalan) return { kod, azalan: true };
  return null;
}

/** `aria-sort` değeri. */
export function ariaSiralama(
  siralama: TabloSiralamaDuzeni | null,
  kod: string,
): 'ascending' | 'descending' | null {
  if (siralama?.kod !== kod) return null;
  return siralama.azalan ? 'descending' : 'ascending';
}
