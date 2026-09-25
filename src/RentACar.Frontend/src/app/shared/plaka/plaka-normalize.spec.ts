import { normalizePlate } from './plaka-normalize';

// Bağımsız oracle: beklenen değerler Yol v2 Ek A vaka listesinden ELLE yazıldı (fonksiyondan türetilmez).
describe('normalizePlate (Ek A)', () => {
  const valid: readonly [string, string][] = [
    ['34 abc 123', '34 ABC 123'],
    ['34abc123', '34 ABC 123'],
    ['34 ibc 123', '34 IBC 123'],
    ['07bfg582', '07 BFG 582'],
    ['07 CYC 35', '07 CYC 35'],
    ['34 A 12345', '34 A 12345'],
  ];

  for (const [input, display] of valid) {
    it(`"${input}" geçerli → "${display}"`, () => {
      const r = normalizePlate(input);
      expect(r.gecerli).toBe(true);
      expect(r.gosterim).toBe(display);
      expect(r.kanonik).toBe(display.replaceAll(' ', ''));
      expect(r.ham).toBe(input);
    });
  }

  const invalid = ['6 ABC 123', '82 ABC 123', '34 ÇBC 123', '34 ABC 1', 'WWW 123', ''];
  for (const input of invalid) {
    it(`"${input}" geçersiz, hata yok, gösterim ham büyük harf`, () => {
      const r = normalizePlate(input);
      expect(r.gecerli).toBe(false);
      expect(r.ham).toBe(input);
    });
  }

  it('geçersizde gösterim kırpılmış ham büyük harftir', () => {
    expect(normalizePlate('  34 çbc 123 ').gosterim).toBe('34 ÇBC 123');
    expect(normalizePlate('www 123').gosterim).toBe('WWW 123');
    expect(normalizePlate('').gosterim).toBe('');
  });

  it('noktasız ı ve TR klavyedeki İ de I olur', () => {
    expect(normalizePlate('34 ıbc 123').gosterim).toBe('34 IBC 123');
    expect(normalizePlate('34 İBC 123').gosterim).toBe('34 IBC 123');
  });

  it('tire ve çoklu boşluk kaldırılır', () => {
    expect(normalizePlate('34-ABC-123').gosterim).toBe('34 ABC 123');
    expect(normalizePlate(' 34   AB  1234 ').gosterim).toBe('34 AB 1234');
  });

  it('harf sayısına göre rakam aralığı: 1 harf 4–5, 2 harf 3–4, 3 harf 2–3', () => {
    expect(normalizePlate('34 A 123').gecerli).toBe(false);
    expect(normalizePlate('34 A 123456').gecerli).toBe(false);
    expect(normalizePlate('34 AB 12').gecerli).toBe(false);
    expect(normalizePlate('34 AB 12345').gecerli).toBe(false);
    expect(normalizePlate('34 ABC 1234').gecerli).toBe(false);
    expect(normalizePlate('34 AB 123').gecerli).toBe(true);
    expect(normalizePlate('81 ABC 12').gecerli).toBe(true);
    expect(normalizePlate('00 ABC 12').gecerli).toBe(false);
  });

  it('null/undefined hata fırlatmaz', () => {
    expect(normalizePlate(null).gecerli).toBe(false);
    expect(normalizePlate(undefined).kanonik).toBe('');
  });
});
