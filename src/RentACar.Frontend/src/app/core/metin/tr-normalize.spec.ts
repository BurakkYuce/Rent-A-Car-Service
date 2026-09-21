import { trAramaAnahtari, trBuyukHarf, trKucukHarf, trNormalize } from '@core/metin/tr-normalize';

describe('tr-normalize', () => {
  it('noktasız büyük I küçükte ı olur', () => {
    expect(trKucukHarf('I')).toBe('ı');
  });

  it('noktalı büyük İ küçükte i olur', () => {
    expect(trKucukHarf('İ')).toBe('i');
  });

  it('büyük harfe çevirmede i → İ, ı → I', () => {
    expect(trBuyukHarf('i')).toBe('İ');
    expect(trBuyukHarf('ı')).toBe('I');
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
    expect(trAramaAnahtari('İş Emri')).toBe('is emri');
    expect(trAramaAnahtari('is')).toBe('is');
    expect(trAramaAnahtari('IŞIK')).toBe('isik');
    expect(trAramaAnahtari('Güneş Çağrı Öğe')).toBe('gunes cagri oge');
  });
});
