import { trSearchKey, trUpperCase, trLowerCase, trNormalize } from '@core/metin/tr-normalize';

describe('tr-normalize', () => {
  it('noktasız büyük I küçükte ı olur', () => {
    expect(trLowerCase('I')).toBe('ı');
  });

  it('noktalı büyük İ küçükte i olur', () => {
    expect(trLowerCase('İ')).toBe('i');
  });

  it('büyük harfe çevirmede i → İ, ı → I', () => {
    expect(trUpperCase('i')).toBe('İ');
    expect(trUpperCase('ı')).toBe('I');
  });

  it('arama anahtarı kırpılır ve Türkçe küçültülür', () => {
    expect(trNormalize('  IĞDIR İSTANBUL ')).toBe('ığdır istanbul');
  });

  it('ayrışık yazılmış İ (I + birleşen nokta) NFC ile aynı anahtara iner', () => {
    expect(trNormalize('İSTANBUL')).toBe(trNormalize('İSTANBUL'));
  });
});

describe('trAramaAnahtari', () => {
  it('"İş" ve "is" aynı anahtara iner; Türkçe harfler aksansızlaşır', () => {
    expect(trSearchKey('İş Emri')).toBe('is emri');
    expect(trSearchKey('is')).toBe('is');
    expect(trSearchKey('IŞIK')).toBe('isik');
    expect(trSearchKey('Güneş Çağrı Öğe')).toBe('gunes cagri oge');
  });
});
