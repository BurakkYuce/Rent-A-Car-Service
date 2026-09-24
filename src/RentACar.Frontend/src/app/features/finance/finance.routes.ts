import type { Routes } from '@angular/router';

import { kaydedilmemisDegisiklikGuard } from '@core/form/kaydedilmemis-degisiklik';
import { ceviriBloguyla } from '@core/i18n/ceviri-blogu';
import { izinGuard } from '@core/oturum/oturum-guard';
import { anyPermissionGuard } from '@features/vehicles/vehicle-guards';

/**
 * F8.2a finans ekranları (1. yarı, PARA) — Blazor yollarıyla AYNI (kesişte yönlendirme birebir). İzinler uçlarla aynı:
 * yazma ekranları FinanceWrite; kurlar okuma OperationsWrite ∨ FinanceWrite ∨ ViewReports, cari ekstre FinanceWrite ∨
 * ViewReports (ekstredeki
 * formlar FinanceWrite, ters kayıt FinanceReverse; kur yazma FinanceWrite — düğmeler sayfada). Para yazan her sayfa
 * sonucu bilinmeyen işlem ya da kirli form varken ayrılışı sorar.
 */
export const FINANCE_ROUTES: Routes = ceviriBloguyla('finans', [
  {
    path: 'kasa',
    title: 'Kasa / Banka — RentACar',
    canMatch: [izinGuard('FinanceWrite')],
    loadComponent: () => import('@features/finance/cash/cash-hub').then((m) => m.CashHub),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'finans/nakit-islem',
    title: 'Nakit İşlem — RentACar',
    canMatch: [izinGuard('FinanceWrite')],
    loadComponent: () =>
      import('@features/finance/cash/cash-operation-page').then((m) => m.CashOperationPage),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'finans/bakiye-duzeltme',
    title: 'Bakiye Düzeltme — RentACar',
    canMatch: [izinGuard('FinanceWrite')],
    loadComponent: () =>
      import('@features/finance/cash/balance-adjustment-page').then((m) => m.BalanceAdjustmentPage),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'cari-virman',
    title: 'Cari Virman — RentACar',
    canMatch: [izinGuard('FinanceWrite')],
    loadComponent: () =>
      import('@features/finance/cash/customer-transfer-page').then((m) => m.CustomerTransferPage),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'depozito',
    title: 'Depozito — RentACar',
    canMatch: [izinGuard('FinanceWrite')],
    loadComponent: () =>
      import('@features/finance/deposit/deposit-page').then((m) => m.DepositPage),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'tek-cari-toplu',
    title: 'Tek Cari Toplu Kapatma — RentACar',
    canMatch: [izinGuard('FinanceWrite')],
    loadComponent: () =>
      import('@features/finance/bulk/single-customer-close').then((m) => m.SingleCustomerClose),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'toplu-tahsilat',
    title: 'Toplu Tahsilat — RentACar',
    canMatch: [izinGuard('FinanceWrite')],
    loadComponent: () =>
      import('@features/finance/bulk/bulk-collection').then((m) => m.BulkCollection),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'toplu-gider',
    title: 'Toplu Gider — RentACar',
    canMatch: [izinGuard('FinanceWrite')],
    loadComponent: () => import('@features/finance/bulk/bulk-expense').then((m) => m.BulkExpense),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'otomatik-tahsilat',
    title: 'Otomatik Tahsilat — RentACar',
    canMatch: [izinGuard('FinanceWrite')],
    loadComponent: () =>
      import('@features/finance/period/auto-collection').then((m) => m.AutoCollection),
  },
  {
    path: 'donem-kapanis',
    title: 'Dönem Kapanışı — RentACar',
    canMatch: [izinGuard('FinanceWrite')],
    loadComponent: () => import('@features/finance/period/period-close').then((m) => m.PeriodClose),
  },
  {
    path: 'kurlar',
    title: 'Döviz Kurları — RentACar',
    canMatch: [anyPermissionGuard('OperationsWrite', 'FinanceWrite', 'ViewReports')],
    loadComponent: () => import('@features/finance/rates/rates-page').then((m) => m.RatesPage),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'cariler/:id/ekstre',
    title: 'Cari Ekstre — RentACar',
    // #295 KVKK M1: cari detayı bakiyeyi yalnız FinanceWrite ∨ ViewReports'a gösteriyor; ekstre aynı kuralla açılır
    // (şube kapsamlı operatöre firma geneli cari defteri gösterilmez). Uç şimdilik daha geniş — karar kuyruğunda.
    canMatch: [anyPermissionGuard('FinanceWrite', 'ViewReports')],
    loadComponent: () =>
      import('@features/finance/statement/customer-statement').then((m) => m.CustomerStatementPage),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
]);
