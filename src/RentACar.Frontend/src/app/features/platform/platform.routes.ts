import type { Routes } from '@angular/router';

import { ceviriBlogu } from '@core/i18n/ceviri-blogu';

import { platformGuestGuard, platformSessionGuard } from './platform-session';

/**
 * `/app/platform/*` — platform console (F12.2). A SEPARATE route tree from the tenant shell: no tenant
 * menu, tabs or `OturumServisi` guard; its own session (`PlatformSessionService`) and layout
 * (`PlatformShell`). Blazor parity: `/platform/login`, `/platform/tenants`, `/platform/tenants/{id}`,
 * `/platform/belgeler` (+ the summary strip of `GET platform/ozet` as the landing page).
 */
export const PLATFORM_ROUTES: Routes = [
  {
    path: 'giris',
    canMatch: [platformGuestGuard],
    canActivate: [ceviriBlogu('platform')],
    title: 'Platform Girişi — RentACar',
    loadComponent: () => import('./login/platform-login-page').then((m) => m.PlatformLoginPage),
  },
  {
    path: '',
    canMatch: [platformSessionGuard],
    canActivate: [ceviriBlogu('platform')],
    loadComponent: () => import('./platform-shell').then((m) => m.PlatformShell),
    children: [
      {
        path: '',
        pathMatch: 'full',
        title: 'Platform Özeti — RentACar',
        loadComponent: () =>
          import('./summary/platform-summary-page').then((m) => m.PlatformSummaryPage),
      },
      {
        path: 'kiracilar',
        title: 'Firmalar — Platform',
        loadComponent: () => import('./tenants/tenant-list-page').then((m) => m.TenantListPage),
      },
      {
        path: 'kiracilar/:id',
        title: 'Firma Detayı — Platform',
        loadComponent: () => import('./tenants/tenant-detail-page').then((m) => m.TenantDetailPage),
      },
      {
        path: 'belgeler',
        title: 'Belge Merkezi — Platform',
        loadComponent: () =>
          import('./documents/document-center-page').then((m) => m.DocumentCenterPage),
      },
      { path: '**', redirectTo: '' },
    ],
  },
];
