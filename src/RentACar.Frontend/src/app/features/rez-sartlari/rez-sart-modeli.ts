import type { QueryParameters } from '@core/api/api-istemcisi';
import type { Schema } from '@core/api/ui-tipleri';
import type { DayText } from '@core/form/tarih-girdisi';
import { listDefinition } from '@core/veri/liste-sorgusu';
import type { SecimSecenegi } from '@shared/form/arama-secim/selection-source';

import { momentValue, dayValue, textValue } from '@features/planlama-ortak/form-yardimcilari';

export type ReservationTerm = Schema<'RezSartDto'>;
export type ReservationTermRequest = Schema<'RezSartIstegi'>;
export type UpdateReservationTermRequest = Schema<'RezSartGuncelleIstegi'>;

/** API `durum` değerleri (`RezSartListeFiltresi`; tanımsız değer 400). */
export const RESERVATION_TERM_STATUSES = ['bekleyen', 'karsilanan'] as const;
export type ReservationTermStatus = (typeof RESERVATION_TERM_STATUSES)[number];

/**
 * Rez şartı listesi URL ↔ API sözleşmesi (`GET /api/ui/v1/rez-sartlari`): Blazor süzgeçleri (müşteri,
 * durum, talep günü aralığı) + sunucu sayfalama/sıralama (`SiralamaHaritasi`). Varsayılan sıra sunucunun
 * (Blazor listesinin) sırası.
 */
export const RESERVATION_TERM_LIST = listDefinition({
  filtreler: {
    musteriId: { tur: 'kimlik' },
    durum: { tur: 'secim', degerler: RESERVATION_TERM_STATUSES },
    bas: { tur: 'tarih' },
    bit: { tur: 'tarih' },
  },
  siralanabilir: ['talepTarihi', 'musteri', 'grup', 'sart', 'karsilamaTarihi'],
  varsayilanSirala: null,
  varsayilanBoyut: 50,
});

/** "N bekleyen" sayacı: aynı süzgeçler + `durum=bekleyen`, tek kayıtlık sayfa (yalnız `toplam` okunur). */
export function pendingParams(p: QueryParameters): QueryParameters | null {
  if (p['durum'] === 'karsilanan') return null; // süzgeç karşılananlar: bekleyen 0
  const result: Record<string, QueryParameters[string]> = {};
  for (const [name, value] of Object.entries(p)) {
    if (name !== 'sayfa' && name !== 'boyut' && name !== 'sirala') result[name] = value;
  }
  return { ...result, durum: 'bekleyen', sayfa: 1, boyut: 1 };
}

/** Form değerleri (Blazor oluştur/düzenle formunun alanları). Tarihler İstanbul takvim günü. */
export interface RezSartFormDegeri {
  readonly musteri: SecimSecenegi | null;
  readonly sart: string | null;
  readonly grup: string | null;
  readonly basTar: DayText | null;
  readonly bitTar: DayText | null;
  readonly talepTarihi: DayText | null;
  readonly karsilamaTarihi: DayText | null;
  readonly teslimEden: string | null;
}

/** Kayıt → form değeri. */
export function valuesFromRecord(s: ReservationTerm): RezSartFormDegeri {
  return {
    musteri: { id: s.musteriId, etiket: s.musteriAd },
    sart: s.sart,
    grup: s.grup,
    basTar: dayValue(s.basTar),
    bitTar: dayValue(s.bitTar),
    talepTarihi: dayValue(s.talepTarihi),
    karsilamaTarihi: dayValue(s.karsilamaTarihi),
    teslimEden: s.teslimEden,
  };
}

/** Yeni kayıt formu: talep tarihi bugün (Blazor varsayılanı). */
export function newValues(today: DayText): RezSartFormDegeri {
  return {
    musteri: null,
    sart: null,
    grup: null,
    basTar: null,
    bitTar: null,
    talepTarihi: today,
    karsilamaTarihi: null,
    teslimEden: null,
  };
}

/**
 * Form → `POST` gövdesi (yeni) ya da `PUT` tam değiştirme gövdesi (düzenleme). PUT'ta bağ kimlikleri
 * (`reservationId`/`quotationId`) tabandan AYNEN geri gider (tam değiştirme; ekranda düzenlenmez) ve
 * `surum` zorunludur. Dokunulmayan tarih sunucunun anıyla gider (gün yuvarlaması yok). Oluşturmada
 * karşılama tarihi gönderilmez (Blazor oluştur formunda yok).
 */
export function reservationTermBody(
  v: RezSartFormDegeri,
  floor: ReservationTerm | null,
): UpdateReservationTermRequest {
  const body: ReservationTermRequest = {
    musteriId: v.musteri?.id ?? '',
    sart: textValue(v.sart),
    grup: textValue(v.grup),
    basTar: momentValue(v.basTar, floor?.basTar),
    bitTar: momentValue(v.bitTar, floor?.bitTar),
    talepTarihi: momentValue(v.talepTarihi, floor?.talepTarihi),
    karsilamaTarihi: floor === null ? null : momentValue(v.karsilamaTarihi, floor.karsilamaTarihi),
    teslimEden: textValue(v.teslimEden),
    reservationId: floor?.reservationId ?? null,
    quotationId: floor?.quotationId ?? null,
  };
  return floor === null ? body : { ...body, surum: floor.surum ?? null };
}
