import type { MenuYaniti } from '@core/api/ui-tipleri';

import { kiraGorunumleri, kisayolCifti } from './kisayollar';
import { grupIkonu, ogeIkonu } from './menu-ikonlari';
import { menuModeliKur } from './menu-modeli';

type Oge = MenuYaniti['ogeler'][number];

function oge(rota: string, etiket: string, ek: Partial<Oge> = {}): Oge {
  return {
    rota,
    etiket,
    grup: 'Kısa Yollar',
    sira: 10,
    sahip: 'spa',
    rozetKodu: null,
    hizliBaglanti: true,
    ...ek,
  };
}

describe('kisayolCifti', () => {
  it('sunucunun hızlı bağlantılarından: Yeni Kira → form, Yeni Rezervasyon (SPA liste) → form', () => {
    const model = menuModeliKur({
      ogeler: [
        oge('/app/rezervasyonlar', 'Yeni Rezervasyon', { sira: 10 }),
        oge('/app/kiralar/yeni', 'Yeni Kira', { sira: 20 }),
        oge('/app/musaitlik', 'Müsaitlik-Rez Açma', { sira: 30 }),
      ],
      rozetler: {},
    });
    const cift = kisayolCifti(model);
    expect(cift.kira?.hedef).toEqual({ tur: 'spa', yol: '/kiralar/yeni' });
    expect(cift.kira?.etiket).toBe('Yeni Kira');
    expect(cift.rezervasyon?.hedef).toEqual({ tur: 'spa', yol: '/rezervasyonlar/yeni' });
    expect(cift.rezervasyon?.etiket).toBe('Yeni Rezervasyon');
  });

  it('izin yoksa sunucu öğeyi göndermez → düğme de yok (istemci eklemez); Blazor hedefi olduğu gibi', () => {
    const model = menuModeliKur({
      ogeler: [oge('/rezervasyonlar', 'Yeni Rezervasyon', { sahip: 'blazor' })],
      rozetler: {},
    });
    expect(kisayolCifti(model)).toEqual({
      kira: null,
      rezervasyon: expect.objectContaining({ hedef: { tur: 'blazor', adres: '/rezervasyonlar' } }),
    });
    expect(kisayolCifti(menuModeliKur({ ogeler: [], rozetler: {} }))).toEqual({
      kira: null,
      rezervasyon: null,
    });
  });
});

describe('kiraGorunumleri', () => {
  it('ilk görünüm listenin kendisi; diğerleri gorunum sorgusuyla, tekil kimlikli', () => {
    const [kiralar] = menuModeliKur({
      ogeler: [oge('/app/kiralar', 'Kiralar', { grup: 'Kira', hizliBaglanti: false })],
      rozetler: {},
    }).tumu;
    if (!kiralar) throw new Error('öğe yok');
    const g = kiraGorunumleri(kiralar);
    expect(g[0]?.kayit).toBe(kiralar);
    expect(g.map((x) => (x.kayit.hedef.tur === 'spa' ? x.kayit.hedef.yol : ''))).toEqual([
      '/kiralar',
      '/kiralar?gorunum=kirada',
      '/kiralar?gorunum=geciken',
      '/kiralar?gorunum=bugun-cikan',
      '/kiralar?gorunum=bugun-donecek',
      '/kiralar?gorunum=faturasiz',
      '/kiralar?gorunum=kapali',
    ]);
    expect(new Set(g.map((x) => x.kayit.kimlik)).size).toBe(g.length);
    expect(g.filter((x) => x.hata).map((x) => x.kod)).toEqual(['geciken']);
  });
});

describe('menü ikonları', () => {
  it('grup adı Türkçe-gevşek eşleşir; bilinmeyen grup genel ikon', () => {
    expect(grupIkonu('Araçlar')).toBe('car');
    expect(grupIkonu('ARAÇLAR')).toBe('car');
    expect(grupIkonu('Tanımlar')).toBe('adjustments-horizontal');
    expect(grupIkonu('Bilinmeyen')).toBe('file-text');
    expect(ogeIkonu({ hedef: { tur: 'spa', yol: '/panel' } })).toBe('home');
    expect(ogeIkonu({ hedef: { tur: 'blazor', adres: '/bildirimler' } })).toBe('bell');
  });
});
