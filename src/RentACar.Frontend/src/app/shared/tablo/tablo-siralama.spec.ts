import type { TabloSutunu } from './tablo-modeli';
import {
  ariaSiralama,
  siralamaAlani,
  siralamaCoz,
  siralamaMetni,
  sonrakiSiralama,
} from './tablo-siralama';

interface Satir {
  plaka: string;
  tutar: number;
}

const SUTUNLAR: readonly TabloSutunu<Satir>[] = [
  { kod: 'plaka', baslik: 'Plaka', deger: (s) => s.plaka, sirala: true },
  { kod: 'tutar', baslik: 'Tutar', deger: (s) => s.tutar, sirala: 'toplamTutar' },
  { kod: 'not', baslik: 'Not', deger: () => '' },
];

describe('Sunucu sıralama eşlemesi (F3.4 `sirala` biçimi)', () => {
  it('alan: true → kod, metin → o alan, yok → sıralanamaz', () => {
    expect(siralamaAlani(SUTUNLAR[0])).toBe('plaka');
    expect(siralamaAlani(SUTUNLAR[1])).toBe('toplamTutar');
    expect(siralamaAlani(SUTUNLAR[2])).toBeNull();
  });

  it('metin → durum: "-alan" azalan, alan sütun koduna çözülür, bilinmeyen null', () => {
    expect(siralamaCoz(SUTUNLAR, 'plaka')).toEqual({ kod: 'plaka', azalan: false });
    expect(siralamaCoz(SUTUNLAR, '-toplamTutar')).toEqual({ kod: 'tutar', azalan: true });
    expect(siralamaCoz(SUTUNLAR, 'tutar')).toBeNull(); // kod değil ALAN adı beklenir
    expect(siralamaCoz(SUTUNLAR, 'not')).toBeNull();
    expect(siralamaCoz(SUTUNLAR, '-')).toBeNull();
    expect(siralamaCoz(SUTUNLAR, null)).toBeNull();
  });

  it('durum → metin (sunucuya giden)', () => {
    expect(siralamaMetni(SUTUNLAR, { kod: 'tutar', azalan: true })).toBe('-toplamTutar');
    expect(siralamaMetni(SUTUNLAR, { kod: 'plaka', azalan: false })).toBe('plaka');
    expect(siralamaMetni(SUTUNLAR, { kod: 'not', azalan: false })).toBeNull();
    expect(siralamaMetni(SUTUNLAR, null)).toBeNull();
  });

  it('başlık döngüsü: yok → artan → azalan → yok; başka sütun artan başlar', () => {
    const a = sonrakiSiralama(SUTUNLAR, null, 'tutar');
    expect(a).toEqual({ kod: 'tutar', azalan: false });
    const b = sonrakiSiralama(SUTUNLAR, a, 'tutar');
    expect(b).toEqual({ kod: 'tutar', azalan: true });
    expect(sonrakiSiralama(SUTUNLAR, b, 'tutar')).toBeNull();
    expect(sonrakiSiralama(SUTUNLAR, b, 'plaka')).toEqual({ kod: 'plaka', azalan: false });
    expect(sonrakiSiralama(SUTUNLAR, b, 'not')).toBe(b);
  });

  it('aria-sort yalnız sıralı sütunda', () => {
    expect(ariaSiralama({ kod: 'tutar', azalan: true }, 'tutar')).toBe('descending');
    expect(ariaSiralama({ kod: 'tutar', azalan: false }, 'tutar')).toBe('ascending');
    expect(ariaSiralama({ kod: 'tutar', azalan: false }, 'plaka')).toBeNull();
    expect(ariaSiralama(null, 'plaka')).toBeNull();
  });
});
