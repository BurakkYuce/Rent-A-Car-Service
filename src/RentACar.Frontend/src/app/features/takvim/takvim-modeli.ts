import type { QueryParameters } from '@core/api/api-istemcisi';
import type { Schema } from '@core/api/ui-tipleri';
import { listDefinition } from '@core/veri/liste-sorgusu';

export type CalendarResponse = Schema<'TakvimYaniti'>;
export type CalendarVehicle = Schema<'TakvimAraci'>;
export type CalendarOptions = Schema<'TakvimSecenekleri'>;

/** Gün hücresinin dolulukları (sunucu hesaplar; kira rezervasyonu ezer). */
export type Occupancy = 'Kira' | 'Rezervasyon';

/**
 * Takvim URL ↔ API sözleşmesi (`GET /api/ui/v1/takvim`): Blazor `/takvim` süzgeçleriyle aynı adlar
 * (`ay`, `plaka`, `grup`, `sube`). Sayfalama yok (sunucu 200 araçla keser, `aracToplam` bildirir);
 * `sayfa`/`boyut` API'ye gitmez (`takvimParametreleri`).
 */
export const CALENDAR = listDefinition({
  filtreler: {
    ay: { tur: 'metin', enFazla: 7 },
    plaka: { tur: 'metin', enFazla: 64 },
    grup: { tur: 'metin', enFazla: 64 },
    sube: { tur: 'metin', enFazla: 128 },
  },
});

const MONTH = /^\d{4}-(0[1-9]|1[0-2])$/;

/** `yyyy-MM` biçiminde mi (elle yazılmış bozuk URL sunucuya 400 ürettirmesin — bugünün ayına düşer). */
export function isMonthValid(month: string | undefined): month is string {
  return month !== undefined && MONTH.test(month);
}

/** Liste parametrelerinden yalnız süzgeçler; bozuk `ay` gönderilmez (sunucu varsayılanı: İstanbul'da bu ay). */
export function calendarParameters(p: QueryParameters): QueryParameters {
  const result: Record<string, QueryParameters[string]> = {};
  for (const [name, value] of Object.entries(p)) {
    if (name === 'sayfa' || name === 'boyut' || name === 'sirala') continue;
    if (name === 'ay' && (typeof value !== 'string' || !isMonthValid(value))) continue;
    result[name] = value;
  }
  return result;
}

export interface TakvimGunu {
  /** Ayın günü (1 tabanlı) = sütun başlığı. */
  readonly no: number;
  /** `yyyy-MM-dd` (İstanbul takvim günü). */
  readonly gun: string;
  readonly haftaSonu: boolean;
}

/** Ayın günleri (`ay` = `yyyy-MM`). Hafta sonu takvim gününden (saat dilimsiz, UTC bileşenleriyle). */
export function daysOfMonth(month: string, dayCount: number): readonly TakvimGunu[] {
  const [y, a] = month.split('-').map(Number);
  return Array.from({ length: dayCount }, (_, i) => {
    const no = i + 1;
    const weekday = new Date(Date.UTC(y ?? 1970, (a ?? 1) - 1, no)).getUTCDay();
    return {
      no,
      gun: `${month}-${String(no).padStart(2, '0')}`,
      haftaSonu: weekday === 0 || weekday === 6,
    };
  });
}

/** Hücre değeri → bilinen doluluk (sunucu başka bir metin yazarsa boş sayılır). */
export function doluluk(value: string | null | undefined): Occupancy | null {
  return value === 'Kira' || value === 'Rezervasyon' ? value : null;
}

/**
 * Kira formuna araçla giden bağlantının sorgusu (F4.3 sözleşmesi `?varac=`). Takvim bir PENCERE
 * seçtirmez: penceresiz `varac` kira formunda aracı ön seçer, tarihleri kullanıcı girer.
 */
export function rentQuery(vehicleId: string): Record<string, string> {
  return { varac: vehicleId };
}
