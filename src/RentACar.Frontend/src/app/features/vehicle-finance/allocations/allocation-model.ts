import type { DayText } from '@core/form/tarih-girdisi';
import type { SecimSecenegi } from '@shared/form/arama-secim/selection-source';

import { momentValue, textValue } from '@features/planlama-ortak/form-yardimcilari';

import type {
  AllocationPurpose,
  AllocationRequest,
  AllocationReturnRequest,
} from '../finance-model';

/** "ss:dd" (saat granülü; tarihten ayrı, isteğe bağlı). */
export const TIME_PATTERN = /^([01]\d|2[0-3]):[0-5]\d$/;

/** Form saati → API `TimeOnly` ("14:05" → "14:05:00"); boş → `null`. */
export function timeValue(s: string | null | undefined): string | null {
  const v = s?.trim() ?? '';
  return TIME_PATTERN.test(v) ? `${v}:00` : null;
}

/** Blazor "Yeni Tahsis" formu. */
export interface AllocationFormValue {
  readonly personel: SecimSecenegi | null;
  readonly arac: SecimSecenegi | null;
  readonly cikisTarihi: DayText | null;
  readonly cikisSaat: string | null;
  readonly cikisKm: number | null;
  readonly cikisYakit: number | null;
  readonly sube: string | null;
  readonly kullanimAmaci: AllocationPurpose | null;
  readonly onaylayan: SecimSecenegi | null;
  readonly kirayaVer: boolean | null;
  readonly aciklama: string | null;
}

export function emptyAllocation(): AllocationFormValue {
  return {
    personel: null,
    arac: null,
    cikisTarihi: null,
    cikisSaat: null,
    cikisKm: 0,
    cikisYakit: null,
    sube: null,
    kullanimAmaci: null,
    onaylayan: null,
    kirayaVer: false,
    aciklama: null,
  };
}

export function allocationRequest(v: AllocationFormValue): AllocationRequest {
  return {
    personelId: v.personel?.id ?? '',
    vehicleId: v.arac?.id ?? '',
    cikisTarihi: momentValue(v.cikisTarihi, null),
    cikisSaat: timeValue(v.cikisSaat),
    cikisKm: v.cikisKm ?? 0,
    cikisYakit: v.cikisYakit,
    sube: textValue(v.sube),
    kullanimAmaci: v.kullanimAmaci,
    onaylayan: v.onaylayan?.id ?? null,
    kirayaVer: v.kirayaVer ?? false,
    aciklama: textValue(v.aciklama),
  };
}

/** Satırın "Teslim Al" formu (tarih boş → sunucu "şimdi"; dönüş ofisi/saati BİLGİ). */
export interface AllocationReturnValue {
  readonly donusKm: number | null;
  readonly donusYakit: number | null;
  /** `rc-tarih-saat-secici` değeri: UTC anı. */
  readonly donusTarihi: string | null;
  readonly donusSube: string | null;
  readonly donusSaat: string | null;
}

export function allocationReturnRequest(v: AllocationReturnValue): AllocationReturnRequest {
  return {
    donusKm: v.donusKm ?? 0,
    donusYakit: v.donusYakit,
    donusTarihi: v.donusTarihi,
    donusSube: textValue(v.donusSube),
    donusSaat: timeValue(v.donusSaat),
  };
}
