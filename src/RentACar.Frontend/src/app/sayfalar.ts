import type { Routes } from '@angular/router';

import { kaydedilmemisDegisiklikGuard } from '@core/form/kaydedilmemis-degisiklik';
import { ceviriBlogu, ceviriBloguyla } from '@core/i18n/ceviri-blogu';
import { KIRA_FORMU_ROTALARI } from '@features/kira-formu/kira-formu.routes';

/**
 * Kabuk içindeki sayfalar (oturum şart; `canMatch: [oturumGuard]` kabuk rotasında). Hepsi tembel.
 *
 * Kurallar (F3.2):
 * - `title` zorunlu (`'<Ad> — RentACar'`): belge başlığı ve SEKME etiketi buradan gelir.
 * - Her sayfa bir sekmedir: başka sekmeye geçilince bileşen yaşamaya devam eder. Kayıt sayfası
 *   `:id` parametresiyle (`kiralar/:id`) — farklı kayıt ayrı sekme; tarayıcıda yalnız desen + id
 *   saklanır. `:id` dışında yol parametresi olan sayfa sekme olur ama yenilemede geri gelmez.
 * - Tek seferlik akış `data: { sekme: false }` ile sekme dışı kalır.
 * - Kirli form: `canDeactivate: [kaydedilmemisDegisiklikGuard]` (sekme KAPATILIRKEN sorar).
 * - İzin: `canMatch: [izinGuard('FinanceWrite')]`.
 * - Çeviri: özellik metinleri ilk pakette değil; sayfa kendi bloğunu yükler: `canActivate: [ceviriBlogu('panel')]`
 *   ya da özellik rota dizisi `ceviriBloguyla('<blok>', [...])` (AGENTS.md "i18n").
 */
export const SAYFALAR: Routes = [
  {
    path: '',
    pathMatch: 'full',
    title: 'Yeni arayüz — RentACar',
    loadComponent: () => import('@features/yer-tutucu/yer-tutucu').then((m) => m.YerTutucu),
    canActivate: [ceviriBlogu('vitrin')],
  },
  {
    // F4.5 Panel (Blazor `/`). Kök yerine `/panel`: kök F3 vitrin/e2e/görsel tabanlarının çapası. F4.6: pilot
    // girişinin varsayılan inişi (`PILOT_INIS`), menü "Panel" kaydı (`/app/panel`, sahip spa) ve Blazor `/`'ın
    // pilot yönlendirmesi bu rota. İzin kapısı içerikte (sunucu).
    path: 'panel',
    title: 'Panel — RentACar',
    loadComponent: () => import('@features/panel/panel-sayfasi').then((m) => m.PanelSayfasi),
    canActivate: [ceviriBlogu('panel')],
  },
  {
    // F4.2 kira listesi. Kayıt formu (`kiralar/yeni`, `kiralar/:id`) F4.3'te; liste yalnız bağlantı verir.
    path: 'kiralar',
    title: 'Kira Sözleşmeleri — RentACar',
    loadComponent: () =>
      import('@features/kiralar/kira-listesi/kira-listesi').then((m) => m.KiraListesi),
    canActivate: [ceviriBlogu('kiralar')],
  },
  ...ceviriBloguyla('vitrin', [
    {
      // F3.7 vitrin dizini: çekirdeğin her parçasına bağlantı (e2e: axe iki tema, 320–1440 taşma, görsel).
      path: 'vitrin',
      title: 'Vitrin — RentACar',
      loadComponent: () =>
        import('@features/vitrin/vitrin-dizini/vitrin-dizini').then((m) => m.VitrinDizini),
    },
    {
      path: 'vitrin/tokenlar',
      title: 'Token vitrini — RentACar',
      loadComponent: () =>
        import('@features/vitrin/tokenlar-vitrini/tokenlar-vitrini').then((m) => m.TokenlarVitrini),
    },
    {
      path: 'vitrin/primitifler',
      title: 'Primitif vitrini — RentACar',
      loadComponent: () =>
        import('@features/vitrin/primitif-vitrini/primitif-vitrini').then((m) => m.PrimitifVitrini),
    },
    {
      path: 'vitrin/kabuk',
      title: 'Kabuk vitrini — RentACar',
      loadComponent: () =>
        import('@features/vitrin/kabuk-vitrini/kabuk-vitrini').then((m) => m.KabukVitrini),
    },
    {
      // F3.3 oturum/geri bildirim vitrini (F3.7 vitrininin parçası); e2e bunun üstünde koşar.
      path: 'vitrin/geri-bildirim',
      title: 'Geri bildirim vitrini — RentACar',
      loadComponent: () =>
        import('@features/vitrin/geri-bildirim-vitrini').then((m) => m.GeriBildirimVitrini),
    },
    {
      // F3.6 form seti vitrini (F3.7 vitrininin parçası); e2e bunun üstünde koşar.
      path: 'vitrin/form',
      title: 'Form vitrini — RentACar',
      loadComponent: () =>
        import('@features/vitrin/form-vitrini/form-vitrini').then((m) => m.FormVitrini),
      canDeactivate: [kaydedilmemisDegisiklikGuard],
    },
    {
      path: 'vitrin/tanim',
      title: 'Tanım vitrini — RentACar',
      loadComponent: () =>
        import('@features/vitrin/tanim-vitrini/tanim-vitrini').then((m) => m.TanimVitrini),
      canDeactivate: [kaydedilmemisDegisiklikGuard],
    },
    {
      // F3.5 tablo motoru vitrini: TanStack table-core + virtual-core yalnız bu parçaya girer.
      path: 'vitrin/tablo',
      title: 'Tablo vitrini — RentACar',
      loadComponent: () =>
        import('@features/vitrin/tablo-vitrini/tablo-vitrini').then((m) => m.TabloVitrini),
    },
  ]),
  // F4.3 kira formu: /kiralar/yeni, /kiralar/:id, /kiralar/:id/yazdir.
  ...KIRA_FORMU_ROTALARI,
];
