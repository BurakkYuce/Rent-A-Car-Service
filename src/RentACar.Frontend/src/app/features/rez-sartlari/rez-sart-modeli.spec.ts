import {
  RESERVATION_TERM_LIST,
  type ReservationTerm,
  pendingParams,
  valuesFromRecord,
  reservationTermBody,
  newValues,
} from './rez-sart-modeli';
import { validityText } from './reservation-term-columns';
import { apiParams, parseQuery } from '@core/veri/liste-sorgusu';

const CUSTOMER = 'c0000000-0000-4000-8000-000000000001';
const RES = 'r0000000-0000-4000-8000-000000000001';

/** Elle kurulmuş kayıt: tarihler sunucunun UTC anları (İstanbul gece yarısı = önceki gün 21:00Z). */
const RECORD: ReservationTerm = {
  id: 's1',
  musteriId: CUSTOMER,
  musteriAd: 'Ayşe Yılmaz',
  sart: 'Bebek koltuğu',
  grup: 'Ekipman',
  basTar: '2026-09-30T21:00:00Z',
  bitTar: null,
  talepTarihi: '2026-09-20T07:15:00Z',
  karsilamaTarihi: null,
  karsilandi: false,
  teslimEden: null,
  reservationId: RES,
  quotationId: null,
  surum: 'surum-1',
};

describe('rez şartı modeli', () => {
  it('yeni kayıt: gün → İstanbul gece yarısı (UTC), karşılama tarihi gönderilmez, surum yok', () => {
    const v = {
      ...newValues('2026-09-23'),
      musteri: { id: CUSTOMER, etiket: 'Ayşe' },
      sart: '  Koltuk  ',
    };
    expect(reservationTermBody(v, null)).toEqual({
      musteriId: CUSTOMER,
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
    const v = valuesFromRecord(RECORD);
    expect(v.basTar).toBe('2026-10-01');
    expect(v.talepTarihi).toBe('2026-09-20');
    const body = reservationTermBody({ ...v, bitTar: '2026-10-05', teslimEden: 'Ali' }, RECORD);
    expect(body).toMatchObject({
      basTar: '2026-09-30T21:00:00Z',
      talepTarihi: '2026-09-20T07:15:00Z',
      bitTar: '2026-10-04T21:00:00.000Z',
      teslimEden: 'Ali',
      reservationId: RES,
      quotationId: null,
      surum: 'surum-1',
    });
  });

  it('"bekleyen" sayacı: aynı süzgeç + durum=bekleyen, tek kayıt; karşılanan süzgecinde istek yok', () => {
    const p = (k: Record<string, string>) =>
      apiParams(RESERVATION_TERM_LIST, parseQuery(RESERVATION_TERM_LIST, k));
    expect(pendingParams(p({ musteriId: CUSTOMER, sayfa: '3', sirala: '-talepTarihi' }))).toEqual({
      musteriId: CUSTOMER,
      durum: 'bekleyen',
      sayfa: 1,
      boyut: 1,
    });
    expect(pendingParams(p({ durum: 'karsilanan' }))).toBeNull();
  });

  it('geçerlilik metni: iki uç, tek uç, yok (İstanbul günü)', () => {
    expect(validityText({ basTar: '2026-09-30T21:00:00Z', bitTar: '2026-10-04T21:00:00Z' })).toBe(
      '01.10.2026 – 05.10.2026',
    );
    expect(validityText({ basTar: '2026-09-30T21:00:00Z', bitTar: null })).toBe('01.10.2026 –');
    expect(validityText({ basTar: null, bitTar: '2026-10-04T21:00:00Z' })).toBe('– 05.10.2026');
    expect(validityText({ basTar: null, bitTar: null })).toBeNull();
  });
});
