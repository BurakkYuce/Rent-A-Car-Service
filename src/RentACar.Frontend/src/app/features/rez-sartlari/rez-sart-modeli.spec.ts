import {
  REZ_SART_LISTESI,
  type RezSart,
  bekleyenParametreleri,
  kayittanDegerler,
  rezSartGovdesi,
  yeniDegerler,
} from './rez-sart-modeli';
import { gecerlilikMetni } from './rez-sart-sutunlari';
import { apiParametreleri, sorguyuCoz } from '@core/veri/liste-sorgusu';

const MUSTERI = 'c0000000-0000-4000-8000-000000000001';
const REZ = 'r0000000-0000-4000-8000-000000000001';

/** Elle kurulmuş kayıt: tarihler sunucunun UTC anları (İstanbul gece yarısı = önceki gün 21:00Z). */
const KAYIT: RezSart = {
  id: 's1',
  musteriId: MUSTERI,
  musteriAd: 'Ayşe Yılmaz',
  sart: 'Bebek koltuğu',
  grup: 'Ekipman',
  basTar: '2026-09-30T21:00:00Z',
  bitTar: null,
  talepTarihi: '2026-09-20T07:15:00Z',
  karsilamaTarihi: null,
  karsilandi: false,
  teslimEden: null,
  reservationId: REZ,
  quotationId: null,
  surum: 'surum-1',
};

describe('rez şartı modeli', () => {
  it('yeni kayıt: gün → İstanbul gece yarısı (UTC), karşılama tarihi gönderilmez, surum yok', () => {
    const v = {
      ...yeniDegerler('2026-09-23'),
      musteri: { id: MUSTERI, etiket: 'Ayşe' },
      sart: '  Koltuk  ',
    };
    expect(rezSartGovdesi(v, null)).toEqual({
      musteriId: MUSTERI,
      sart: 'Koltuk',
      grup: null,
      basTar: null,
      bitTar: null,
      talepTarihi: '2026-09-22T21:00:00.000Z',
      karsilamaTarihi: null,
      teslimEden: null,
      reservationId: null,
      quotationId: null,
    });
  });

  it('düzenleme (tam PUT): dokunulmayan tarih sunucunun ANI ile aynen gider, bağ kimlikleri ve surum geri döner', () => {
    const v = kayittanDegerler(KAYIT);
    expect(v.basTar).toBe('2026-10-01');
    expect(v.talepTarihi).toBe('2026-09-20');
    const govde = rezSartGovdesi({ ...v, bitTar: '2026-10-05', teslimEden: 'Ali' }, KAYIT);
    expect(govde).toMatchObject({
      basTar: '2026-09-30T21:00:00Z',
      talepTarihi: '2026-09-20T07:15:00Z',
      bitTar: '2026-10-04T21:00:00.000Z',
      teslimEden: 'Ali',
      reservationId: REZ,
      quotationId: null,
      surum: 'surum-1',
    });
  });

  it('"bekleyen" sayacı: aynı süzgeç + durum=bekleyen, tek kayıt; karşılanan süzgecinde istek yok', () => {
    const p = (k: Record<string, string>) =>
      apiParametreleri(REZ_SART_LISTESI, sorguyuCoz(REZ_SART_LISTESI, k));
    expect(
      bekleyenParametreleri(p({ musteriId: MUSTERI, sayfa: '3', sirala: '-talepTarihi' })),
    ).toEqual({
      musteriId: MUSTERI,
      durum: 'bekleyen',
      sayfa: 1,
      boyut: 1,
    });
    expect(bekleyenParametreleri(p({ durum: 'karsilanan' }))).toBeNull();
  });

  it('geçerlilik metni: iki uç, tek uç, yok (İstanbul günü)', () => {
    expect(
      gecerlilikMetni({ basTar: '2026-09-30T21:00:00Z', bitTar: '2026-10-04T21:00:00Z' }),
    ).toBe('01.10.2026 – 05.10.2026');
    expect(gecerlilikMetni({ basTar: '2026-09-30T21:00:00Z', bitTar: null })).toBe('01.10.2026 –');
    expect(gecerlilikMetni({ basTar: null, bitTar: '2026-10-04T21:00:00Z' })).toBe('– 05.10.2026');
    expect(gecerlilikMetni({ basTar: null, bitTar: null })).toBeNull();
  });
});
