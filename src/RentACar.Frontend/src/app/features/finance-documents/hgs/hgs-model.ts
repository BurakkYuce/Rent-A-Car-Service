import type { Schema } from '@core/api/ui-tipleri';
import { trLowerCase } from '@core/metin/tr-normalize';
import { listDefinition } from '@core/veri/liste-sorgusu';

/** `/api/ui/v1/hgs/*` (HGS geçişleri: dosyadan içe aktar, liste, yansıt). */
export const HGS = '/api/ui/v1/hgs';

export type HgsCrossingRow = Schema<'HgsCrossingRow'>;
export type HgsImportResult = Schema<'HgsImportResultDto'>;
export type HgsImportRow = Schema<'HgsImportRowDto'>;
export type HgsField = Schema<'HgsFieldDto'>;
export type HgsReflectResult = Schema<'HgsReflectResultDto'>;
export type HgsRematchResult = Schema<'HgsRematchResult'>;

/** Sunucu enum ADLARI (`HgsCrossingStatus`; tanımsız ad 400). */
export const HGS_STATUSES = ['Eslesti', 'KiraDisi', 'Belirsiz', 'Yansitildi'] as const;
export type HgsStatus = (typeof HGS_STATUSES)[number];

/** Önizleme satır durumları (`HgsImportRowStatus`). */
export const HGS_IMPORT_STATUSES = [
  'Eslesti',
  'KiraDisi',
  'Belirsiz',
  'Mukerrer',
  'Hatali',
] as const;

export const HGS_LIST = listDefinition({
  filtreler: {
    plaka: { tur: 'metin', enFazla: 16 },
    bas: { tur: 'tarih' },
    bit: { tur: 'tarih' },
    durum: { tur: 'secim', degerler: HGS_STATUSES },
    kiraId: { tur: 'kimlik' },
    iceAktarimId: { tur: 'kimlik' },
  },
  siralanabilir: ['gecisZamani', 'plaka', 'tutar', 'durum', 'sozlesmeNo', 'cariAd'],
  varsayilanSirala: null,
  varsayilanBoyut: 100,
});

/** Sunucunun yükleme sınırı (`HgsUiApi.UploadLimit`, 5 MB). */
export const HGS_MAX_BYTES = 5 * 1024 * 1024;

/** Yansıtma hizmet oranı varsayılanı (referans ekranla ve mevcut HGS yansıtmasıyla aynı: %3). */
export const DEFAULT_SERVICE_RATE = 1.03;

/** Tek yansıtma isteğindeki geçiş sınırı (`HgsCrossingService.MaxReflectIds`). */
export const MAX_REFLECT = 500;

/** Yalnız `.xlsx` ve `.csv` (`.xls` ClosedXML ile okunamaz; sunucu da reddeder). */
export function acceptedFile(name: string): boolean {
  const dot = name.lastIndexOf('.');
  if (dot < 0) return false;
  const extension = trLowerCase(name.slice(dot));
  return extension === '.xlsx' || extension === '.csv';
}

/**
 * Sütun eşlemesi → sunucunun beklediği JSON (`{"plaka":"Araç Plakası", …}`). Boş seçim `null` gider (alan
 * eşlenmedi). Eşleme hiç değiştirilmediyse `null` döner: sunucu kendi önerisini kullanır.
 */
export function mappingJson(
  fields: readonly HgsField[],
  chosen: Readonly<Record<string, string | null>>,
): string | null {
  let changed = false;
  const out: Record<string, string | null> = {};
  for (const f of fields) {
    const value = Object.hasOwn(chosen, f.kod) ? (chosen[f.kod] ?? null) : (f.baslik ?? null);
    if (value !== (f.baslik ?? null)) changed = true;
    out[f.kod] = value;
  }
  return changed ? JSON.stringify(out) : null;
}

/** Yansıtılabilir mi: yalnız kiraya eşleşmiş ve henüz yansıtılmamış geçiş. */
export function reflectable(row: Pick<HgsCrossingRow, 'durum' | 'kiraId'>): boolean {
  return row.durum === 'Eslesti' && row.kiraId !== null && row.kiraId !== undefined;
}
