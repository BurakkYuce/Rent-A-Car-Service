import type { Routes } from '@angular/router';

import { kaydedilmemisDegisiklikGuard } from '@core/form/kaydedilmemis-degisiklik';
import { misafirGuard, oturumGuard } from '@core/oturum/oturum-guard';

/**
 * Uygulama rotaları (`/app/` altında), sayfalar tembel (form seti ve CDK ilk pakete girmez). Kabuk anonim
 * yüklenir; oturum isteyen rota `canMatch: [oturumGuard]`, izin isteyen ek olarak `izinGuard('FinanceWrite')`
 * alır (`@core/oturum/oturum-guard`). F3.2 kabuğu bu listeyi menü kaydıyla genişletir.
 */
export const routes: Routes = [
  {
    path: 'giris',
    canMatch: [misafirGuard],
    title: 'Giriş — RentACar',
    loadComponent: () => import('@features/giris/giris-sayfasi').then((m) => m.GirisSayfasi),
  },
  {
    path: '',
    pathMatch: 'full',
    canMatch: [oturumGuard],
    loadComponent: () => import('@features/yer-tutucu/yer-tutucu').then((m) => m.YerTutucu),
  },
  {
    // F3.3 oturum/geri bildirim vitrini (F3.7 vitrininin parçası); e2e bunun üstünde koşar.
    path: 'vitrin/geri-bildirim',
    canMatch: [oturumGuard],
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
  { path: '**', redirectTo: '' },
];
