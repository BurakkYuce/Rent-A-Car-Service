/**
 * Dışa aktarma SUNUCU uçlarıyla (F3.5): istemcide Excel/CSV üretilmez. Mevcut Blazor uçları
 * `GET /listeler/export/{liste}` ve `GET /raporlar/export/{rapor}` (`?format=excel|csv|pdf` + ekrandaki
 * filtreler — "gördüğün = indirdiğin"). Oturum çerezi aynı kökene giden GET ile gider; indirme
 * tarayıcının kendi gezinmesidir (`<a href>`), JS `fetch`/Blob yok → CSP `connect-src` etkilenmez.
 */

export type ExportFormat = 'excel' | 'csv' | 'pdf';

export type ExportPath = `/listeler/export/${string}` | `/raporlar/export/${string}`;

type ParameterValue = string | number | boolean | null | undefined;

export interface DisaAktarma {
  /** Sunucu ucu (kök-göreli). */
  readonly yol: ExportPath;
  /** Ekrandaki filtreler (F3.4 `apiParametreleri(tanim, sorgu)` çıktısı doğrudan verilebilir). */
  readonly parametreler?: Readonly<Record<string, ParameterValue | readonly ParameterValue[]>>;
  /** Gösterilecek biçimler; varsayılan Excel + CSV. */
  readonly bicimler?: readonly ExportFormat[];
}

/**
 * Sayfalama parametreleri dışa aktarmaya taşınmaz: dosya ekrandaki SAYFAYI değil, filtreye uyan
 * TÜM kayıtları içerir (sunucu uçları sayfasız). Sıralama taşınır (uç destekliyorsa uygular).
 */
const NOT_CARRIED: ReadonlySet<string> = new Set(['sayfa', 'boyut', 'format']);

export function exportUrl(exportItem: DisaAktarma, format: ExportFormat): string {
  const path = exportItem.yol;
  if (!/^\/(listeler|raporlar)\/export\/[a-z0-9-]+$/.test(path)) {
    throw new TypeError(`Geçersiz dışa aktarma yolu: "${path}".`);
  }
  const parameters = new URLSearchParams();
  parameters.set('format', format);
  for (const [name, value] of Object.entries(exportItem.parametreler ?? {})) {
    if (NOT_CARRIED.has(name)) continue;
    const values: readonly ParameterValue[] = Array.isArray(value)
      ? (value as readonly ParameterValue[])
      : [value as ParameterValue];
    for (const tek of values) {
      if (tek === null || tek === undefined || tek === '') continue;
      parameters.append(name, String(tek));
    }
  }
  return `${path}?${parameters.toString()}`;
}
