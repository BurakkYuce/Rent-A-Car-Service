import type { ApiHatasi } from '../api/api-hatasi';
import { TahsilatDenemesi, sonucuBilinmeyenHata } from './tahsilat-denemesi';

const K1 = 'aaaaaaaa-0000-4000-8000-000000000001';
const K2 = 'aaaaaaaa-0000-4000-8000-000000000002';

const hata = (kod: ApiHatasi['kod'], mevcut?: { ayniIcerik: boolean }): ApiHatasi =>
  ({
    status: kod === 'mukerrer' ? 409 : 0,
    kod,
    detay: 'x',
    ...(mevcut ? { mevcut: { id: 'c', belgeNo: 'T-1', tutar: 500, doviz: 'TRY', ...mevcut } } : {}),
  }) as ApiHatasi;

describe('TahsilatDenemesi (M-C / L-2)', () => {
  it('sonucu bilinmeyen: ağ, 5xx, kodsuz; kesin red değil', () => {
    expect(
      ['ag', 'sunucu', 'bilinmeyen'].map((k) => sonucuBilinmeyenHata(hata(k as 'ag'))),
    ).toEqual([true, true, true]);
    for (const k of ['dogrulama', 'cakisma', 'yetki_yok', 'oturum_yok', 'mukerrer'] as const)
      expect(sonucuBilinmeyenHata(hata(k))).toBe(false);
  });

  it('ilk gönderim tekrar değildir; bilinmeyen sonuçtan sonra AYNI anahtar tekrardır, başka anahtar değildir', () => {
    const d = new TahsilatDenemesi();
    expect(d.tekrarMi(K1)).toBe(false);
    expect(d.hataGeldi(K1, hata('ag'), false)).toBeNull();
    expect(d.tekrarMi(K1)).toBe(true);
    expect(d.tekrarMi(K2)).toBe(false);
    expect(d.tekrarMi(null)).toBe(false);
  });

  it('409 sınıfları: mevcut yok → bayat; aynı içerik → zaten; farklı + tekrar → önceki deneme; farklı + ilk → başka işlem', () => {
    const d = new TahsilatDenemesi();
    expect(d.hataGeldi(K1, hata('mukerrer'), true)).toBe('bayatAnahtar');
    expect(d.hataGeldi(K1, hata('mukerrer', { ayniIcerik: true }), true)).toBe('zatenKaydedildi');
    expect(d.hataGeldi(K1, hata('mukerrer', { ayniIcerik: false }), true)).toBe(
      'oncekiDenemeKaydedilmis',
    );
    expect(d.hataGeldi(K1, hata('mukerrer', { ayniIcerik: false }), false)).toBe(
      'baskaIslemYazildi',
    );
  });

  it('kesin red izi SİLMEZ; mukerrer ve 2xx siler', () => {
    const d = new TahsilatDenemesi();
    d.hataGeldi(K1, hata('sunucu'), false);
    expect(d.hataGeldi(K1, hata('dogrulama'), true)).toBeNull();
    expect(d.tekrarMi(K1)).toBe(true);
    d.hataGeldi(K1, hata('mukerrer', { ayniIcerik: false }), true);
    expect(d.tekrarMi(K1)).toBe(false);
    d.hataGeldi(K1, hata('ag'), false);
    d.basarili();
    expect(d.tekrarMi(K1)).toBe(false);
  });

  it('L-2: tutar yenileme yalnız FARKLI anahtarlı ilk tazelemede, bir kez', () => {
    const d = new TahsilatDenemesi();
    expect(d.tutarYenilensinMi(K2)).toBe(false); // istenmedi
    d.tutarYenilemesiIste(K1);
    expect(d.tutarYenilensinMi(K1)).toBe(false); // aynı (bayat) anahtar
    expect(d.tutarYenilensinMi(undefined)).toBe(false);
    expect(d.tutarYenilensinMi(K2)).toBe(true);
    expect(d.tutarYenilensinMi(K2)).toBe(false);
    d.tutarYenilemesiIste(K1);
    d.basarili();
    expect(d.tutarYenilensinMi(K2)).toBe(false);
  });
});
