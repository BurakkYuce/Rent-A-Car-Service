import { ApiHatasi } from '@core/api/api-hatasi';

import { resolveSource } from './tablo-kaynagi';

describe('Tablo kaynağı (TemelStore durumu → çizim hâli)', () => {
  const page = { kayitlar: ['a', 'b'], toplam: 42, sayfaNo: 2, boyut: 2 };

  it('hazır Sayfa<T>: satırlar + sunucu sayfalaması; sıfır kayıt yalnız burada "kayıt yok"', () => {
    expect(resolveSource({ tur: 'hazir', veri: page })).toEqual({
      tur: 'hazir',
      satirlar: ['a', 'b'],
      sayfa: { sayfa: 2, boyut: 2, toplam: 42 },
      hata: null,
      eskiVeri: false,
      kayitYok: false,
    });
    expect(
      resolveSource({ tur: 'hazir', veri: { ...page, kayitlar: [], toplam: 0 } }).kayitYok,
    ).toBe(true);
    expect(resolveSource({ tur: 'hazir', veri: [] as readonly string[] })).toMatchObject({
      sayfa: null,
      kayitYok: true,
    });
  });

  it('hata: veri YOK (ne eski ne boş liste), kayıt yok DEĞİL', () => {
    const error = new ApiHatasi({ status: 500, kod: 'sunucu', detay: 'x' });
    expect(resolveSource({ tur: 'hata', hata: error })).toEqual({
      tur: 'hata',
      satirlar: [],
      sayfa: null,
      hata: error,
      eskiVeri: false,
      kayitYok: false,
    });
  });

  it('yükleniyor: önceki veri varsa soluk gösterilir; yoksa boş (iskelet)', () => {
    expect(resolveSource({ tur: 'yukleniyor', onceki: page })).toMatchObject({
      satirlar: ['a', 'b'],
      eskiVeri: true,
      kayitYok: false,
    });
    expect(resolveSource({ tur: 'yukleniyor', onceki: undefined })).toMatchObject({
      satirlar: [],
      eskiVeri: false,
      kayitYok: false,
    });
    expect(resolveSource({ tur: 'bos' })).toMatchObject({ satirlar: [], kayitYok: false });
  });
});
