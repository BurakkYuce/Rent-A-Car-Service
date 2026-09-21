import type { Routes } from '@angular/router';

import { YerTutucu } from '@features/yer-tutucu/yer-tutucu';

/**
 * Rotalar. Her özellik sayfası `loadComponent` ile TEMBEL yüklenir (ilk paket bütçesi 300 kB);
 * tablo motoru (TanStack table-core + virtual-core) yalnız tabloyu kullanan sayfanın parçasına girer.
 */
export const rotalar: Routes = [
  {
    path: 'vitrin/tablo',
    title: 'Tablo vitrini · RentACar',
    loadComponent: () =>
      import('@features/vitrin/tablo-vitrini/tablo-vitrini').then((m) => m.TabloVitrini),
  },
  { path: '**', component: YerTutucu },
];
