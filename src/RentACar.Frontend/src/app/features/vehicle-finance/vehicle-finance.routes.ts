import type { Routes } from '@angular/router';

import { kaydedilmemisDegisiklikGuard } from '@core/form/kaydedilmemis-degisiklik';
import { ceviriBloguyla } from '@core/i18n/ceviri-blogu';
import { izinGuard } from '@core/oturum/oturum-guard';
import { anyPermissionGuard } from '@features/vehicles/vehicle-guards';

/**
 * F6.2b araç finans ekranları — Blazor yollarıyla AYNI (`/arac-kredi`, `/musteri-taksit`, `/arac-siparis`, `/baf`,
 * `/hasar`, `/filo-plan`; kesişte yönlendirme birebir). İzinler uçlarla aynı:
 * - kredi okuma OperationsWrite ∨ FinanceWrite ∨ ViewReports (oluşturma OperationsWrite, taksit ödeme FinanceWrite,
 *   iptal OperationsDelete — düğmeler sayfada/sunucu bayraklarında);
 * - müşteri taksit okuma FinanceWrite ∨ ViewReports (yazma FinanceWrite);
 * - sipariş okuma OperationsWrite ∨ FinanceWrite ∨ ViewReports (yazma OperationsWrite — sunucu `yetkiler`);
 * - BAF ve hasar OperationsWrite (BAF iptal OperationsDelete); filo plan ViewReports ∨ OperationsWrite.
 * `yeni`, `:id`'den ÖNCE eşleşmeli.
 */
export const VEHICLE_FINANCE_ROUTES: Routes = ceviriBloguyla('arac-finans', [
  {
    path: 'arac-kredi',
    title: 'Araç Kredisi — RentACar',
    canMatch: [anyPermissionGuard('OperationsWrite', 'FinanceWrite', 'ViewReports')],
    loadComponent: () =>
      import('@features/vehicle-finance/loans/loan-list').then((m) => m.LoanList),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'arac-kredi/:id',
    title: 'Araç Kredisi — RentACar',
    canMatch: [anyPermissionGuard('OperationsWrite', 'FinanceWrite', 'ViewReports')],
    loadComponent: () =>
      import('@features/vehicle-finance/loans/loan-detail').then((m) => m.LoanDetail),
  },
  {
    path: 'musteri-taksit',
    title: 'Müşteri Taksitleri — RentACar',
    canMatch: [anyPermissionGuard('FinanceWrite', 'ViewReports')],
    loadComponent: () =>
      import('@features/vehicle-finance/customer-installments/customer-installment-list').then(
        (m) => m.CustomerInstallmentList,
      ),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'arac-siparis',
    title: 'Araç Sipariş — RentACar',
    canMatch: [anyPermissionGuard('OperationsWrite', 'FinanceWrite', 'ViewReports')],
    loadComponent: () =>
      import('@features/vehicle-finance/orders/order-list').then((m) => m.OrderList),
  },
  {
    path: 'arac-siparis/yeni',
    title: 'Yeni Araç Siparişi — RentACar',
    canMatch: [izinGuard('OperationsWrite')],
    loadComponent: () =>
      import('@features/vehicle-finance/orders/order-form').then((m) => m.OrderForm),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'arac-siparis/:id',
    title: 'Araç Siparişi — RentACar',
    canMatch: [anyPermissionGuard('OperationsWrite', 'FinanceWrite', 'ViewReports')],
    loadComponent: () =>
      import('@features/vehicle-finance/orders/order-form').then((m) => m.OrderForm),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'baf',
    title: 'BAF — Personel Araç Tahsis — RentACar',
    canMatch: [izinGuard('OperationsWrite')],
    loadComponent: () =>
      import('@features/vehicle-finance/allocations/allocation-list').then((m) => m.AllocationList),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'hasar',
    title: 'Hasar Dosyaları — RentACar',
    canMatch: [izinGuard('OperationsWrite')],
    loadComponent: () =>
      import('@features/vehicle-finance/damage-files/damage-file-list').then(
        (m) => m.DamageFileList,
      ),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'filo-plan',
    title: 'Filo Plan — RentACar',
    canMatch: [anyPermissionGuard('ViewReports', 'OperationsWrite')],
    loadComponent: () =>
      import('@features/vehicle-finance/fleet-plan/fleet-plan-list').then((m) => m.FleetPlanList),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
]);
