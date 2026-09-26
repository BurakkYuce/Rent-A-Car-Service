import { formatMoney, sayiBicimle, tarihBicimle, formatDateTime } from './bicim';

/**
 * Bağımsız oracle: beklenen metinler elle yazıldı (Türk muhasebe yazımı ve backend
 * `decimal.ToString("N2")` davranışı), biçimleyici koddan türetilmedi.
 */
describe('paraBicimle', () => {
  const table: [number, string][] = [
    [1234.5, '1.234,50 ₺'],
    [1234.56, '1.234,56 ₺'],
    [-93040, '-93.040,00 ₺'],
    [0, '0,00 ₺'],
    [1234567.891, '1.234.567,89 ₺'],
    // Yuvarlama kuralı: yarım kuruş sıfırdan uzağa, ondalık yazım üzerinden.
    [0.005, '0,01 ₺'],
    [-0.005, '-0,01 ₺'],
    [1.005, '1,01 ₺'], // ikili gösterimde 1,00499…; yine de 1,01
    [2.675, '2,68 ₺'],
    [0.004, '0,00 ₺'],
    [-0.004, '0,00 ₺'], // "-0,00" değil
  ];

  it.each(table)('%s → %s', (amount, expected) => {
    expect(formatMoney(amount)).toBe(expected);
  });

  it('para birimi koduna göre simge; simgesi olmayan kodu yazar', () => {
    expect(formatMoney(100, 'USD')).toBe('100,00 $');
    expect(formatMoney(100, 'EUR')).toBe('100,00 €');
    expect(formatMoney(100, 'GBP')).toBe('100,00 £');
    expect(formatMoney(100, 'CHF')).toBe('100,00 CHF');
    expect(formatMoney(-1500.25, 'EUR')).toBe('-1.500,25 €');
  });

  it('değer yoksa ya da sayı değilse boş metin', () => {
    expect(formatMoney(null)).toBe('');
    expect(formatMoney(undefined)).toBe('');
    expect(formatMoney(Number.NaN)).toBe('');
    expect(formatMoney(Number.POSITIVE_INFINITY)).toBe('');
  });
});

describe('sayiBicimle', () => {
  it('binlik nokta, ondalık virgül, gereksiz sıfır yok', () => {
    expect(sayiBicimle(1234.5)).toBe('1.234,5');
    expect(sayiBicimle(12000)).toBe('12.000');
    expect(sayiBicimle(0.125, '1.0-2')).toBe('0,13');
    expect(sayiBicimle(null)).toBe('');
  });
});

describe('tarihBicimle / tarihSaatBicimle', () => {
  it('takvim günü saat dilimine sokulmadan dd.MM.yyyy', () => {
    expect(tarihBicimle('2026-08-26')).toBe('26.08.2026');
    expect(tarihBicimle('2024-02-29')).toBe('29.02.2024');
  });

  it('anlık zaman İstanbul (UTC+3) gününe ve saatine çevrilir', () => {
    // 26.08.2026 21:30 UTC = 27.08.2026 00:30 İstanbul: gün değişir.
    const an = new Date('2026-08-26T21:30:00Z');
    expect(tarihBicimle(an)).toBe('27.08.2026');
    expect(formatDateTime(an)).toBe('27.08.2026 00:30');
    expect(formatDateTime('2026-01-05T06:07:00Z')).toBe('05.01.2026 09:07');
    expect(formatDateTime('2026-08-26T10:00:00+03:00')).toBe('26.08.2026 10:00');
    expect(formatDateTime(Date.UTC(2026, 11, 31, 20, 59))).toBe('31.12.2026 23:59');
  });

  it('geçersiz ya da boş girdi boş metin döner, fırlatmaz', () => {
    expect(tarihBicimle('tarih değil')).toBe('');
    expect(formatDateTime('tarih değil')).toBe('');
    expect(tarihBicimle(null)).toBe('');
    expect(tarihBicimle('')).toBe('');
    expect(formatDateTime(undefined)).toBe('');
  });
});
