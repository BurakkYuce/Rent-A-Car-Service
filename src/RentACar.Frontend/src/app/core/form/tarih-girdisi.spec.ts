import {
  mergeMoment,
  parseMoment,
  parseRange,
  monthGrid,
  monthTitle,
  bugun,
  formatDay,
  parseDay,
  addDays,
  normalizeDay,
  weekStart,
  weekdayNames,
  presetRanges,
  parseHour,
} from './tarih-girdisi';

/** Beklenen değerler elle (takvim + İstanbul UTC+3); tarih kodundan türetilmedi. */
describe('takvim günü', () => {
  it.each<[string, string]>([
    ['22.09.2026', '2026-09-22'],
    ['2.9.2026', '2026-09-02'],
    ['22/09/2026', '2026-09-22'],
    ['22092026', '2026-09-22'],
    ['29.02.2024', '2024-02-29'],
  ])('%s → %s', (text, expected) => {
    expect(parseDay(text)).toBe(expected);
  });

  it.each(['31.02.2026', '29.02.2025', '32.01.2026', '12.13.2026', '2026-09-22', 'abc', '1.1.26'])(
    'geçersiz: %s',
    (text) => {
      expect(parseDay(text)).toBe('gecersiz');
    },
  );

  it('boş → null', () => {
    expect(parseDay('  ')).toBeNull();
  });

  it('biçim ve saatli sunucu yazımı', () => {
    expect(formatDay('2026-09-22')).toBe('22.09.2026');
    expect(normalizeDay('2026-09-22T00:00:00')).toBe('2026-09-22');
    expect(normalizeDay('2026-02-30')).toBeNull();
  });

  it('gidiş-dönüş: kaydet → aç → kaydet aynı gün (saat dilimi kayması yok)', () => {
    let day = '2026-03-29'; // Avrupa yaz saati geçişi günü — Date yerel yolu burada kayardı
    for (let i = 0; i < 3; i++) {
      const repeat = parseDay(formatDay(day));
      expect(repeat).toBe('2026-03-29');
      day = repeat as string;
    }
  });

  it('gün aritmetiği ve pazartesi başlangıç', () => {
    expect(addDays('2026-02-28', 1)).toBe('2026-03-01');
    expect(addDays('2024-02-28', 1)).toBe('2024-02-29');
    expect(addDays('2026-01-01', -1)).toBe('2025-12-31');
    expect(weekStart('2026-09-22')).toBe('2026-09-21'); // salı → pazartesi
    expect(weekStart('2026-09-27')).toBe('2026-09-21'); // pazar → önceki pazartesi
  });

  it('ay ızgarası: 42 hücre, pazartesi başlar, komşu ay işaretli', () => {
    const grid = monthGrid('2026-09-15');
    expect(grid).toHaveLength(42);
    expect(grid[0]).toEqual({ gun: '2026-08-31', ayDisi: true }); // 1 Eylül 2026 salı
    expect(grid[1]).toEqual({ gun: '2026-09-01', ayDisi: false });
    expect(grid[30]).toEqual({ gun: '2026-09-30', ayDisi: false });
    expect(grid[31]?.ayDisi).toBe(true);
  });

  it('Türkçe ay ve gün adları', () => {
    expect(monthTitle('2026-09-22')).toBe('Eylül 2026');
    expect(weekdayNames()[0]).toBe('Pt');
    expect(weekdayNames()[6]).toBe('Pa');
  });

  it('bugün İstanbul günüdür', () => {
    expect(bugun(new Date('2026-09-21T21:30:00Z'))).toBe('2026-09-22'); // 00:30 İstanbul
    expect(bugun(new Date('2026-09-21T20:59:00Z'))).toBe('2026-09-21');
  });

  it('hazır aralıklar', () => {
    const h = Object.fromEntries(presetRanges('2026-09-22').map((x) => [x.kimlik, x.aralik]));
    expect(h['bugun']).toEqual({ baslangic: '2026-09-22', bitis: '2026-09-22' });
    expect(h['buHafta']).toEqual({ baslangic: '2026-09-21', bitis: '2026-09-27' });
    expect(h['gecenAy']).toEqual({ baslangic: '2026-08-01', bitis: '2026-08-31' });
    expect(h['son30']).toEqual({ baslangic: '2026-08-24', bitis: '2026-09-22' });
    expect(h['buYil']).toEqual({ baslangic: '2026-01-01', bitis: '2026-12-31' });
  });

  it('aralık metni', () => {
    expect(parseRange('22.09.2026 – 25.09.2026')).toEqual({
      baslangic: '2026-09-22',
      bitis: '2026-09-25',
    });
    expect(parseRange('22.09.2026 - 25.09.2026')).toEqual({
      baslangic: '2026-09-22',
      bitis: '2026-09-25',
    });
    expect(parseRange('22.09.2026')).toBe('gecersiz');
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
  ])('saat %s → %s', (text, expected) => {
    expect(parseHour(text)).toBe(expected);
  });

  it.each(['24:00', '12:60', 'ab', '12345'])('geçersiz saat: %s', (text) => {
    expect(parseHour(text)).toBe('gecersiz');
  });

  it('İstanbul duvar saati ↔ UTC anı', () => {
    expect(mergeMoment('2026-09-22', '14:30')).toBe('2026-09-22T11:30:00.000Z');
    expect(mergeMoment('2026-09-22', '01:00')).toBe('2026-09-21T22:00:00.000Z'); // gün geriye geçer
    expect(parseMoment('2026-09-22T11:30:00.000Z')).toEqual({ gun: '2026-09-22', saat: '14:30' });
    expect(parseMoment('2026-09-21T22:00:00+00:00')).toEqual({ gun: '2026-09-22', saat: '01:00' });
    expect(parseMoment('2026-09-22T14:30:00+03:00')).toEqual({ gun: '2026-09-22', saat: '14:30' });
    // Ofsetsiz yazım UTC sayılır (tarayıcı saat diliminden bağımsız).
    expect(parseMoment('2026-09-22T11:30:00')).toEqual({ gun: '2026-09-22', saat: '14:30' });
    expect(parseMoment('bozuk')).toBeNull();
  });

  it('gidiş-dönüş: sunucu anı → form → kaydet → form (iki kez) aynı an', () => {
    const server = '2026-12-31T21:45:00+00:00'; // İstanbul'da 1 Ocak 00:45 — yıl sınırı
    let an = server;
    for (let i = 0; i < 2; i++) {
      const p = parseMoment(an);
      expect(p).toEqual({ gun: '2027-01-01', saat: '00:45' });
      an = mergeMoment(p?.gun ?? '', p?.saat ?? '');
    }
    expect(an).toBe('2026-12-31T21:45:00.000Z');
  });
});
