import { TEMA_ZEMINLERI, kiraciVurgusuTuret } from './tema-servisi';
import { hexNormalize, kontrastOrani, okunurTon, uzerindekiMetin } from './renk';

describe('kontrastOrani (WCAG 2.x)', () => {
  // Oracle: WCAG referans değerleri (webaim.org kontrast denetleyicisiyle aynı).
  const tablo: [string, string, number][] = [
    ['#000000', '#ffffff', 21],
    ['#ffffff', '#ffffff', 1],
    ['#767676', '#ffffff', 4.54], // AA sınırındaki en açık gri
    ['#777777', '#ffffff', 4.48], // bir ton açığı AA'yı kaçırır
    ['#595959', '#ffffff', 7.0],
    ['#949494', '#ffffff', 3.03],
    ['#ffffff', '#1d4ed8', 6.7],
  ];

  it.each(tablo)('%s / %s ≈ %s', (a, b, beklenen) => {
    expect(kontrastOrani(a, b)).toBeCloseTo(beklenen, 2);
    expect(kontrastOrani(b, a)).toBeCloseTo(beklenen, 2);
  });
});

describe('hexNormalize', () => {
  it('kısa, önek siz ve büyük harfli hex normalize edilir; geçersiz null', () => {
    expect(hexNormalize('#ABC')).toBe('#aabbcc');
    expect(hexNormalize('D97706')).toBe('#d97706');
    expect(hexNormalize(' #1d4ed8 ')).toBe('#1d4ed8');
    expect(hexNormalize('kırmızı')).toBeNull();
    expect(hexNormalize('#12345')).toBeNull();
    expect(hexNormalize(null)).toBeNull();
  });
});

describe('üstündeki metin ve okunur ton', () => {
  it('koyu dolguya beyaz, açık dolguya siyah', () => {
    expect(uzerindekiMetin('#1d4ed8')).toBe('#ffffff');
    expect(uzerindekiMetin('#0b3d91')).toBe('#ffffff');
    expect(uzerindekiMetin('#facc15')).toBe('#000000');
    // Amber: beyazla 3.19 (AA değil), siyahla 6.58.
    expect(uzerindekiMetin('#d97706')).toBe('#000000');
  });

  it('yeterli renk aynen döner, yetersiz olan eşiğe kadar koyulaşır/açılır', () => {
    expect(okunurTon('#1d4ed8', ['#ffffff'], 4.5)).toBe('#1d4ed8');
    const sari = okunurTon('#facc15', ['#ffffff', '#f5f7fa'], 4.5);
    expect(kontrastOrani(sari, '#ffffff')).toBeGreaterThanOrEqual(4.5);
    expect(kontrastOrani(sari, '#f5f7fa')).toBeGreaterThanOrEqual(4.5);
    const lacivert = okunurTon('#0b3d91', ['#151d29'], 4.5);
    expect(kontrastOrani(lacivert, '#151d29')).toBeGreaterThanOrEqual(4.5);
  });
});

describe('kiracı vurgusu kontrast tablosu (iki tema)', () => {
  // Kiracının seçebileceği uç renkler: açık/koyu/orta ton, doygun ve gri.
  const renkler = [
    '#d97706', // amber
    '#facc15', // sarı (çok açık)
    '#0b3d91', // lacivert (çok koyu)
    '#dc2626', // kırmızı
    '#16a34a', // yeşil
    '#7c3aed', // mor
    '#777777', // orta gri (beyaz/siyah arası en zor bölge)
    '#000000',
    '#ffffff',
  ];

  it.each(renkler)('%s: düğme metni ≥ 4.5, metin tonu her zeminde ≥ 4.5', (renk) => {
    const v = kiraciVurgusuTuret(renk);
    expect(v).not.toBeNull();
    if (!v) return;
    expect(
      kontrastOrani(v['--rc-kiraci-vurgu-uzeri'], v['--rc-kiraci-vurgu']),
    ).toBeGreaterThanOrEqual(4.5);
    expect(
      kontrastOrani(v['--rc-kiraci-vurgu-uzeri'], v['--rc-kiraci-vurgu-hover']),
    ).toBeGreaterThanOrEqual(4.5);
    for (const zemin of TEMA_ZEMINLERI.acik) {
      expect(kontrastOrani(v['--rc-kiraci-vurgu-metin-acik'], zemin)).toBeGreaterThanOrEqual(4.5);
    }
    for (const zemin of TEMA_ZEMINLERI.koyu) {
      expect(kontrastOrani(v['--rc-kiraci-vurgu-metin-koyu'], zemin)).toBeGreaterThanOrEqual(4.5);
    }
  });

  it('geçersiz renkte türetme yok', () => {
    expect(kiraciVurgusuTuret('mavi')).toBeNull();
    expect(kiraciVurgusuTuret('')).toBeNull();
  });
});
