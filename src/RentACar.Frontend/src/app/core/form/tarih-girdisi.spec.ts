import {
  anBirlestir,
  anParcala,
  aralikCoz,
  ayIzgarasi,
  ayBasligi,
  bugun,
  gunBicimle,
  gunCoz,
  gunEkle,
  gunNormalize,
  haftaBasi,
  haftaGunuAdlari,
  hazirAraliklar,
  saatCoz,
} from './tarih-girdisi';

/** Beklenen değerler elle (takvim + İstanbul UTC+3); tarih kodundan türetilmedi. */
describe('takvim günü', () => {
  it.each<[string, string]>([
    ['22.09.2026', '2026-09-22'],
    ['2.9.2026', '2026-09-02'],
    ['22/09/2026', '2026-09-22'],
    ['22092026', '2026-09-22'],
    ['29.02.2024', '2024-02-29'],
  ])('%s → %s', (metin, beklenen) => {
    expect(gunCoz(metin)).toBe(beklenen);
  });

  it.each(['31.02.2026', '29.02.2025', '32.01.2026', '12.13.2026', '2026-09-22', 'abc', '1.1.26'])(
    'geçersiz: %s',
    (metin) => {
      expect(gunCoz(metin)).toBe('gecersiz');
    },
  );

  it('boş → null', () => {
    expect(gunCoz('  ')).toBeNull();
  });

  it('biçim ve saatli sunucu yazımı', () => {
    expect(gunBicimle('2026-09-22')).toBe('22.09.2026');
    expect(gunNormalize('2026-09-22T00:00:00')).toBe('2026-09-22');
    expect(gunNormalize('2026-02-30')).toBeNull();
  });

  it('gidiş-dönüş: kaydet → aç → kaydet aynı gün (saat dilimi kayması yok)', () => {
    let gun = '2026-03-29'; // Avrupa yaz saati geçişi günü — Date yerel yolu burada kayardı
    for (let i = 0; i < 3; i++) {
      const tekrar = gunCoz(gunBicimle(gun));
      expect(tekrar).toBe('2026-03-29');
      gun = tekrar as string;
    }
  });

  it('gün aritmetiği ve pazartesi başlangıç', () => {
    expect(gunEkle('2026-02-28', 1)).toBe('2026-03-01');
    expect(gunEkle('2024-02-28', 1)).toBe('2024-02-29');
    expect(gunEkle('2026-01-01', -1)).toBe('2025-12-31');
    expect(haftaBasi('2026-09-22')).toBe('2026-09-21'); // salı → pazartesi
    expect(haftaBasi('2026-09-27')).toBe('2026-09-21'); // pazar → önceki pazartesi
  });

  it('ay ızgarası: 42 hücre, pazartesi başlar, komşu ay işaretli', () => {
    const izgara = ayIzgarasi('2026-09-15');
    expect(izgara).toHaveLength(42);
    expect(izgara[0]).toEqual({ gun: '2026-08-31', ayDisi: true }); // 1 Eylül 2026 salı
    expect(izgara[1]).toEqual({ gun: '2026-09-01', ayDisi: false });
    expect(izgara[30]).toEqual({ gun: '2026-09-30', ayDisi: false });
    expect(izgara[31]?.ayDisi).toBe(true);
  });

  it('Türkçe ay ve gün adları', () => {
    expect(ayBasligi('2026-09-22')).toBe('Eylül 2026');
    expect(haftaGunuAdlari()[0]).toBe('Pt');
    expect(haftaGunuAdlari()[6]).toBe('Pa');
  });

  it('bugün İstanbul günüdür', () => {
    expect(bugun(new Date('2026-09-21T21:30:00Z'))).toBe('2026-09-22'); // 00:30 İstanbul
    expect(bugun(new Date('2026-09-21T20:59:00Z'))).toBe('2026-09-21');
  });

  it('hazır aralıklar', () => {
    const h = Object.fromEntries(hazirAraliklar('2026-09-22').map((x) => [x.kimlik, x.aralik]));
    expect(h['bugun']).toEqual({ baslangic: '2026-09-22', bitis: '2026-09-22' });
    expect(h['buHafta']).toEqual({ baslangic: '2026-09-21', bitis: '2026-09-27' });
    expect(h['gecenAy']).toEqual({ baslangic: '2026-08-01', bitis: '2026-08-31' });
    expect(h['son30']).toEqual({ baslangic: '2026-08-24', bitis: '2026-09-22' });
    expect(h['buYil']).toEqual({ baslangic: '2026-01-01', bitis: '2026-12-31' });
  });

  it('aralık metni', () => {
    expect(aralikCoz('22.09.2026 – 25.09.2026')).toEqual({
      baslangic: '2026-09-22',
      bitis: '2026-09-25',
    });
    expect(aralikCoz('22.09.2026 - 25.09.2026')).toEqual({
      baslangic: '2026-09-22',
      bitis: '2026-09-25',
    });
    expect(aralikCoz('22.09.2026')).toBe('gecersiz');
  });
});

describe('saat ve an (UTC)', () => {
  it.each<[string, string]>([
    ['14:30', '14:30'],
    ['1430', '14:30'],
    ['9', '09:00'],
    ['9:5', '09:05'],
    ['930', '09:30'],
    ['0', '00:00'],
  ])('saat %s → %s', (metin, beklenen) => {
    expect(saatCoz(metin)).toBe(beklenen);
  });

  it.each(['24:00', '12:60', 'ab', '12345'])('geçersiz saat: %s', (metin) => {
    expect(saatCoz(metin)).toBe('gecersiz');
  });

  it('İstanbul duvar saati ↔ UTC anı', () => {
    expect(anBirlestir('2026-09-22', '14:30')).toBe('2026-09-22T11:30:00.000Z');
    expect(anBirlestir('2026-09-22', '01:00')).toBe('2026-09-21T22:00:00.000Z'); // gün geriye geçer
    expect(anParcala('2026-09-22T11:30:00.000Z')).toEqual({ gun: '2026-09-22', saat: '14:30' });
    expect(anParcala('2026-09-21T22:00:00+00:00')).toEqual({ gun: '2026-09-22', saat: '01:00' });
    expect(anParcala('2026-09-22T14:30:00+03:00')).toEqual({ gun: '2026-09-22', saat: '14:30' });
    // Ofsetsiz yazım UTC sayılır (tarayıcı saat diliminden bağımsız).
    expect(anParcala('2026-09-22T11:30:00')).toEqual({ gun: '2026-09-22', saat: '14:30' });
    expect(anParcala('bozuk')).toBeNull();
  });

  it('gidiş-dönüş: sunucu anı → form → kaydet → form (iki kez) aynı an', () => {
    const sunucu = '2026-12-31T21:45:00+00:00'; // İstanbul'da 1 Ocak 00:45 — yıl sınırı
    let an = sunucu;
    for (let i = 0; i < 2; i++) {
      const p = anParcala(an);
      expect(p).toEqual({ gun: '2027-01-01', saat: '00:45' });
      an = anBirlestir(p?.gun ?? '', p?.saat ?? '');
    }
    expect(an).toBe('2026-12-31T21:45:00.000Z');
  });
});
