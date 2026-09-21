import { invariantOndalik, ondalikBicimle, ondalikCoz, ondalikDuzenlemeMetni } from './ondalik';

/**
 * Bağımsız oracle: beklenen değerler elle yazıldı (Türk muhasebe yazımı + backend `decimal`),
 * ayrıştırıcıdan türetilmedi. Değer invariant METİN — kayan nokta karşılaştırması yok.
 */
const PARA = { kesir: 2 } as const;
const PARA_NEGATIF = { kesir: 2, negatif: true } as const;

function deger(metin: string, secenek: Parameters<typeof ondalikCoz>[1] = PARA): string | null {
  const c = ondalikCoz(metin, secenek);
  if (!c.gecerli) throw new Error(`geçersiz: ${metin}`);
  return c.deger;
}

describe('ondalikCoz — para (kullanıcı yazımı → invariant metin)', () => {
  it.each<[string, string]>([
    ['1.234,56', '1234.56'],
    ['1234,56', '1234.56'],
    ['1234,5', '1234.50'],
    ['1234', '1234.00'],
    ['1.234', '1234.00'], // Türkçe binlik
    ['1.234.567,89', '1234567.89'],
    ['0,5', '0.50'],
    [',5', '0.50'],
    ['1234.56', '1234.56'], // Excel/İngilizce yapıştırma (tek nokta, 3 olmayan kesir)
    ['12.5', '12.50'],
    ['  1 234,56 ₺ ', '1234.56'], // boşluk ve simge atılır
    ['007', '7.00'],
    // Yuvarlama: yarım kuruş sıfırdan uzağa, rakam dizisi üstünde.
    ['0,005', '0.01'],
    ['0,004', '0.00'],
    ['0,0049', '0.00'],
    ['1,005', '1.01'], // ikili gösterimde 1,00499… olurdu
    ['2,675', '2.68'],
    ['0.005', '0.01'], // baştaki grup 0 → binlik değil, ondalık
    ['9,995', '10.00'], // taşma tam kısma geçer
    ['999,999', '1000.00'],
  ])('%s → %s', (metin, beklenen) => {
    expect(deger(metin)).toBe(beklenen);
  });

  it('negatif yalnız izin verilince; yuvarlama sıfırdan uzağa, sıfır işaretsiz', () => {
    expect(ondalikCoz('-1.234,56', PARA).gecerli).toBe(false);
    expect(deger('-1.234,56', PARA_NEGATIF)).toBe('-1234.56');
    expect(deger('−5', PARA_NEGATIF)).toBe('-5.00'); // U+2212
    expect(deger('-0,005', PARA_NEGATIF)).toBe('-0.01');
    expect(deger('-0,004', PARA_NEGATIF)).toBe('0.00'); // "-0.00" değil
    expect(deger('-0', PARA)).toBe('0.00'); // sıfır negatif sayılmaz
  });

  it('boş → null (sıfır değil)', () => {
    expect(deger('')).toBeNull();
    expect(deger('   ')).toBeNull();
  });

  it.each(['abc', '12,3a', '1,2,3', '1.23,4', '12.34.5', '-', ',', '1.2345.678', '1e5', '--1'])(
    'biçimsiz: %s',
    (metin) => {
      expect(ondalikCoz(metin, PARA_NEGATIF).gecerli).toBe(false);
    },
  );

  it('tam kısım en çok 15 hane (numeric(19,4))', () => {
    expect(deger('999999999999999')).toBe('999999999999999.00');
    expect(ondalikCoz('1000000000000000', PARA).gecerli).toBe(false);
  });

  it('adet: fazla kesir yuvarlanmaz, reddedilir', () => {
    const adet = { kesir: 0, fazlaHane: 'reddet' } as const;
    expect(deger('1.234', adet)).toBe('1234');
    expect(deger('12,0', adet)).toBe('12');
    expect(ondalikCoz('12,5', adet).gecerli).toBe(false);
  });
});

describe('ondalikBicimle / ondalikDuzenlemeMetni', () => {
  it.each<[string, string]>([
    ['1234.56', '1.234,56'],
    ['1234.5', '1.234,50'],
    ['0.01', '0,01'],
    ['-1234567.8', '-1.234.567,80'],
    ['100', '100,00'],
  ])('%s → %s', (girdi, beklenen) => {
    expect(ondalikBicimle(girdi, 2)).toBe(beklenen);
  });

  it('düzenleme metni gruplamasız', () => {
    expect(ondalikDuzenlemeMetni('1234567.89', 2)).toBe('1234567,89');
  });

  it('değer yoksa boş', () => {
    expect(ondalikBicimle(null, 2)).toBe('');
    expect(ondalikBicimle('bozuk', 2)).toBe('');
  });
});

describe('invariantOndalik (sunucu/kod değeri, kullanıcı yazımı DEĞİL)', () => {
  it('JSON sayısı ve invariant metin kanonik metne', () => {
    expect(invariantOndalik(1234.56, PARA)).toBe('1234.56');
    expect(invariantOndalik(1234.5, PARA)).toBe('1234.50');
    expect(invariantOndalik('1234.5678', PARA)).toBe('1234.57'); // numeric(19,4) → 2 hane
    expect(invariantOndalik(-0.005, PARA)).toBe('-0.01');
    expect(invariantOndalik(0.1 + 0.2, PARA)).toBe('0.30');
  });

  it('"1.234" burada bir virgül iki yüz otuz dört binde birdir, 1234 değil', () => {
    expect(invariantOndalik('1.234', PARA)).toBe('1.23');
  });

  it('biçimsiz → null', () => {
    expect(invariantOndalik('1.234,56', PARA)).toBeNull();
    expect(invariantOndalik(Number.NaN, PARA)).toBeNull();
    expect(invariantOndalik(null, PARA)).toBeNull();
    expect(invariantOndalik('', PARA)).toBeNull();
  });

  it('gidiş-dönüş: biçimle → ayrıştır aynı değeri verir (iki kez kaydet → kayma yok)', () => {
    for (const d of ['1234.56', '0.01', '1000000.00', '-15.50']) {
      const bir = deger(ondalikBicimle(d, 2), PARA_NEGATIF);
      const iki = deger(ondalikBicimle(bir, 2), PARA_NEGATIF);
      expect(bir).toBe(d);
      expect(iki).toBe(d);
    }
  });
});
