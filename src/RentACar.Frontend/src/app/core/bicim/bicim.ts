import { formatDate, formatNumber, getCurrencySymbol } from '@angular/common';
import { ISTANBUL_OFFSET, LOCALE } from '../yerel/tr-yerel';

/**
 * Türkçe biçimler (saf). Tek kural kaynağı: pipe'lar ve bileşenler bunları çağırır.
 *
 * Para: `1.234,56 ₺` — binlik nokta, ondalık virgül, her zaman 2 kuruş hanesi, simge SONDA.
 * Yuvarlama: yarım kuruş SIFIRDAN UZAĞA, sayının ondalık yazımı üzerinden (0,005 → 0,01;
 * -0,005 → -0,01; 1,005 → 1,01; ikili kayan nokta kayması yok). Backend'in `decimal`
 * `ToString("N2")` davranışıyla aynı. Sıfıra yuvarlanan negatif "-0,00" değil "0,00" olur.
 */
export function formatMoney(amount: number | null | undefined, currency = 'TRY'): string {
  if (amount === null || amount === undefined || !Number.isFinite(amount)) return '';
  const count = formatNumber(amount, LOCALE, '1.2-2');
  return `${count} ${getCurrencySymbol(currency, 'narrow', LOCALE)}`;
}

/** Sayı: `1.234,5` (kuruşsuz alanlar için; hane sayısı Angular `digitsInfo` ile). */
export function sayiBicimle(value: number | null | undefined, digits = '1.0-2'): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return '';
  return formatNumber(value, LOCALE, digits);
}

export type DateInput = Date | string | number | null | undefined;

/** `2026-08-26` biçimli takvim günü (DateOnly): saat dilimi dönüşümüne SOKULMAZ. */
const CALENDAR_DAY = /^(\d{4})-(\d{2})-(\d{2})$/;

function safeFormat(value: DateInput, pattern: string): string {
  if (value === null || value === undefined || value === '') return '';
  try {
    return formatDate(value, pattern, LOCALE, ISTANBUL_OFFSET);
  } catch {
    return '';
  }
}

/**
 * Tarih: `dd.MM.yyyy`. Takvim günü (`2026-08-26`) olduğu gibi yazılır; anlık zaman (ISO, Date,
 * epoch ms) İstanbul gününe çevrilir — tarayıcının saat dilimi sonucu değiştirmez.
 */
export function tarihBicimle(value: DateInput): string {
  if (typeof value === 'string') {
    const day = CALENDAR_DAY.exec(value.trim());
    if (day) return `${day[3]}.${day[2]}.${day[1]}`;
  }
  return safeFormat(value, 'dd.MM.yyyy');
}

/** Tarih-saat: `dd.MM.yyyy HH:mm`, İstanbul saatiyle. */
export function formatDateTime(value: DateInput): string {
  return safeFormat(value, 'dd.MM.yyyy HH:mm');
}
