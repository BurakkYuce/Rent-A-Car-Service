import type { Sema } from '@core/api/ui-tipleri';
import type { GunMetni } from '@core/form/tarih-girdisi';
import { metinDegeri } from '@features/planlama-ortak/form-yardimcilari';
import type { SecimSecenegi } from '@shared/form/arama-secim/secim-kaynagi';

/**
 * F10.3 vardiya yazma formu — saf kurallar (Blazor `PersonelCalismaTablosu` formu paritesi). İş kuralları
 * (çakışma, gece vardiyası, sıfır süre, şube) sunucuda; burada yalnız form ↔ gövde eşlemesi.
 */
export type Shift = Sema<'ShiftDto'>;
export type ShiftRequest = Sema<'ShiftRequest'>;
export type ShiftListRow = Sema<'ShiftRow'>;

export const SHIFTS = '/api/ui/v1/vardiyalar';

export function shiftPath(id: string): `/api/ui/v1/vardiyalar/${string}` {
  return `${SHIFTS}/${encodeURIComponent(id)}`;
}

/** 24 saat `SS:dd` (sunucu da aynı biçimi ister; tek haneli saat kabul edilir). */
export const TIME_PATTERN = /^([01]?\d|2[0-3]):[0-5]\d$/;

export interface ShiftFormValue {
  readonly personel: SecimSecenegi | null;
  readonly tarih: GunMetni | null;
  readonly baslangicSaat: string | null;
  readonly bitisSaat: string | null;
  readonly sube: string | null;
  readonly aciklama: string | null;
}

/** Yeni vardiya: Blazor varsayılanları — görüntülenen pencerenin ilk günü, 08:00–18:00. */
export function emptyShift(day: GunMetni | null, branch: string | null): ShiftFormValue {
  return {
    personel: null,
    tarih: day,
    baslangicSaat: '08:00',
    bitisSaat: '18:00',
    sube: branch,
    aciklama: null,
  };
}

/** Sunucu `"08:00"` ya da `"08:00:00"` → `"08:00"`. */
export function timeText(value: string | null | undefined): string | null {
  if (!value) return null;
  return value.length > 5 ? value.slice(0, 5) : value;
}

export function shiftToForm(s: Shift): ShiftFormValue {
  return {
    personel: { id: s.personelId, etiket: s.personelAd },
    tarih: s.tarih,
    baslangicSaat: timeText(s.baslangicSaat),
    bitisSaat: timeText(s.bitisSaat),
    sube: s.sube,
    aciklama: s.aciklama,
  };
}

/** Liste satırı → form (tekil okuma dönene kadar geçici taban). */
export function listRowToForm(r: ShiftListRow): ShiftFormValue {
  return {
    personel: { id: r.personelId, etiket: r.personelAd },
    tarih: r.tarih,
    baslangicSaat: timeText(r.baslangicSaat),
    bitisSaat: timeText(r.bitisSaat),
    sube: r.sube,
    aciklama: r.aciklama,
  };
}

/** Form → `POST` / tam değiştirme `PUT` gövdesi (PUT'ta `surum`). */
export function shiftRequest(v: ShiftFormValue, base: Shift | null): ShiftRequest {
  return {
    personelId: v.personel?.id ?? null,
    tarih: v.tarih,
    baslangicSaat: metinDegeri(v.baslangicSaat),
    bitisSaat: metinDegeri(v.bitisSaat),
    sube: metinDegeri(v.sube),
    aciklama: metinDegeri(v.aciklama),
    surum: base?.surum ?? null,
  };
}
