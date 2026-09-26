import { apiParams, parseQuery } from '@core/veri/liste-sorgusu';

import { MUSAITLIK, type AvailabilityRow, searchParams, rentParameters } from './musaitlik-modeli';
import { preAuthText } from './availability-columns';

const p = (source: Record<string, string>) => apiParams(MUSAITLIK, parseQuery(MUSAITLIK, source));

describe('müsaitlik modeli', () => {
  it('başlangıç günü yoksa arama YOK (Blazor ilk açılış)', () => {
    expect(searchParams(p({}))).toBeNull();
    expect(searchParams(p({ bitGun: '2026-10-04', grup: 'C' }))).toBeNull();
  });

  it('arama parametreleri API adlarıyla; sayfa/boyut gitmez; bozuk saat düşer, geçerli saat kalır', () => {
    expect(
      searchParams(
        p({
          basGun: '2026-10-01',
          gun: '3',
          basSaat: '25:00',
          bitSaat: '09:30',
          rezKaynak: 'Web',
          doviz: 'EUR',
        }),
      ),
    ).toEqual({
      basGun: '2026-10-01',
      gun: '3',
      bitSaat: '09:30',
      rezKaynak: 'Web',
      doviz: 'EUR',
    });
  });

  it('gün sayısı 1–365 dışı URL değeri yok sayılır', () => {
    expect(searchParams(p({ basGun: '2026-10-01', gun: '400' }))).toEqual({
      basGun: '2026-10-01',
    });
  });

  it('Kirala bağlantısı sunucunun ÇÖZÜLMÜŞ penceresini taşır; grup yalnız seçildiyse', () => {
    expect(rentParameters('a1', { vfrom: '2026-10-01', vto: '2026-10-04', vgrup: 'C' })).toEqual({
      varac: 'a1',
      vfrom: '2026-10-01',
      vto: '2026-10-04',
      vgrup: 'C',
    });
    expect(rentParameters('a1', { vfrom: '2026-10-01', vto: '2026-10-04', vgrup: null })).toEqual({
      varac: 'a1',
      vfrom: '2026-10-01',
      vto: '2026-10-04',
    });
  });

  it('provizyon: tr biçimi + grubun kendi dövizi; döviz yoksa yalnız tutar; yoksa boş', () => {
    const row = (preAuth: number | null, preAuthCurrency: string | null) =>
      ({ provizyon: preAuth, provizyonDoviz: preAuthCurrency }) as unknown as AvailabilityRow;
    expect(preAuthText(row(1500, 'EUR'))).toBe('1.500,00 EUR');
    expect(preAuthText(row(2500.5, null))).toBe('2.500,50');
    expect(preAuthText(row(null, 'EUR'))).toBeNull();
  });
});
