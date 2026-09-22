import type { Routes } from '@angular/router';

import { kaydedilmemisDegisiklikGuard } from '@core/form/kaydedilmemis-degisiklik';
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
 */
export const SAYFALAR: Routes = [
  {
    path: '',
    pathMatch: 'full',
    title: 'Yeni arayüz — RentACar',
    loadComponent: () => import('@features/yer-tutucu/yer-tutucu').then((m) => m.YerTutucu),
  },
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
  // F4.3 kira formu: /kiralar/yeni, /kiralar/:id, /kiralar/:id/yazdir.
  ...KIRA_FORMU_ROTALARI,
];
