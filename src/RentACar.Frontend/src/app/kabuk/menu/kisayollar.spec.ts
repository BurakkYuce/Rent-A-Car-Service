import type { MenuResponse } from '@core/api/ui-tipleri';

import { rentalViews, shortcutPair } from './kisayollar';
import { groupIcon, itemIcon } from './menu-ikonlari';
import { buildMenuModel } from './menu-modeli';

type Oge = MenuResponse['ogeler'][number];

function oge(route: string, label: string, extra: Partial<Oge> = {}): Oge {
  return {
    rota: route,
    etiket: label,
    grup: 'Kısa Yollar',
    sira: 10,
    sahip: 'spa',
    rozetKodu: null,
    hizliBaglanti: true,
    ...extra,
  };
}

describe('kisayolCifti', () => {
  it('sunucunun hızlı bağlantılarından: Yeni Kira → form, Yeni Rezervasyon (SPA liste) → form', () => {
    const model = buildMenuModel({
      ogeler: [
        oge('/app/rezervasyonlar', 'Yeni Rezervasyon', { sira: 10 }),
        oge('/app/kiralar/yeni', 'Yeni Kira', { sira: 20 }),
        oge('/app/musaitlik', 'Müsaitlik-Rez Açma', { sira: 30 }),
      ],
      rozetler: {},
    });
    const duplicate = shortcutPair(model);
    expect(duplicate.kira?.hedef).toEqual({ tur: 'spa', yol: '/kiralar/yeni' });
    expect(duplicate.kira?.etiket).toBe('Yeni Kira');
    expect(duplicate.rezervasyon?.hedef).toEqual({ tur: 'spa', yol: '/rezervasyonlar/yeni' });
    expect(duplicate.rezervasyon?.etiket).toBe('Yeni Rezervasyon');
  });

  it('izin yoksa sunucu öğeyi göndermez → düğme de yok (istemci eklemez); Blazor hedefi olduğu gibi', () => {
    const model = buildMenuModel({
      ogeler: [oge('/rezervasyonlar', 'Yeni Rezervasyon', { sahip: 'blazor' })],
      rozetler: {},
    });
    expect(shortcutPair(model)).toEqual({
      kira: null,
      rezervasyon: expect.objectContaining({ hedef: { tur: 'blazor', adres: '/rezervasyonlar' } }),
    });
    expect(shortcutPair(buildMenuModel({ ogeler: [], rozetler: {} }))).toEqual({
      kira: null,
      rezervasyon: null,
    });
  });
});

describe('kiraGorunumleri', () => {
  it('ilk görünüm listenin kendisi; diğerleri gorunum sorgusuyla, tekil kimlikli', () => {
    const [rentals] = buildMenuModel({
      ogeler: [oge('/app/kiralar', 'Kiralar', { grup: 'Kira', hizliBaglanti: false })],
      rozetler: {},
    }).tumu;
    if (!rentals) throw new Error('öğe yok');
    const g = rentalViews(rentals);
    expect(g[0]?.kayit).toBe(rentals);
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
    expect(groupIcon('Araçlar')).toBe('car');
    expect(groupIcon('ARAÇLAR')).toBe('car');
    expect(groupIcon('Tanımlar')).toBe('adjustments-horizontal');
    expect(groupIcon('Bilinmeyen')).toBe('file-text');
    expect(itemIcon({ hedef: { tur: 'spa', yol: '/panel' } })).toBe('home');
    expect(itemIcon({ hedef: { tur: 'blazor', adres: '/bildirimler' } })).toBe('bell');
  });
});
