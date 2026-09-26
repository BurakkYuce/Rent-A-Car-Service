import type { Routes } from '@angular/router';

import { unsavedChangesGuard } from '@core/form/kaydedilmemis-degisiklik';
import { withTranslationBlock } from '@core/i18n/ceviri-blogu';
import { permissionGuard } from '@core/oturum/session-guard';
import { anyPermissionGuard } from '@features/vehicles/vehicle-guards';

/**
 * F8.2a finans ekranları (1. yarı, PARA) — Blazor yollarıyla AYNI (kesişte yönlendirme birebir). İzinler uçlarla aynı:
 * yazma ekranları FinanceWrite; kurlar okuma OperationsWrite ∨ FinanceWrite ∨ ViewReports, cari ekstre FinanceWrite ∨
 * ViewReports (ekstredeki
 * formlar FinanceWrite, ters kayıt FinanceReverse; kur yazma FinanceWrite — düğmeler sayfada). Para yazan her sayfa
 * sonucu bilinmeyen işlem ya da kirli form varken ayrılışı sorar.
 */
export const FINANCE_ROUTES: Routes = withTranslationBlock('finans', [
  {
    path: 'kasa',
    title: 'Kasa / Banka — RentACar',
    canMatch: [permissionGuard('FinanceWrite')],
    loadComponent: () => import('@features/finance/cash/cash-hub').then((m) => m.CashHub),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'finans/nakit-islem',
    title: 'Nakit İşlem — RentACar',
    canMatch: [permissionGuard('FinanceWrite')],
    loadComponent: () =>
      import('@features/finance/cash/cash-operation-page').then((m) => m.CashOperationPage),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'finans/bakiye-duzeltme',
    title: 'Bakiye Düzeltme — RentACar',
    canMatch: [permissionGuard('FinanceWrite')],
    loadComponent: () =>
      import('@features/finance/cash/balance-adjustment-page').then((m) => m.BalanceAdjustmentPage),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'cari-virman',
    title: 'Cari Virman — RentACar',
    canMatch: [permissionGuard('FinanceWrite')],
    loadComponent: () =>
      import('@features/finance/cash/customer-transfer-page').then((m) => m.CustomerTransferPage),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'depozito',
    title: 'Depozito — RentACar',
    canMatch: [permissionGuard('FinanceWrite')],
    loadComponent: () =>
      import('@features/finance/deposit/deposit-page').then((m) => m.DepositPage),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'tek-cari-toplu',
    title: 'Tek Cari Toplu Kapatma — RentACar',
    canMatch: [permissionGuard('FinanceWrite')],
    loadComponent: () =>
      import('@features/finance/bulk/single-customer-close').then((m) => m.SingleCustomerClose),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'toplu-tahsilat',
    title: 'Toplu Tahsilat — RentACar',
    canMatch: [permissionGuard('FinanceWrite')],
    loadComponent: () =>
      import('@features/finance/bulk/bulk-collection').then((m) => m.BulkCollection),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'toplu-gider',
    title: 'Toplu Gider — RentACar',
    canMatch: [permissionGuard('FinanceWrite')],
    loadComponent: () => import('@features/finance/bulk/bulk-expense').then((m) => m.BulkExpense),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'otomatik-tahsilat',
    title: 'Otomatik Tahsilat — RentACar',
    canMatch: [permissionGuard('FinanceWrite')],
    loadComponent: () =>
      import('@features/finance/period/auto-collection').then((m) => m.AutoCollection),
  },
  {
    path: 'donem-kapanis',
    title: 'Dönem Kapanışı — RentACar',
    canMatch: [permissionGuard('FinanceWrite')],
    loadComponent: () => import('@features/finance/period/period-close').then((m) => m.PeriodClose),
  },
  {
    path: 'kurlar',
    title: 'Döviz Kurları — RentACar',
    canMatch: [anyPermissionGuard('OperationsWrite', 'FinanceWrite', 'ViewReports')],
    loadComponent: () => import('@features/finance/rates/rates-page').then((m) => m.RatesPage),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'cariler/:id/ekstre',
    title: 'Cari Ekstre — RentACar',
    // #295 KVKK M1: cari detayı bakiyeyi yalnız FinanceWrite ∨ ViewReports'a gösteriyor; ekstre aynı kuralla açılır
    // (şube kapsamlı operatöre firma geneli cari defteri gösterilmez). Uç şimdilik daha geniş — karar kuyruğunda.
    canMatch: [anyPermissionGuard('FinanceWrite', 'ViewReports')],
    loadComponent: () =>
      import('@features/finance/statement/customer-statement').then((m) => m.CustomerStatementPage),
    canDeactivate: [unsavedChangesGuard],
  },
]);
