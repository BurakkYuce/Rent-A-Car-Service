import type { QueryParameters } from '@core/api/api-istemcisi';
import type { Schema } from '@core/api/ui-tipleri';
import { listDefinition } from '@core/veri/liste-sorgusu';

export type AvailabilityResponse = Schema<'MusaitlikYaniti'>;
export type AvailabilityRow = Schema<'MusaitlikSatiri'>;
export type AvailabilityOptions = Schema<'MusaitlikSecenekleri'>;
export type RentQuery = Schema<'KiralaSorgusu'>;

/**
 * Müsaitlik URL ↔ API sözleşmesi (`GET /api/ui/v1/musaitlik`, `PlanlamaApi.MusaitlikSorgusu`). Blazor
 * `/musaitlik` süzgeçlerinin tamamı (FAZ-48/73): pencere (başlangıç günü + bitiş günü YA DA gün sayısı +
 * alış/dönüş saati), grup, şube, rezervasyon kaynağı (fiyat kanalı + broker çiti), döviz süzgeci, plaka.
 * Sayfalama yok; `sayfa`/`boyut` API'ye gitmez.
 */
export const MUSAITLIK = listDefinition({
  filtreler: {
    basGun: { tur: 'tarih' },
    bitGun: { tur: 'tarih' },
    gun: { tur: 'tamsayi', enAz: 1, enFazla: 365 },
    basSaat: { tur: 'metin', enFazla: 5 },
    bitSaat: { tur: 'metin', enFazla: 5 },
    grup: { tur: 'metin', enFazla: 64 },
    sube: { tur: 'metin', enFazla: 128 },
    rezKaynak: { tur: 'metin', enFazla: 128 },
    doviz: { tur: 'metin', enFazla: 3 },
    plaka: { tur: 'metin', enFazla: 32 },
  },
});

const HOUR = /^([01]\d|2[0-3]):[0-5]\d$/;

/**
 * Arama parametreleri; başlangıç günü yoksa `null` (Blazor: ilk açılışta arama YAPILMAZ). Bozuk saat
 * (`25:00`, elle yazılmış URL) gönderilmez — sunucu bağlama hatası üretmesin; saat verilmezse 00:00.
 */
export function searchParams(p: QueryParameters): QueryParameters | null {
  if (typeof p['basGun'] !== 'string') return null;
  const result: Record<string, QueryParameters[string]> = {};
  for (const [name, value] of Object.entries(p)) {
    if (name === 'sayfa' || name === 'boyut' || name === 'sirala') continue;
    if (
      (name === 'basSaat' || name === 'bitSaat') &&
      (typeof value !== 'string' || !HOUR.test(value))
    )
      continue;
    result[name] = value;
  }
  return result;
}

/**
 * Kira formuna araç + ÇÖZÜLMÜŞ pencere taşıyan bağlantının sorgusu (F4.3 `?varac&vfrom&vto&vgrup`).
 * Tarihler sunucunun `kiralaSorgusu`'ndan (İstanbul takvim günü; gün-sayısı modunda bitiş alanı boştur,
 * ham alan taşınsaydı kira formuna eksik tarih giderdi). Grup yalnız süzgeçte seçildiyse.
 */
export function rentParameters(vehicleId: string, s: RentQuery): Record<string, string> {
  const result: Record<string, string> = { varac: vehicleId, vfrom: s.vfrom, vto: s.vto };
  if (s.vgrup) result['vgrup'] = s.vgrup;
  return result;
}

/** JSON sayısı (`number | string`) → gösterim sayısı. YALNIZ gösterim. */
export function count(value: number | string | null | undefined): number | null {
  if (typeof value === 'number') return Number.isFinite(value) ? value : null;
  if (typeof value === 'string' && value.trim() !== '') {
    const n = Number(value);
    return Number.isFinite(n) ? n : null;
  }
  return null;
}
