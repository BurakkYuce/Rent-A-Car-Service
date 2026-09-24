import { describe, expect, it } from 'vitest';

import { assistanceHiddenContact, assistanceRequest, emptyAssistance } from './crm-forms';
import type { Assistance } from './crm-model';

/** PR #295 L1: assistans gizli ad/telefon sözleşmesi — null = dokunma, "" = temizle. */
const row = {
  id: 'a1',
  zaman: '2026-09-03T20:30:00Z',
  rentalId: 'r1',
  sozlesmeNo: 'KS-1',
  plaka: '34XYZ99',
  adSoyad: null,
  cepTel: '05550000000',
  mesaj: 'Lastik',
  sebep: null,
  yedekLastikMi: false,
  aracHareketMi: true,
  kapandi: false,
  cozum: null,
} as unknown as Assistance;

describe('assistans gizli iletişim', () => {
  it('gizli sayılır: bağlı kira var ve yanıt değeri null', () => {
    expect(assistanceHiddenContact(row)).toEqual({ name: true, phone: false });
    expect(assistanceHiddenContact({ ...row, rentalId: null })).toEqual({
      name: false,
      phone: false,
    });
    expect(assistanceHiddenContact(null)).toEqual({ name: false, phone: false });
  });

  it('boş alan null (dokunma), temizle kutusu ""', () => {
    const base = { ...emptyAssistance(), mesaj: 'Lastik' };
    expect(assistanceRequest(base, { surum: 'v1' })).toMatchObject({ adSoyad: null, cepTel: null });
    expect(
      assistanceRequest({ ...base, clearName: true, clearPhone: true }, { surum: 'v1' }),
    ).toMatchObject({ adSoyad: '', cepTel: '', surum: 'v1' });
    expect(assistanceRequest({ ...base, adSoyad: ' Ali ' }, null)).toMatchObject({
      adSoyad: 'Ali',
    });
  });

  it('#295b: kayıtta GÖRÜNÜR değeri boşaltmak "" (temizle); gizli (null) alan boşsa null (dokunma)', () => {
    const base = { ...emptyAssistance(), mesaj: 'Lastik' };
    // row: ad gizli (null), telefon görünür → kullanıcı ikisini de boş bıraktı.
    expect(assistanceRequest(base, { surum: 'v1', row })).toMatchObject({
      adSoyad: null,
      cepTel: '',
    });
    expect(assistanceRequest({ ...base, cepTel: '0555 111' }, { surum: 'v1', row })).toMatchObject({
      cepTel: '0555 111',
    });
  });
});
