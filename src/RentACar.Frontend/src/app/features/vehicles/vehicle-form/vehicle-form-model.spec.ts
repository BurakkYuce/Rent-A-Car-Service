import { describe, expect, it } from 'vitest';

import type { VehicleCard } from '../vehicle-model';
import { cardToForm, formToRequest, newVehicleValue } from './vehicle-form-model';

/** Elle kurulmuş kart (beklenen değerler buradan; dönüşüm kodundan türetilmez). */
const CARD = {
  id: '11111111-1111-4111-8111-111111111111',
  surum: 'surum-1',
  plaka: '34ABC123',
  marka: 'Fiat',
  grup: 'EKONOMİK',
  grupBilincliBos: false,
  durum: 'Musait',
  km: 12500,
  yakit: null,
  // 1995-03-10 İstanbul gece yarısı = 1995-03-09T21:00:00Z (o yıl +03:00 değil +02:00 → 22:00Z)
  tescilTarihi: '1995-03-09T22:00:00Z',
  alimBedeli: 850000.5,
  alisEuro: null,
  kiraMusteriId: '22222222-2222-4222-8222-222222222222',
  webRezKapat: true,
  createdAtUtc: '2026-01-01T00:00:00Z',
} as unknown as VehicleCard;

describe('araç kartı form modeli', () => {
  it('kart → form: gün İstanbul günü, tutar invariant metin, grup seçim öğesi, bayrak boolean', () => {
    const v = cardToForm(CARD);
    expect(v['tescilTarihi']).toBe('1995-03-10');
    expect(v['alimBedeli']).toBe('850000.5');
    expect(v['km']).toBe(12500);
    expect(v['grup']).toEqual({ id: 'ad:EKONOMİK', etiket: 'EKONOMİK' });
    expect(v['webRezKapat']).toBe(true);
    expect(v['temizlik']).toBe(false);
    expect(v['yakit']).toBeNull();
  });

  it('gidiş-dönüş: dokunulmayan tarih sunucunun ANI ile aynen, formda olmayan kiraMusteriId korunur', () => {
    const body = formToRequest(cardToForm(CARD), CARD) as Record<string, unknown>;
    expect(body['tescilTarihi']).toBe('1995-03-09T22:00:00Z');
    expect(body['kiraMusteriId']).toBe('22222222-2222-4222-8222-222222222222');
    expect(body['alimBedeli']).toBe('850000.5');
    expect(body['grup']).toBe('EKONOMİK');
    expect(body['grupBilincliBos']).toBe(false);
    expect(body['alisEuro']).toBeNull(); // kartta null → işaretsiz kutu null kalır
    expect(body['plaka']).toBe('34ABC123');
    expect(body['sasiNo']).toBeNull();
  });

  it('değişen gün İstanbul gece yarısı (UTC) olur; boş metin null; grup boş = bilinçli "(Grupsuz)"', () => {
    const v = { ...cardToForm(CARD), tescilTarihi: '2026-09-23', motorNo: '   ', grup: null };
    const body = formToRequest(v, CARD) as Record<string, unknown>;
    expect(body['tescilTarihi']).toBe('2026-09-22T21:00:00.000Z');
    expect(body['motorNo']).toBeNull();
    expect(body['grup']).toBeNull();
    expect(body['grupBilincliBos']).toBe(true);
  });

  it('yeni araç: durum Müsait, KM 0, yakıt belirtilmedi, varsayılan grup önseçili', () => {
    const v = newVehicleValue('Ekonomik');
    expect(v['durum']).toBe('Musait');
    expect(v['km']).toBe(0);
    expect(v['yakit']).toBeNull();
    expect(v['grup']).toEqual({ id: 'ad:Ekonomik', etiket: 'Ekonomik' });
    const body = formToRequest({ ...v, plaka: '06XYZ9' }, null) as Record<string, unknown>;
    expect(body['kiraMusteriId']).toBeNull();
    expect(body['grup']).toBe('Ekonomik');
    expect(body['km']).toBe(0);
  });
});
