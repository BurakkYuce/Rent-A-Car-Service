import type { Routes } from '@angular/router';

import { unsavedChangesGuard } from '@core/form/kaydedilmemis-degisiklik';
import { withTranslationBlock } from '@core/i18n/ceviri-blogu';
import { permissionGuard } from '@core/oturum/session-guard';
import { anyPermissionGuard } from '@features/vehicles/vehicle-guards';

/**
 * F7.2 cari ekranları — Blazor yollarıyla AYNI (`/cariler`, `/cariler/{id}`, `/cariler/{id}/detay`; kesişte yönlendirme
 * birebir) + yeni cari kartı (`/cariler/yeni`; Blazor'da liste içi form). İzinler uçlarla aynı: okuma OperationsWrite ∨
 * FinanceWrite ∨ ViewReports; oluşturma OperationsWrite (kartta yazma düğmesi de); silme OperationsDelete (satır düğmesi).
 * `yeni`, `:id`'den ÖNCE eşleşmeli.
 */
const READ = anyPermissionGuard('OperationsWrite', 'FinanceWrite', 'ViewReports');

export const CUSTOMER_ROUTES: Routes = withTranslationBlock('cari', [
  {
    path: 'cariler',
    title: 'Cariler — RentACar',
    canMatch: [READ],
    loadComponent: () =>
      import('@features/customers/customer-list/customer-list').then((m) => m.CustomerList),
  },
  {
    path: 'cariler/yeni',
    title: 'Yeni Cari — RentACar',
    canMatch: [permissionGuard('OperationsWrite')],
    loadComponent: () =>
      import('@features/customers/customer-form/customer-form').then((m) => m.CustomerForm),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'cariler/:id',
    title: 'Cari Kartı — RentACar',
    canMatch: [READ],
    loadComponent: () =>
      import('@features/customers/customer-form/customer-form').then((m) => m.CustomerForm),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'cariler/:id/detay',
    title: 'Cari Detayı — RentACar',
    canMatch: [READ],
    loadComponent: () =>
      import('@features/customers/customer-detail/customer-detail').then((m) => m.CustomerDetail),
  },
]);
