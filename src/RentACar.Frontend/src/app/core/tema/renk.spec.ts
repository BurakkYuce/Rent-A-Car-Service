import { TEMA_ZEMINLERI, deriveTenantAccent } from './tema-servisi';
import { hexNormalize, contrastRatio, readableTone, textOn } from './renk';

describe('kontrastOrani (WCAG 2.x)', () => {
  // Oracle: WCAG referans değerleri (webaim.org kontrast denetleyicisiyle aynı).
  const table: [string, string, number][] = [
    ['#000000', '#ffffff', 21],
    ['#ffffff', '#ffffff', 1],
    ['#767676', '#ffffff', 4.54], // AA sınırındaki en açık gri
    ['#777777', '#ffffff', 4.48], // bir ton açığı AA'yı kaçırır
    ['#595959', '#ffffff', 7.0],
    ['#949494', '#ffffff', 3.03],
    ['#ffffff', '#1d4ed8', 6.7],
  ];

  it.each(table)('%s / %s ≈ %s', (a, b, expected) => {
    expect(contrastRatio(a, b)).toBeCloseTo(expected, 2);
    expect(contrastRatio(b, a)).toBeCloseTo(expected, 2);
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
    expect(textOn('#1d4ed8')).toBe('#ffffff');
    expect(textOn('#0b3d91')).toBe('#ffffff');
    expect(textOn('#facc15')).toBe('#000000');
    // Amber: beyazla 3.19 (AA değil), siyahla 6.58.
    expect(textOn('#d97706')).toBe('#000000');
  });

  it('yeterli renk aynen döner, yetersiz olan eşiğe kadar koyulaşır/açılır', () => {
    expect(readableTone('#1d4ed8', ['#ffffff'], 4.5)).toBe('#1d4ed8');
    const sari = readableTone('#facc15', ['#ffffff', '#f5f7fa'], 4.5);
    expect(contrastRatio(sari, '#ffffff')).toBeGreaterThanOrEqual(4.5);
    expect(contrastRatio(sari, '#f5f7fa')).toBeGreaterThanOrEqual(4.5);
    const navy = readableTone('#0b3d91', ['#151d29'], 4.5);
    expect(contrastRatio(navy, '#151d29')).toBeGreaterThanOrEqual(4.5);
  });
});

describe('kiracı vurgusu kontrast tablosu (iki tema)', () => {
  // Kiracının seçebileceği uç renkler: açık/koyu/orta ton, doygun ve gri.
  const colors = [
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

  it.each(colors)('%s: düğme metni ≥ 4.5, metin tonu her zeminde ≥ 4.5', (renk) => {
    const v = deriveTenantAccent(renk);
    expect(v).not.toBeNull();
    if (!v) return;
    expect(
      contrastRatio(v['--rc-kiraci-vurgu-uzeri'], v['--rc-kiraci-vurgu']),
    ).toBeGreaterThanOrEqual(4.5);
    expect(
      contrastRatio(v['--rc-kiraci-vurgu-uzeri'], v['--rc-kiraci-vurgu-hover']),
    ).toBeGreaterThanOrEqual(4.5);
    for (const background of TEMA_ZEMINLERI.acik) {
      expect(contrastRatio(v['--rc-kiraci-vurgu-metin-acik'], background)).toBeGreaterThanOrEqual(
        4.5,
      );
    }
    for (const background of TEMA_ZEMINLERI.koyu) {
      expect(contrastRatio(v['--rc-kiraci-vurgu-metin-koyu'], background)).toBeGreaterThanOrEqual(
        4.5,
      );
    }
  });

  it('geçersiz renkte türetme yok', () => {
    expect(deriveTenantAccent('mavi')).toBeNull();
    expect(deriveTenantAccent('')).toBeNull();
  });
});
