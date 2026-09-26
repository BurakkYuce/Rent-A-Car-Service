import type { Routes } from '@angular/router';

import { guestGuard, sessionGuard } from '@core/oturum/session-guard';

/**
 * Uygulama rotaları (`/app/` altında). Giriş sayfası kabuk DIŞINDA; geri kalan her şey oturum ister
 * (`oturumGuard` kabuk rotasında — vitrinler dahil, FetchPolicy oturum bağlamı olmadan yüklemez) ve
 * kabuğun (menü, üst çubuk, sekmeler) içinde açılır. Kabuk ve sayfalar tembel parça: ilk pakette yalnız
 * rota iskeleti ve sekme stratejisi var. Sayfa eklemek için `sayfalar.ts`.
 */
export const routes: Routes = [
  {
    path: 'giris',
    canMatch: [guestGuard],
    title: 'Giriş — RentACar',
    loadComponent: () => import('@features/giris/login-page').then((m) => m.LoginPage),
  },
  // F12.2 platform console: its own session and layout, OUTSIDE the tenant shell (no tenant menu/tabs).
  {
    path: 'platform',
    loadChildren: () => import('@features/platform/platform.routes').then((m) => m.PLATFORM_ROUTES),
  },
  {
    path: '',
    canMatch: [sessionGuard],
    loadChildren: () => import('./kabuk/kabuk.routes').then((m) => m.SHELL_ROUTES),
  },
  { path: '**', redirectTo: '' },
];
