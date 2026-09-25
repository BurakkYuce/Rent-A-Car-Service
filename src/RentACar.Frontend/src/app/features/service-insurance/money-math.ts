import { num } from './service-insurance-model';

/** Kuruşa yuvarlama, yarım kuruş sıfırdan uzağa (sunucu `MidpointRounding.AwayFromZero` ile aynı yön). */
export function round2(x: number): number {
  const scaled = Number((Math.abs(x) * 100).toPrecision(15));
  return (Math.sign(x) * Math.round(scaled)) / 100;
}

/**
 * Servis kaleminin NET satır tutarı — YALNIZ `mukerrer` sınıflandırması için (inceleme M3): sunucunun
 * `mevcut.tutar`'ı net satır tutarıdır. Kayıt değeri sunucuda hesaplanır; bu değer hiçbir gövdeye girmez.
 * `tutar ?? round2(birimFiyat × (miktar ?? 1)) − round2(indirim ?? 0)`.
 */
export function lineNetAmount(v: {
  readonly tutar: number | string | null;
  readonly birimFiyat: number | string | null;
  readonly miktar: number | string | null;
  readonly indirim: number | string | null;
}): number | null {
  const explicit = num(v.tutar);
  if (explicit !== null) return explicit;
  const unit = num(v.birimFiyat);
  if (unit === null) return null;
  return round2(round2(unit * (num(v.miktar) ?? 1)) - round2(num(v.indirim) ?? 0));
}
