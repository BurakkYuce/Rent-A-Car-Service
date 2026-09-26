import type { Routes } from '@angular/router';
import { unsavedChangesGuard } from '@core/form/kaydedilmemis-degisiklik';
import { withTranslationBlock } from '@core/i18n/ceviri-blogu';
import { permissionGuard } from '@core/oturum/session-guard';

/**
 * F5.2a rezervasyon ve teklif rotaları — `sayfalar.ts`'e tek satırla eklenir. Hepsi tembel parça; metinler
 * `rezervasyon` çeviri bloğunda (rota yükler). Uçların tamamı OperationsWrite ister (Blazor grubu + menü kaydı):
 * rota kapısı aynı izni ister, asıl kapı sunucuda. Sıra önemli: `…/yeni`, `…/:id`'den önce.
 */
export const RESERVATION_ROUTES: Routes = withTranslationBlock('rezervasyon', [
  {
    path: 'rezervasyonlar',
    title: 'Rezervasyonlar — RentACar',
    canMatch: [permissionGuard('OperationsWrite')],
    loadComponent: () =>
      import('./rezervasyon-listesi/reservation-list').then((m) => m.ReservationList),
  },
  {
    path: 'rezervasyonlar/yeni',
    title: 'Yeni Rezervasyon — RentACar',
    canMatch: [permissionGuard('OperationsWrite')],
    canDeactivate: [unsavedChangesGuard],
    loadComponent: () =>
      import('./rezervasyon-formu/rezervasyon-formu').then((m) => m.ReservationFormPage),
  },
  {
    path: 'rezervasyonlar/:id',
    title: 'Rezervasyon — RentACar',
    canMatch: [permissionGuard('OperationsWrite')],
    canDeactivate: [unsavedChangesGuard],
    loadComponent: () =>
      import('./rezervasyon-formu/rezervasyon-formu').then((m) => m.ReservationFormPage),
  },
  {
    path: 'teklifler',
    title: 'Teklifler — RentACar',
    canMatch: [permissionGuard('OperationsWrite')],
    loadComponent: () =>
      import('../teklifler/teklif-listesi/quotation-list').then((m) => m.QuotationList),
  },
  {
    path: 'teklifler/yeni',
    title: 'Yeni Teklif — RentACar',
    canMatch: [permissionGuard('OperationsWrite')],
    canDeactivate: [unsavedChangesGuard],
    loadComponent: () =>
      import('../teklifler/teklif-formu/teklif-formu').then((m) => m.QuotationFormPage),
  },
  {
    path: 'teklifler/:id',
    title: 'Teklif — RentACar',
    canMatch: [permissionGuard('OperationsWrite')],
    loadComponent: () =>
      import('../teklifler/teklif-detayi/quotation-detail').then((m) => m.QuotationDetail),
  },
]);
