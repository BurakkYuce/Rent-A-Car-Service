import type { Routes } from '@angular/router';
import { kaydedilmemisDegisiklikGuard } from '@core/form/kaydedilmemis-degisiklik';

/** Sayfalar tembel yüklenir: form seti (forms + CDK) ilk pakete girmez. */
export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('@features/yer-tutucu/yer-tutucu').then((m) => m.YerTutucu),
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
  { path: '**', redirectTo: '' },
];
