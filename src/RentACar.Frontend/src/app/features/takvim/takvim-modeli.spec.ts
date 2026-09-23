import {
  TAKVIM,
  ayGecerli,
  ayinGunleri,
  doluluk,
  kiralaSorgusu,
  takvimParametreleri,
} from './takvim-modeli';
import { apiParametreleri, sorguyuCoz } from '@core/veri/liste-sorgusu';

describe('takvim modeli', () => {
  it('ayın günleri: Şubat 2026 = 28 gün; 1 Şubat pazar (hafta sonu), 2 Şubat pazartesi', () => {
    const gunler = ayinGunleri('2026-02', 28);
    expect(gunler).toHaveLength(28);
    expect(gunler[0]).toEqual({ no: 1, gun: '2026-02-01', haftaSonu: true });
    expect(gunler[1]).toEqual({ no: 2, gun: '2026-02-02', haftaSonu: false });
    // 7 Şubat 2026 cumartesi, 28 Şubat cumartesi.
    expect(gunler[6]?.haftaSonu).toBe(true);
    expect(gunler[27]).toEqual({ no: 28, gun: '2026-02-28', haftaSonu: true });
    expect(gunler.filter((g) => g.haftaSonu)).toHaveLength(8);
  });

  it('ay biçimi: yalnız yyyy-MM (01–12)', () => {
    expect(ayGecerli('2026-09')).toBe(true);
    expect(ayGecerli('2026-13')).toBe(false);
    expect(ayGecerli('2026-9')).toBe(false);
    expect(ayGecerli('bozuk')).toBe(false);
    expect(ayGecerli(undefined)).toBe(false);
  });

  it('API parametreleri: sayfa/boyut gitmez, bozuk ay düşer (sunucu bu ayı açar), süzgeçler aynı adla', () => {
    const p = apiParametreleri(
      TAKVIM,
      sorguyuCoz(TAKVIM, { ay: '2026-13', plaka: '34 AB', grup: 'C', sube: 'Merkez', sayfa: '3' }),
    );
    expect(takvimParametreleri(p)).toEqual({ plaka: '34 AB', grup: 'C', sube: 'Merkez' });
    expect(
      takvimParametreleri(apiParametreleri(TAKVIM, sorguyuCoz(TAKVIM, { ay: '2026-10' }))),
    ).toEqual({
      ay: '2026-10',
    });
  });

  it('doluluk: yalnız Kira / Rezervasyon; bilinmeyen metin boş hücre', () => {
    expect(doluluk('Kira')).toBe('Kira');
    expect(doluluk('Rezervasyon')).toBe('Rezervasyon');
    expect(doluluk('Servis')).toBeNull();
    expect(doluluk(null)).toBeNull();
  });

  it('plaka bağlantısı kira formuna penceresiz ?varac= taşır', () => {
    expect(kiralaSorgusu('a1')).toEqual({ varac: 'a1' });
  });
});
