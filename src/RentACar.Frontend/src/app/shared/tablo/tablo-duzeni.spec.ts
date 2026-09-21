import {
  duzeniBirlestir,
  genislikAyarla,
  gorunurlukAyarla,
  sutunuKaydir,
  sutunuYerlestir,
  tanstackDurumu,
  varsayilanDuzen,
} from './tablo-duzeni';
import type { TabloDuzeni, TabloSutunu } from './tablo-modeli';

interface Satir {
  plaka: string;
  marka: string;
  tutar: number;
  km: number;
}

const SUTUNLAR: readonly TabloSutunu<Satir>[] = [
  { kod: 'marka', baslik: 'Marka', deger: (s) => s.marka, sirala: true },
  { kod: 'plaka', baslik: 'Plaka', deger: (s) => s.plaka, sabit: true, genislik: 110 },
  { kod: 'tutar', baslik: 'Tutar', deger: (s) => s.tutar, tur: 'para', sirala: 'toplamTutar' },
  { kod: 'km', baslik: 'Km', deger: (s) => s.km, gizli: true, enAzGenislik: 60 },
];

const kodlar = (d: TabloDuzeni) => d.sutunlar.map((s) => s.kod);

describe('Tablo düzeni (saf)', () => {
  it('varsayılan: sabit sütun en solda, gizli kapalı, genişlik tanımdan (null)', () => {
    const d = varsayilanDuzen(SUTUNLAR);
    expect(kodlar(d)).toEqual(['plaka', 'marka', 'tutar', 'km']);
    expect(d.sutunlar.map((s) => s.gorunur)).toEqual([true, true, true, false]);
    expect(d.sutunlar.every((s) => s.genislik === null)).toBe(true);
    expect(d.siralama).toEqual([]);
  });

  it('kayıtlı düzen: sıra/görünürlük/genişlik korunur, bilinmeyen ve yinelenen kod atılır', () => {
    const kayitli: TabloDuzeni = {
      sutunlar: [
        { kod: 'km', gorunur: true, genislik: 90 },
        { kod: 'silinmis', gorunur: true, genislik: 100 },
        { kod: 'tutar', gorunur: false, genislik: 5000 },
        { kod: 'km', gorunur: false, genislik: 10 },
        { kod: 'marka', gorunur: true, genislik: 3 },
      ],
      siralama: [{ kod: 'tutar', azalan: true }],
    };
    const d = duzeniBirlestir(SUTUNLAR, kayitli);
    expect(kodlar(d)).toEqual(['plaka', 'km', 'tutar', 'marka']);
    expect(d.sutunlar).toEqual([
      { kod: 'plaka', gorunur: true, genislik: null },
      { kod: 'km', gorunur: true, genislik: 90 },
      { kod: 'tutar', gorunur: false, genislik: 2000 }, // sunucu üst sınırı
      { kod: 'marka', gorunur: true, genislik: 48 }, // varsayılan alt sınır
    ]);
    expect(d.siralama).toEqual([{ kod: 'tutar', azalan: true }]);
  });

  it('kayıtta olmayan yeni sütun tanımdaki önceli ardına, tanımdaki görünürlükle girer', () => {
    const kayitli: TabloDuzeni = {
      sutunlar: [
        { kod: 'tutar', gorunur: true, genislik: null },
        { kod: 'marka', gorunur: true, genislik: null },
      ],
      siralama: [],
    };
    // km'nin tanımdaki önceli "tutar" → onun hemen ardına; km tanımda gizli → kapalı.
    const d = duzeniBirlestir(SUTUNLAR, kayitli);
    expect(kodlar(d)).toEqual(['plaka', 'tutar', 'km', 'marka']);
    expect(d.sutunlar.find((s) => s.kod === 'km')?.gorunur).toBe(false);
  });

  it('sabit sütun kayıtta gizli/başka yerde olsa da en solda ve görünür kalır', () => {
    const kayitli: TabloDuzeni = {
      sutunlar: [
        { kod: 'marka', gorunur: true, genislik: null },
        { kod: 'plaka', gorunur: false, genislik: 150 },
      ],
      siralama: [],
    };
    const d = duzeniBirlestir(SUTUNLAR, kayitli);
    expect(d.sutunlar[0]).toEqual({ kod: 'plaka', gorunur: true, genislik: 150 });
  });

  it('bozuk kayıt (null, sutunlar dizi değil) varsayılana düşer; sıralanamaz sütunun sıralaması atılır', () => {
    expect(duzeniBirlestir(SUTUNLAR, null)).toEqual(varsayilanDuzen(SUTUNLAR));
    const bozuk = { sutunlar: 'x', siralama: [] } as unknown as TabloDuzeni;
    expect(duzeniBirlestir(SUTUNLAR, bozuk)).toEqual(varsayilanDuzen(SUTUNLAR));
    const d = duzeniBirlestir(SUTUNLAR, {
      sutunlar: [],
      siralama: [
        { kod: 'plaka', azalan: false }, // sıralanamaz
        { kod: 'marka', azalan: false },
      ],
    });
    expect(d.siralama).toEqual([{ kod: 'marka', azalan: false }]);
  });

  it('gizleme: sabit sütun gizlenemez; son görünür sütun gizlenemez', () => {
    const d = varsayilanDuzen(SUTUNLAR);
    expect(gorunurlukAyarla(SUTUNLAR, d, 'plaka', false)).toBe(d);
    const markaGizli = gorunurlukAyarla(SUTUNLAR, d, 'marka', false);
    expect(markaGizli.sutunlar.find((s) => s.kod === 'marka')?.gorunur).toBe(false);

    const tekSutun: readonly TabloSutunu<Satir>[] = [
      { kod: 'a', baslik: 'A', deger: (s) => s.plaka },
    ];
    const t = varsayilanDuzen(tekSutun);
    expect(gorunurlukAyarla(tekSutun, t, 'a', false)).toBe(t);
  });

  it('genişlik sütunun alt sınırıyla kırpılır, yuvarlanır', () => {
    const d = varsayilanDuzen(SUTUNLAR);
    expect(
      genislikAyarla(SUTUNLAR, d, 'km', 20).sutunlar.find((s) => s.kod === 'km')?.genislik,
    ).toBe(60);
    expect(
      genislikAyarla(SUTUNLAR, d, 'marka', 151.6).sutunlar.find((s) => s.kod === 'marka')?.genislik,
    ).toBe(152);
  });

  it('kaydırma sabitin önüne geçmez; sürükle-bırak önüne/ardına yerleştirir', () => {
    const d = varsayilanDuzen(SUTUNLAR); // plaka, marka, tutar, km
    expect(sutunuKaydir(SUTUNLAR, d, 'marka', -1)).toBe(d);
    expect(kodlar(sutunuKaydir(SUTUNLAR, d, 'marka', 1))).toEqual([
      'plaka',
      'tutar',
      'marka',
      'km',
    ]);
    expect(sutunuKaydir(SUTUNLAR, d, 'km', 1)).toBe(d);
    expect(kodlar(sutunuYerlestir(SUTUNLAR, d, 'km', 'marka', 'once'))).toEqual([
      'plaka',
      'km',
      'marka',
      'tutar',
    ]);
    expect(kodlar(sutunuYerlestir(SUTUNLAR, d, 'marka', 'tutar', 'sonra'))).toEqual([
      'plaka',
      'tutar',
      'marka',
      'km',
    ]);
    expect(sutunuYerlestir(SUTUNLAR, d, 'marka', 'plaka', 'once')).toBe(d);
  });

  it('TanStack durumu: seçim sütunu en solda sabit, genişlik varsayılan/kayıtlı', () => {
    const d = genislikAyarla(SUTUNLAR, varsayilanDuzen(SUTUNLAR), 'tutar', 200);
    const t = tanstackDurumu(SUTUNLAR, d, true);
    expect(t.columnOrder).toEqual(['__secim', 'plaka', 'marka', 'tutar', 'km']);
    expect(t.columnPinning).toEqual({ left: ['__secim', 'plaka'], right: [] });
    expect(t.columnVisibility).toEqual({ plaka: true, marka: true, tutar: true, km: false });
    expect(t.columnSizing).toEqual({ plaka: 110, marka: 140, tutar: 200, km: 140, __secim: 36 });
  });
});
