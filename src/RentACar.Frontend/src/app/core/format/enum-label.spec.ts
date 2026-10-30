import { enumLabel } from './enum-label';

/**
 * Kabul testi: ekranlarda ham sunucu kodu görünüyordu (dönem "Planlandi", Faturasız kiralar "Tamamlandi", gider ödeme
 * "AcikHesap", ekstre kaynak "BakiyeDuzeltme" / "TersKayit" / "CariVirman", dış hizmet "Kayitli"). Beklenen metinler
 * elle yazıldı.
 */
describe('enumLabel — tek etiket haritası', () => {
  it.each([
    ['Planlandi', 'Planlandı'],
    ['Kesildi', 'Kesildi'],
    ['Tamamlandi', 'Tamamlandı'],
    ['Iptal', 'İptal'],
    ['Kayitli', 'Kayıtlı'],
    ['AcikHesap', 'Açık hesap'],
    ['BakiyeDuzeltme', 'Bakiye düzeltme'],
    ['TersKayit', 'Ters kayıt'],
    ['CariVirman', 'Cari virman'],
    ['FaturaIade', 'İade faturası'],
    ['Odeme', 'Ödeme'],
  ])('%s → %s', (code, label) => {
    expect(enumLabel(code)).toBe(label);
  });

  it('bilinmeyen kod olduğu gibi (veri kaybolmaz); boş → boş metin', () => {
    expect(enumLabel('YeniBirKod')).toBe('YeniBirKod');
    expect(enumLabel(null)).toBe('');
    expect(enumLabel(undefined)).toBe('');
    // Nesne prototipinden ad gelmez.
    expect(enumLabel('constructor')).toBe('constructor');
    expect(enumLabel('toString')).toBe('toString');
  });
});
