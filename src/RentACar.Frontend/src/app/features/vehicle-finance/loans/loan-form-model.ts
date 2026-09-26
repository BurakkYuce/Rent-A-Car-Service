import type { DayText } from '@core/form/tarih-girdisi';
import type { SecimSecenegi } from '@shared/form/arama-secim/selection-source';

import { momentValue, textValue } from '@features/planlama-ortak/form-yardimcilari';

import type { LoanRequest, LoanRow } from '../finance-model';

/** Blazor "Yeni Kredi" formunun alanları (döviz yok → TRY; kur sunucuda 1). */
export interface LoanFormValue {
  readonly bankaAdi: string | null;
  readonly cari: SecimSecenegi | null;
  readonly dosyaNo: string | null;
  readonly arac: SecimSecenegi | null;
  /** `rc-para-girdisi` değeri: invariant metin ("1500.5"). */
  readonly krediTutari: string | null;
  /** Kesir (0,20 = yıllık %20 basit faiz) — Blazor alanıyla aynı birim; yüzdeye çevrilmez (ölçek kaybı olmasın). */
  readonly faizOran: number | null;
  readonly taksitSayisi: number | null;
  readonly baslangic: DayText | null;
  readonly aciklama: string | null;
}

export function emptyLoanForm(): LoanFormValue {
  return {
    bankaAdi: null,
    cari: null,
    dosyaNo: null,
    arac: null,
    krediTutari: null,
    faizOran: 0,
    taksitSayisi: null,
    baslangic: null,
    aciklama: null,
  };
}

/** Form → `POST /arac-kredileri` gövdesi. Tutar invariant METİN olarak AYNEN gider (yuvarlanmaz). */
export function loanRequest(v: LoanFormValue): LoanRequest {
  return {
    bankaAdi: textValue(v.bankaAdi),
    cariId: v.cari?.id ?? null,
    dosyaNo: textValue(v.dosyaNo),
    vehicleId: v.arac?.id ?? null,
    krediTutari: v.krediTutari ?? '',
    faizOran: v.faizOran ?? 0,
    taksitSayisi: v.taksitSayisi ?? 0,
    baslangicTarihi: momentValue(v.baslangic, null),
    aciklama: textValue(v.aciklama),
  };
}

/**
 * Toplu iptal: bu sayfada aktif OLMADIĞI görülen seçimler düşer (Blazor: onay kutusu yalnız aktif satırda). Başka
 * sayfada seçilmiş kimlik gider — sunucu yalnız aktif kredileri iptal eder, ödenmiş taksitlere dokunmaz.
 */
export function activeSelection(
  selected: readonly string[],
  rows: readonly Pick<LoanRow, 'id' | 'durum'>[],
): string[] {
  const inactive = new Set(rows.filter((r) => r.durum !== 'Aktif').map((r) => r.id));
  return selected.filter((id) => !inactive.has(id));
}
