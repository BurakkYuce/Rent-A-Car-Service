import type { Routes } from '@angular/router';

import { kaydedilmemisDegisiklikGuard } from '@core/form/kaydedilmemis-degisiklik';
import { ceviriBloguyla } from '@core/i18n/ceviri-blogu';
import { anyPermissionGuard } from '@features/vehicles/vehicle-guards';

/** Servis / sigorta / vade uçlarının okuma izni (F9.1 `ReadAny`; Blazor sayfaları tüm rollere açıktı). */
const READ = anyPermissionGuard('OperationsWrite', 'FinanceWrite', 'ViewReports');

/**
 * F9.2 servis / sigorta / vade ekranları — Blazor yollarıyla AYNI kök (`/servisler`, `/regulasyon`, `/vade`; kesişte
 * yönlendirme birebir). Blazor `/regulasyon` tek sayfasının üç bölümü ayrı liste rotası (`/regulasyon`,
 * `/regulasyon/mtv`, `/regulasyon/muayene`); kayıtlar `:id` ile. Yazma izinleri (kayıt/kalem OperationsWrite, ödeme ve
 * yansıtma FinanceWrite, iptal OperationsDelete) sunucu `yetkiler`'inden düğmelerde.
 */
export const SERVICE_INSURANCE_ROUTES: Routes = ceviriBloguyla('servis-sigorta', [
  {
    path: 'servisler',
    title: 'Servis / Bakım — RentACar',
    canMatch: [READ],
    loadComponent: () =>
      import('@features/service-insurance/services/service-list').then((m) => m.ServiceList),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'servisler/:id',
    title: 'Servis Kaydı — RentACar',
    canMatch: [READ],
    loadComponent: () =>
      import('@features/service-insurance/services/service-detail').then((m) => m.ServiceDetail),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'regulasyon',
    title: 'Sigorta Poliçeleri — RentACar',
    canMatch: [READ],
    loadComponent: () =>
      import('@features/service-insurance/regulation/policy-list').then((m) => m.PolicyList),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'regulasyon/sigortalar/:id',
    title: 'Sigorta Poliçesi — RentACar',
    canMatch: [READ],
    loadComponent: () =>
      import('@features/service-insurance/regulation/policy-detail').then((m) => m.PolicyDetail),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'regulasyon/mtv',
    title: 'MTV — RentACar',
    canMatch: [READ],
    data: { kind: 'mtv' },
    loadComponent: () =>
      import('@features/service-insurance/regulation/installment-list').then(
        (m) => m.InstallmentList,
      ),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'regulasyon/mtv/:id',
    title: 'MTV Kaydı — RentACar',
    canMatch: [READ],
    data: { kind: 'mtv' },
    loadComponent: () =>
      import('@features/service-insurance/regulation/installment-record').then(
        (m) => m.InstallmentRecord,
      ),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'regulasyon/muayene',
    title: 'Muayene — RentACar',
    canMatch: [READ],
    data: { kind: 'muayene' },
    loadComponent: () =>
      import('@features/service-insurance/regulation/installment-list').then(
        (m) => m.InstallmentList,
      ),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'regulasyon/muayeneler/:id',
    title: 'Muayene Kaydı — RentACar',
    canMatch: [READ],
    data: { kind: 'muayene' },
    loadComponent: () =>
      import('@features/service-insurance/regulation/installment-record').then(
        (m) => m.InstallmentRecord,
      ),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'vade',
    title: 'Vade Uyarıları — RentACar',
    canMatch: [READ],
    loadComponent: () =>
      import('@features/service-insurance/due-board/due-board').then((m) => m.DueBoardPage),
  },
]);
