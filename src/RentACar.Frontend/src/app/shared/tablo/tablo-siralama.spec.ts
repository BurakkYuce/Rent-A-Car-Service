import type { TabloSutunu } from './tablo-modeli';
import { ariaSort, sortField, parseSort, sortText, nextSort } from './tablo-siralama';

interface Satir {
  plaka: string;
  tutar: number;
}

const COLUMNS: readonly TabloSutunu<Satir>[] = [
  { kod: 'plaka', baslik: 'Plaka', deger: (s) => s.plaka, sirala: true },
  { kod: 'tutar', baslik: 'Tutar', deger: (s) => s.tutar, sirala: 'toplamTutar' },
  { kod: 'not', baslik: 'Not', deger: () => '' },
];

describe('Sunucu sıralama eşlemesi (F3.4 `sirala` biçimi)', () => {
  it('alan: true → kod, metin → o alan, yok → sıralanamaz', () => {
    expect(sortField(COLUMNS[0])).toBe('plaka');
    expect(sortField(COLUMNS[1])).toBe('toplamTutar');
    expect(sortField(COLUMNS[2])).toBeNull();
  });

  it('metin → durum: "-alan" azalan, alan sütun koduna çözülür, bilinmeyen null', () => {
    expect(parseSort(COLUMNS, 'plaka')).toEqual({ kod: 'plaka', azalan: false });
    expect(parseSort(COLUMNS, '-toplamTutar')).toEqual({ kod: 'tutar', azalan: true });
    expect(parseSort(COLUMNS, 'tutar')).toBeNull(); // kod değil ALAN adı beklenir
    expect(parseSort(COLUMNS, 'not')).toBeNull();
    expect(parseSort(COLUMNS, '-')).toBeNull();
    expect(parseSort(COLUMNS, null)).toBeNull();
  });

  it('durum → metin (sunucuya giden)', () => {
    expect(sortText(COLUMNS, { kod: 'tutar', azalan: true })).toBe('-toplamTutar');
    expect(sortText(COLUMNS, { kod: 'plaka', azalan: false })).toBe('plaka');
    expect(sortText(COLUMNS, { kod: 'not', azalan: false })).toBeNull();
    expect(sortText(COLUMNS, null)).toBeNull();
  });

  it('başlık döngüsü: yok → artan → azalan → yok; başka sütun artan başlar', () => {
    const a = nextSort(COLUMNS, null, 'tutar');
    expect(a).toEqual({ kod: 'tutar', azalan: false });
    const b = nextSort(COLUMNS, a, 'tutar');
    expect(b).toEqual({ kod: 'tutar', azalan: true });
    expect(nextSort(COLUMNS, b, 'tutar')).toBeNull();
    expect(nextSort(COLUMNS, b, 'plaka')).toEqual({ kod: 'plaka', azalan: false });
    expect(nextSort(COLUMNS, b, 'not')).toBe(b);
  });

  it('aria-sort yalnız sıralı sütunda', () => {
    expect(ariaSort({ kod: 'tutar', azalan: true }, 'tutar')).toBe('descending');
    expect(ariaSort({ kod: 'tutar', azalan: false }, 'tutar')).toBe('ascending');
    expect(ariaSort({ kod: 'tutar', azalan: false }, 'plaka')).toBeNull();
    expect(ariaSort(null, 'plaka')).toBeNull();
  });
});
