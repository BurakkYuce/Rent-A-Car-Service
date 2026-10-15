import { describe, expect, it } from 'vitest';

import { acceptedFile, type HgsField, mappingJson, reflectable } from './hgs-model';

const fields: readonly HgsField[] = [
  { kod: 'plaka', etiket: 'Plaka', zorunlu: true, baslik: 'Plaka' },
  { kod: 'gecisZamani', etiket: 'Geçiş (çıkış) zamanı', zorunlu: true, baslik: 'Geçiş Tarihi' },
  { kod: 'tutar', etiket: 'Tutar', zorunlu: true, baslik: null },
];

describe('HGS içe aktarım modeli', () => {
  it('yalnız .xlsx ve .csv kabul eder (büyük harf uzantı dahil)', () => {
    expect(acceptedFile('gecisler.xlsx')).toBe(true);
    expect(acceptedFile('GECISLER.CSV')).toBe(true);
    expect(acceptedFile('eski.xls')).toBe(false);
    expect(acceptedFile('uzantisiz')).toBe(false);
  });

  it('eşleme değişmediyse null gönderir (sunucu önerisi kullanılır)', () => {
    expect(mappingJson(fields, {})).toBeNull();
    expect(
      mappingJson(fields, { plaka: 'Plaka', gecisZamani: 'Geçiş Tarihi', tutar: null }),
    ).toBeNull();
  });

  it('değişen eşlemede TÜM alanları gönderir; boş seçim null', () => {
    const json = mappingJson(fields, { tutar: 'Ücret', plaka: null });
    expect(json).not.toBeNull();
    expect(JSON.parse(json ?? '{}')).toEqual({
      plaka: null,
      gecisZamani: 'Geçiş Tarihi',
      tutar: 'Ücret',
    });
  });

  it('yalnız kiraya eşleşmiş ve yansıtılmamış geçiş yansıtılabilir', () => {
    expect(reflectable({ durum: 'Eslesti', kiraId: 'k1' })).toBe(true);
    expect(reflectable({ durum: 'Eslesti', kiraId: null })).toBe(false);
    expect(reflectable({ durum: 'KiraDisi', kiraId: null })).toBe(false);
    expect(reflectable({ durum: 'Yansitildi', kiraId: 'k1' })).toBe(false);
  });
});
