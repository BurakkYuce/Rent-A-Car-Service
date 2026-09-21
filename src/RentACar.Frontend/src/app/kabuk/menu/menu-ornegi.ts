import type { MenuYaniti } from '@core/api/ui-tipleri';

/**
 * Birim testlerinin örnek menü yanıtı (`GET /api/ui/v1/menu` biçimi). Sunucu izne göre SÜZMÜŞ gelir:
 * bu kullanıcıda Finans yok (FinanceWrite'sız) — istemci eksik öğeyi eklemez, fazlasını gizlemez.
 * Bilerek sırası karışık (istemci `sira`ya göre dizer).
 */
export const ORNEK_MENU: MenuYaniti = {
  ogeler: [
    {
      rota: '/kiralar',
      etiket: 'Kiralar',
      grup: 'Kira',
      sira: 60,
      sahip: 'blazor',
      rozetKodu: null,
      hizliBaglanti: false,
    },
    {
      rota: '/rezervasyonlar',
      etiket: 'Yeni Rezervasyon',
      grup: 'Kısa Yollar',
      sira: 10,
      sahip: 'blazor',
      rozetKodu: null,
      hizliBaglanti: true,
    },
    {
      rota: '/',
      etiket: 'Panel',
      grup: '',
      sira: 40,
      sahip: 'blazor',
      rozetKodu: null,
      hizliBaglanti: false,
    },
    {
      rota: '/vitrin/form',
      etiket: 'Form vitrini',
      grup: 'Vitrin',
      sira: 50,
      sahip: 'spa',
      rozetKodu: null,
      hizliBaglanti: false,
    },
    {
      rota: '/app/vitrin/tablo',
      etiket: 'Tablo vitrini',
      grup: 'Vitrin',
      sira: 52,
      sahip: 'spa',
      rozetKodu: null,
      hizliBaglanti: false,
    },
    {
      rota: '/is-emirleri',
      etiket: 'İş Emirleri',
      grup: 'Servis & Sigorta',
      sira: 70,
      sahip: 'blazor',
      rozetKodu: null,
      hizliBaglanti: false,
    },
    {
      rota: '/isik-raporu',
      etiket: 'Işık Raporu',
      grup: 'Servis & Sigorta',
      sira: 71,
      sahip: 'blazor',
      rozetKodu: null,
      hizliBaglanti: false,
    },
    {
      rota: '/bildirimler',
      etiket: 'Bildirimler',
      grup: '',
      sira: 90,
      sahip: 'blazor',
      rozetKodu: 'okunmamis-bildirim',
      hizliBaglanti: false,
    },
    {
      rota: '/arac-tipleri',
      etiket: 'Araç Tipleri',
      grup: 'Araçlar',
      sira: 30,
      sahip: 'blazor',
      rozetKodu: null,
      hizliBaglanti: false,
    },
    {
      rota: '/arac-tipleri',
      etiket: 'Araç Tipleri',
      grup: 'Tanımlar',
      sira: 80,
      sahip: 'blazor',
      rozetKodu: null,
      hizliBaglanti: false,
    },
  ],
  rozetler: { 'okunmamis-bildirim': 3 },
};
