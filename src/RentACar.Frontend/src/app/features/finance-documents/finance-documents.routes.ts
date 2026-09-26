import type { Routes } from '@angular/router';

import { unsavedChangesGuard } from '@core/form/kaydedilmemis-degisiklik';
import { withTranslationBlock } from '@core/i18n/ceviri-blogu';
import { permissionGuard } from '@core/oturum/session-guard';
import { anyPermissionGuard } from '@features/vehicles/vehicle-guards';

/**
 * F8.2b finans belge ekranları — Blazor yollarıyla AYNI (`/faturalar`, `/faturalar/detay-listesi`,
 * `/faturalar/:id/yazdir`, `/cezalar`, `/giderler`, `/gelen-efatura`, `/satislar`; kesişte yönlendirme birebir).
 * Kapılar uçların okuma izinleriyle birebir (yazma düğmeleri sayfada ayrıca izinle gizlenir, sunucu zorlar):
 * - fatura okuma FinanceWrite ∨ ViewReports (manuel/toplu FinanceWrite, iade FinanceReverse); detay listesi ViewReports;
 *   yazdır Blazor `InvoicePrint` gibi ViewReports;
 * - ceza okuma OperationsWrite ∨ FinanceWrite ∨ ViewReports (kayıt OperationsWrite, yansıt/öde FinanceWrite, iptal
 *   OperationsDelete);
 * - gider okuma FinanceWrite ∨ ViewReports (yazma FinanceWrite); gelen e-fatura FinanceWrite;
 * - satış okuma FinanceWrite ∨ ViewReports ∨ OperationsWrite (satış FinanceWrite).
 * Statik yollar (`detay-listesi`) `:id`'den önce.
 */
export const FINANCE_DOCUMENT_ROUTES: Routes = withTranslationBlock('finans-belge', [
  {
    path: 'faturalar',
    title: 'Faturalar — RentACar',
    canMatch: [anyPermissionGuard('FinanceWrite', 'ViewReports')],
    loadComponent: () => import('./invoices/invoice-list').then((m) => m.InvoiceList),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'faturalar/detay-listesi',
    title: 'Fatura Detay Listesi — RentACar',
    canMatch: [permissionGuard('ViewReports')],
    loadComponent: () => import('./invoices/invoice-lines').then((m) => m.InvoiceLines),
  },
  {
    // Fatura çıktısı: sunucunun PDF ucuna tam sayfa gezinme (Blazor InvoicePrint gibi); sekme açmaz.
    path: 'faturalar/:id/yazdir',
    title: 'Fatura yazdır — RentACar',
    data: { sekme: false },
    canMatch: [permissionGuard('ViewReports')],
    loadComponent: () => import('./invoices/invoice-print').then((m) => m.InvoicePrint),
  },
  {
    path: 'cezalar',
    title: 'Trafik Cezaları — RentACar',
    canMatch: [anyPermissionGuard('OperationsWrite', 'FinanceWrite', 'ViewReports')],
    loadComponent: () => import('./penalties/penalty-list').then((m) => m.PenaltyList),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'giderler',
    title: 'Giderler — RentACar',
    canMatch: [anyPermissionGuard('FinanceWrite', 'ViewReports')],
    loadComponent: () => import('./expenses/expense-list').then((m) => m.ExpenseList),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'gelen-efatura',
    title: 'Gelen e-Fatura — RentACar',
    canMatch: [permissionGuard('FinanceWrite')],
    loadComponent: () =>
      import('./incoming-invoices/incoming-invoice-list').then((m) => m.IncomingInvoiceList),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'satislar',
    title: 'Araç Satışları — RentACar',
    canMatch: [anyPermissionGuard('FinanceWrite', 'ViewReports', 'OperationsWrite')],
    loadComponent: () => import('./vehicle-sales/vehicle-sale-list').then((m) => m.VehicleSaleList),
    canDeactivate: [unsavedChangesGuard],
  },
]);
