import type { Routes } from '@angular/router';

import { misafirGuard, oturumGuard } from '@core/oturum/oturum-guard';

/**
 * Uygulama rotaları (`/app/` altında). Giriş sayfası kabuk DIŞINDA; geri kalan her şey oturum ister
 * (`oturumGuard` kabuk rotasında — vitrinler dahil, FetchPolicy oturum bağlamı olmadan yüklemez) ve
 * kabuğun (menü, üst çubuk, sekmeler) içinde açılır. Kabuk ve sayfalar tembel parça: ilk pakette yalnız
 * rota iskeleti ve sekme stratejisi var. Sayfa eklemek için `sayfalar.ts`.
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
    canMatch: [oturumGuard],
    loadChildren: () => import('./kabuk/kabuk.routes').then((m) => m.KABUK_ROTALARI),
  },
  { path: '**', redirectTo: '' },
];
