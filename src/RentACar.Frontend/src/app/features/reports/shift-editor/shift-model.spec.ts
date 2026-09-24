import {
  TIME_PATTERN,
  emptyShift,
  shiftPath,
  shiftRequest,
  shiftToForm,
  timeText,
  type Shift,
} from './shift-model';

const SHIFT: Shift = {
  id: 's-1',
  personelId: 'p-1',
  personelAd: 'Ali Veli',
  tarih: '2026-09-21',
  baslangicSaat: '22:00',
  bitisSaat: '06:00',
  sureDk: 480,
  aralik: '22:00-06:00 (+1)',
  sube: 'Merkez',
  aciklama: null,
  surum: 'v-7',
};

describe('vardiya formu modeli', () => {
  it('yeni vardiya: Blazor varsayılanları (pencerenin ilk günü, 08:00–18:00)', () => {
    expect(emptyShift('2026-09-21', null)).toEqual({
      personel: null,
      tarih: '2026-09-21',
      baslangicSaat: '08:00',
      bitisSaat: '18:00',
      sube: null,
      aciklama: null,
    });
    expect(emptyShift(null, 'SubeA').sube).toBe('SubeA');
  });

  it('saat biçimi: 24 saat SS:dd, saniyeli sunucu değeri kırpılır', () => {
    for (const ok of ['08:00', '8:30', '23:59', '00:00']) expect(TIME_PATTERN.test(ok)).toBe(true);
    for (const bad of ['24:00', '08:60', '0800', '08:0', 'ab:cd', ''])
      expect(TIME_PATTERN.test(bad)).toBe(false);
    expect(timeText('09:00:00')).toBe('09:00');
    expect(timeText('09:00')).toBe('09:00');
    expect(timeText(null)).toBeNull();
  });

  it('kayıt → form → PUT gövdesi: sürüm tabandan, boş metin null, personel kimliği', () => {
    const form = shiftToForm(SHIFT);
    expect(form.personel).toEqual({ id: 'p-1', etiket: 'Ali Veli' });
    expect(shiftRequest({ ...form, aciklama: '  ', sube: ' Merkez ' }, SHIFT)).toEqual({
      personelId: 'p-1',
      tarih: '2026-09-21',
      baslangicSaat: '22:00',
      bitisSaat: '06:00',
      sube: 'Merkez',
      aciklama: null,
      surum: 'v-7',
    });
    expect(shiftRequest(form, null).surum).toBeNull(); // POST sürüm taşımaz
  });

  it('kayıt yolu kimliği kaçışlar', () => {
    expect(shiftPath('a/b')).toBe('/api/ui/v1/vardiyalar/a%2Fb');
  });
});
