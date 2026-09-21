import { ApiHatasi } from '@core/api/api-hatasi';

import { kaynakCoz } from './tablo-kaynagi';

describe('Tablo kaynağı (TemelStore durumu → çizim hâli)', () => {
  const sayfa = { kayitlar: ['a', 'b'], toplam: 42, sayfaNo: 2, boyut: 2 };

  it('hazır Sayfa<T>: satırlar + sunucu sayfalaması; sıfır kayıt yalnız burada "kayıt yok"', () => {
    expect(kaynakCoz({ tur: 'hazir', veri: sayfa })).toEqual({
      tur: 'hazir',
      satirlar: ['a', 'b'],
      sayfa: { sayfa: 2, boyut: 2, toplam: 42 },
      hata: null,
      eskiVeri: false,
      kayitYok: false,
    });
    expect(kaynakCoz({ tur: 'hazir', veri: { ...sayfa, kayitlar: [], toplam: 0 } }).kayitYok).toBe(
      true,
    );
    expect(kaynakCoz({ tur: 'hazir', veri: [] as readonly string[] })).toMatchObject({
      sayfa: null,
      kayitYok: true,
    });
  });

  it('hata: veri YOK (ne eski ne boş liste), kayıt yok DEĞİL', () => {
    const hata = new ApiHatasi({ status: 500, kod: 'sunucu', detay: 'x' });
    expect(kaynakCoz({ tur: 'hata', hata })).toEqual({
      tur: 'hata',
      satirlar: [],
      sayfa: null,
      hata,
      eskiVeri: false,
      kayitYok: false,
    });
  });

  it('yükleniyor: önceki veri varsa soluk gösterilir; yoksa boş (iskelet)', () => {
    expect(kaynakCoz({ tur: 'yukleniyor', onceki: sayfa })).toMatchObject({
      satirlar: ['a', 'b'],
      eskiVeri: true,
      kayitYok: false,
    });
    expect(kaynakCoz({ tur: 'yukleniyor', onceki: undefined })).toMatchObject({
      satirlar: [],
      eskiVeri: false,
      kayitYok: false,
    });
    expect(kaynakCoz({ tur: 'bos' })).toMatchObject({ satirlar: [], kayitYok: false });
  });
});
