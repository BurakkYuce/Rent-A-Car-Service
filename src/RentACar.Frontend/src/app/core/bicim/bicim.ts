import { formatDate, formatNumber, getCurrencySymbol } from '@angular/common';
import { ISTANBUL_OFSETI, YEREL } from '../yerel/tr-yerel';

/**
 * Türkçe biçimler (saf). Tek kural kaynağı: pipe'lar ve bileşenler bunları çağırır.
 *
 * Para: `1.234,56 ₺` — binlik nokta, ondalık virgül, her zaman 2 kuruş hanesi, simge SONDA.
 * Yuvarlama: yarım kuruş SIFIRDAN UZAĞA, sayının ondalık yazımı üzerinden (0,005 → 0,01;
 * -0,005 → -0,01; 1,005 → 1,01; ikili kayan nokta kayması yok). Backend'in `decimal`
 * `ToString("N2")` davranışıyla aynı. Sıfıra yuvarlanan negatif "-0,00" değil "0,00" olur.
 */
export function paraBicimle(tutar: number | null | undefined, paraBirimi = 'TRY'): string {
  if (tutar === null || tutar === undefined || !Number.isFinite(tutar)) return '';
  const sayi = formatNumber(tutar, YEREL, '1.2-2');
  return `${sayi} ${getCurrencySymbol(paraBirimi, 'narrow', YEREL)}`;
}

/** Sayı: `1.234,5` (kuruşsuz alanlar için; hane sayısı Angular `digitsInfo` ile). */
export function sayiBicimle(deger: number | null | undefined, haneler = '1.0-2'): string {
  if (deger === null || deger === undefined || !Number.isFinite(deger)) return '';
  return formatNumber(deger, YEREL, haneler);
}

export type TarihGirdisi = Date | string | number | null | undefined;

/** `2026-08-26` biçimli takvim günü (DateOnly): saat dilimi dönüşümüne SOKULMAZ. */
const TAKVIM_GUNU = /^(\d{4})-(\d{2})-(\d{2})$/;

function guvenliBicimle(deger: TarihGirdisi, desen: string): string {
  if (deger === null || deger === undefined || deger === '') return '';
  try {
    return formatDate(deger, desen, YEREL, ISTANBUL_OFSETI);
  } catch {
    return '';
  }
}

/**
 * Tarih: `dd.MM.yyyy`. Takvim günü (`2026-08-26`) olduğu gibi yazılır; anlık zaman (ISO, Date,
 * epoch ms) İstanbul gününe çevrilir — tarayıcının saat dilimi sonucu değiştirmez.
 */
export function tarihBicimle(deger: TarihGirdisi): string {
  if (typeof deger === 'string') {
    const gun = TAKVIM_GUNU.exec(deger.trim());
    if (gun) return `${gun[3]}.${gun[2]}.${gun[1]}`;
  }
  return guvenliBicimle(deger, 'dd.MM.yyyy');
}

/** Tarih-saat: `dd.MM.yyyy HH:mm`, İstanbul saatiyle. */
export function tarihSaatBicimle(deger: TarihGirdisi): string {
  return guvenliBicimle(deger, 'dd.MM.yyyy HH:mm');
}
