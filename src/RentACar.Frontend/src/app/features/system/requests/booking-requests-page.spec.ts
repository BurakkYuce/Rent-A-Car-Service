import { statusFromQuery } from './booking-requests-page';

/**
 * F11.3: gelen talepler `?durum=` süzgeci — durum adı ya da eski arayüzün sayı kodu (enum değeri; elle yazılmış
 * tablo: 0 Yeni, 1 Dönüştü, 2 Reddedildi, 3 İletişimde, 4 Teklif Verildi, 5 Kayıp). Bilinmeyen değer varsayılan liste.
 */
describe('statusFromQuery', () => {
  it.each([
    ['Yeni', 'Yeni'],
    ['Kayip', 'Kayip'],
    ['0', 'Yeni'],
    ['1', 'Donustu'],
    ['2', 'Reddedildi'],
    ['3', 'Iletisimde'],
    ['4', 'TeklifVerildi'],
    ['5', 'Kayip'],
  ])('%j → %j', (value, status) => {
    expect(statusFromQuery(value)).toBe(status);
  });

  it.each([null, '', '6', '-1', 'yeni', 'constructor', '__proto__', '0 '])(
    'tanınmaz: %j',
    (value) => {
      expect(statusFromQuery(value)).toBeNull();
    },
  );
});
