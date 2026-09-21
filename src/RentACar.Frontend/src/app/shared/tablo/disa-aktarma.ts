/**
 * Dışa aktarma SUNUCU uçlarıyla (F3.5): istemcide Excel/CSV üretilmez. Mevcut Blazor uçları
 * `GET /listeler/export/{liste}` ve `GET /raporlar/export/{rapor}` (`?format=excel|csv|pdf` + ekrandaki
 * filtreler — "gördüğün = indirdiğin"). Oturum çerezi aynı kökene giden GET ile gider; indirme
 * tarayıcının kendi gezinmesidir (`<a href>`), JS `fetch`/Blob yok → CSP `connect-src` etkilenmez.
 */

export type DisaAktarmaBicimi = 'excel' | 'csv' | 'pdf';

export type DisaAktarmaYolu = `/listeler/export/${string}` | `/raporlar/export/${string}`;

type ParametreDegeri = string | number | boolean | null | undefined;

export interface DisaAktarma {
  /** Sunucu ucu (kök-göreli). */
  readonly yol: DisaAktarmaYolu;
  /** Ekrandaki filtreler (F3.4 `apiParametreleri(tanim, sorgu)` çıktısı doğrudan verilebilir). */
  readonly parametreler?: Readonly<Record<string, ParametreDegeri | readonly ParametreDegeri[]>>;
  /** Gösterilecek biçimler; varsayılan Excel + CSV. */
  readonly bicimler?: readonly DisaAktarmaBicimi[];
}

/**
 * Sayfalama parametreleri dışa aktarmaya taşınmaz: dosya ekrandaki SAYFAYI değil, filtreye uyan
 * TÜM kayıtları içerir (sunucu uçları sayfasız). Sıralama taşınır (uç destekliyorsa uygular).
 */
const TASINMAYANLAR: ReadonlySet<string> = new Set(['sayfa', 'boyut', 'format']);

export function disaAktarmaAdresi(disaAktarma: DisaAktarma, bicim: DisaAktarmaBicimi): string {
  const yol = disaAktarma.yol;
  if (!/^\/(listeler|raporlar)\/export\/[a-z0-9-]+$/.test(yol)) {
    throw new TypeError(`Geçersiz dışa aktarma yolu: "${yol}".`);
  }
  const parametreler = new URLSearchParams();
  parametreler.set('format', bicim);
  for (const [ad, deger] of Object.entries(disaAktarma.parametreler ?? {})) {
    if (TASINMAYANLAR.has(ad)) continue;
    const degerler: readonly ParametreDegeri[] = Array.isArray(deger)
      ? (deger as readonly ParametreDegeri[])
      : [deger as ParametreDegeri];
    for (const tek of degerler) {
      if (tek === null || tek === undefined || tek === '') continue;
      parametreler.append(ad, String(tek));
    }
  }
  return `${yol}?${parametreler.toString()}`;
}
