import type { FixedRate, FixedRateUpdateRequest } from '../finance-model';

/** Sabit kur formu (kod yalnız yeni kayıtta yazılır; kur `rc-sayi-girdisi` sayısı, 6 ondalık). */
export interface FixedRateFormValue {
  readonly kod: string | null;
  readonly kur: number | null;
  readonly basTar: string | null;
  readonly bitTar: string | null;
  readonly aktif: boolean | null;
}

/** Birleştirme yardımcısının beklediği düz kayıt biçimi. */
export function asRecord(v: FixedRateFormValue): Readonly<Record<string, unknown>> {
  return { kod: v.kod, kur: v.kur, basTar: v.basTar, bitTar: v.bitTar, aktif: v.aktif };
}

/** Sunucu kaydı → form değeri (birleştirme tabanı da budur). */
export function fixedRateToForm(r: FixedRate): FixedRateFormValue {
  const exchangeRate = typeof r.kur === 'number' ? r.kur : Number(r.kur);
  return {
    kod: r.kod,
    kur: Number.isFinite(exchangeRate) ? exchangeRate : null,
    basTar: r.basTar,
    bitTar: r.bitTar,
    aktif: r.aktif,
  };
}

/** Tam değiştirme gövdesi: kod DEĞİŞMEZ (gövdede yok), `surum` zorunlu (bayatsa 409 `cakisma`). */
export function fixedRateUpdateBody(
  v: FixedRateFormValue,
  version: string | null,
): FixedRateUpdateRequest {
  return {
    kur: v.kur ?? '',
    basTar: v.basTar,
    bitTar: v.bitTar,
    aktif: v.aktif === true,
    surum: version,
  };
}
