import { describe, expect, it } from 'vitest';

import { ApiHatasi } from '@core/api/api-hatasi';

import {
  UNCERTAIN_NOTICE,
  classifyDuplicate,
  customNotice,
  duplicateNotice,
  errorNotice,
} from './money-notice';

/** Beklenen değerler elle yazıldı (bağımsız oracle): sınıf ve metin anahtarı senaryodan, koddan türetilmez. */
const conflict = (
  existing?: { belgeNo: string; tutar: number | string; doviz: string; ayniIcerik: boolean },
  detail = 'Bu işlem zaten kaydedilmiş (çift gönderim / mükerrer).',
): Pick<ApiHatasi, 'mevcut' | 'detay'> =>
  ({ detay: detail, ...(existing ? { mevcut: { id: 'x', ...existing } } : {}) }) as Pick<
    ApiHatasi,
    'mevcut' | 'detay'
  >;

describe('409 mukerrer sınıflandırması (işlem başına rastgele anahtar)', () => {
  it('mevcut + aynı içerik → recorded; farklı → recordedChanged; mevcut yok → recordedEarlier', () => {
    expect(
      classifyDuplicate(conflict({ belgeNo: 'TH-1', tutar: 500, doviz: 'TRY', ayniIcerik: true })),
    ).toBe('recorded');
    expect(
      classifyDuplicate(conflict({ belgeNo: 'TH-1', tutar: 500, doviz: 'TRY', ayniIcerik: false })),
    ).toBe('recordedChanged');
    expect(classifyDuplicate(conflict())).toBe('recordedEarlier');
  });

  it('aynı içerik: bilgi tonu, "İşlem zaten kaydedildi" + önceki denemenin No ve tutarı; iade/iptal çağrısı YOK', () => {
    const n = duplicateNotice(
      conflict({ belgeNo: 'GD-000010', tutar: '1500.5', doviz: 'TRY', ayniIcerik: true }),
      { tutar: '1500.50', doviz: 'TRY' },
    );
    expect(n).toEqual({
      tone: 'bilgi',
      title: 'paraIslemi.zatenKaydedildi',
      message: 'paraIslemi.oncekiKaydedildi',
      params: { no: 'GD-000010', tutar: '1.500,50 ₺' },
      detail: 'Bu işlem zaten kaydedilmiş (çift gönderim / mükerrer).',
    });
  });

  it('farklı içerik + gönderilen tutar kancası → "girdiğiniz … YAZILMADI" metni (servis kalemi net tutarı)', () => {
    const n = duplicateNotice(
      conflict({ belgeNo: 'Yağ', tutar: 200, doviz: 'TRY', ayniIcerik: false }),
      { tutar: 300, doviz: 'TRY' },
    );
    expect(n.tone).toBe('uyari');
    expect(n.message).toBe('paraIslemi.oncekiKaydedildiFarkliTutar');
    expect(n.params).toEqual({ no: 'Yağ', tutar: '200,00 ₺', girilen: '300,00 ₺' });
  });

  it('farklı içerik, tutar kancası yok → yalnız "değiştirdiğiniz içerik yazılmadı; iade/iptal" metni', () => {
    const n = duplicateNotice(
      conflict({ belgeNo: 'GD-000001', tutar: 600, doviz: 'USD', ayniIcerik: false }),
    );
    expect(n.message).toBe('paraIslemi.oncekiKaydedildiFarkli');
    expect(n.params).toEqual({ no: 'GD-000001', tutar: '600,00 $' });
  });

  it('mevcut YOK (yarış / FarkliIcerik): "daha önce kaydedildi" — "yazılmadı, tekrar girin" metni DEĞİL', () => {
    const n = duplicateNotice(
      conflict(undefined, 'Bu işlem anahtarı farklı içerikle zaten kullanılmış.'),
      { tutar: 700, doviz: 'TRY' },
    );
    expect(n).toEqual({
      tone: 'bilgi',
      title: 'paraIslemi.dahaOnceBaslik',
      message: 'paraIslemi.dahaOnceKaydedildi',
      params: {},
      detail: 'Bu işlem anahtarı farklı içerikle zaten kullanılmış.',
    });
  });

  it('boş sunucu açıklaması ikincil satır üretmez; belirsiz ve çağıran notları sabit', () => {
    expect(
      duplicateNotice(conflict({ belgeNo: '1', tutar: 1, doviz: 'TRY', ayniIcerik: true }, '   '))
        .detail,
    ).toBeNull();
    expect(UNCERTAIN_NOTICE.message).toBe('paraIslemi.sonucBilinmiyor');
    expect(customNotice('finansBelge.satis.oncekiSatisYazilmis')).toEqual({
      tone: 'uyari',
      title: null,
      message: 'finansBelge.satis.oncekiSatisYazilmis',
      params: {},
      detail: null,
    });
  });
});

describe('başlıksız (deterministik anahtarlı) işlemlerde hata → not', () => {
  it('fatura No + tutar; farklı içerikte iade/iptal notu; mevcutsuz "daha önce"; ağ "bilinmiyor"; doğrulama notsuz', () => {
    const same = new ApiHatasi({
      status: 409,
      kod: 'mukerrer',
      detay: 'zaten',
      mevcut: {
        id: 'x',
        belgeNo: 'RNT2026000000007',
        tutar: 1800.6,
        doviz: 'TRY',
        ayniIcerik: true,
      },
    });
    expect(errorNotice(same)).toMatchObject({
      tone: 'bilgi',
      message: 'paraIslemi.oncekiKaydedildi',
      params: { no: 'RNT2026000000007', tutar: '1.800,60 ₺' },
    });
    const other = new ApiHatasi({
      status: 409,
      kod: 'mukerrer',
      detay: 'başka',
      mevcut: { id: 'x', belgeNo: 'GD-1', tutar: '50', doviz: 'EUR', ayniIcerik: false },
    });
    expect(errorNotice(other)).toMatchObject({
      tone: 'uyari',
      message: 'paraIslemi.oncekiKaydedildiFarkli',
      params: { no: 'GD-1', tutar: '50,00 €' },
    });
    expect(
      errorNotice(new ApiHatasi({ status: 409, kod: 'mukerrer', detay: 'yarış' }))?.message,
    ).toBe('paraIslemi.dahaOnceKaydedildi');
    expect(errorNotice(new ApiHatasi({ status: 0, kod: 'ag', detay: 'ağ' }))).toBe(
      UNCERTAIN_NOTICE,
    );
    expect(errorNotice(new ApiHatasi({ status: 400, kod: 'dogrulama', detay: 'x' }))).toBeNull();
  });
});
