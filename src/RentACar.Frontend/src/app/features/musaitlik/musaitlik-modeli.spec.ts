import { apiParametreleri, sorguyuCoz } from '@core/veri/liste-sorgusu';

import {
  MUSAITLIK,
  type MusaitlikSatiri,
  aramaParametreleri,
  kiralaParametreleri,
} from './musaitlik-modeli';
import { provizyonMetni } from './musaitlik-sutunlari';

const p = (kaynak: Record<string, string>) =>
  apiParametreleri(MUSAITLIK, sorguyuCoz(MUSAITLIK, kaynak));

describe('müsaitlik modeli', () => {
  it('başlangıç günü yoksa arama YOK (Blazor ilk açılış)', () => {
    expect(aramaParametreleri(p({}))).toBeNull();
    expect(aramaParametreleri(p({ bitGun: '2026-10-04', grup: 'C' }))).toBeNull();
  });

  it('arama parametreleri API adlarıyla; sayfa/boyut gitmez; bozuk saat düşer, geçerli saat kalır', () => {
    expect(
      aramaParametreleri(
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
    expect(aramaParametreleri(p({ basGun: '2026-10-01', gun: '400' }))).toEqual({
      basGun: '2026-10-01',
    });
  });

  it('Kirala bağlantısı sunucunun ÇÖZÜLMÜŞ penceresini taşır; grup yalnız seçildiyse', () => {
    expect(
      kiralaParametreleri('a1', { vfrom: '2026-10-01', vto: '2026-10-04', vgrup: 'C' }),
    ).toEqual({
      varac: 'a1',
      vfrom: '2026-10-01',
      vto: '2026-10-04',
      vgrup: 'C',
    });
    expect(
      kiralaParametreleri('a1', { vfrom: '2026-10-01', vto: '2026-10-04', vgrup: null }),
    ).toEqual({
      varac: 'a1',
      vfrom: '2026-10-01',
      vto: '2026-10-04',
    });
  });

  it('provizyon: tr biçimi + grubun kendi dövizi; döviz yoksa yalnız tutar; yoksa boş', () => {
    const satir = (provizyon: number | null, provizyonDoviz: string | null) =>
      ({ provizyon, provizyonDoviz }) as unknown as MusaitlikSatiri;
    expect(provizyonMetni(satir(1500, 'EUR'))).toBe('1.500,00 EUR');
    expect(provizyonMetni(satir(2500.5, null))).toBe('2.500,50');
    expect(provizyonMetni(satir(null, 'EUR'))).toBeNull();
  });
});
