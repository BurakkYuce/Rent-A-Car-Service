import type { Routes } from '@angular/router';
import { unsavedChangesGuard } from '@core/form/kaydedilmemis-degisiklik';
import { withTranslationBlock } from '@core/i18n/ceviri-blogu';
import { permissionGuard } from '@core/oturum/session-guard';

/**
 * Kira formu rotaları (F4.3) — `sayfalar.ts`'e tek satırla eklenir. TEK form bileşeni iki rotada
 * (Blazor `KiraForm.razor` gibi); sıra önemli: `kiralar/yeni` ve `kiralar/:id/yazdir`, `kiralar/:id`'den
 * önce. Form ve sekmeleri tembel parça (ilk pakete girmez); metinleri `kira-formu` çeviri bloğunda (rota yükler).
 */
export const RENTAL_FORM_ROUTES: Routes = withTranslationBlock('kira-formu', [
  {
    // Sorgu sözleşmesi: ?varac=&vfrom=&vto=&vgrup=&musteriId= (Blazor müsaitlik/araç durumu bağlantıları).
    path: 'kiralar/yeni',
    title: 'Yeni Kira — RentACar',
    canMatch: [permissionGuard('OperationsWrite')],
    canDeactivate: [unsavedChangesGuard],
    loadComponent: () => import('./kira-formu').then((m) => m.RentalFormPage),
  },
  {
    // Sözleşme çıktısı: sunucunun PDF ucuna tam sayfa gezinme (Blazor RentalPrint ile aynı); sekme açmaz.
    path: 'kiralar/:id/yazdir',
    title: 'Sözleşme yazdır — RentACar',
    data: { sekme: false },
    loadComponent: () => import('./kira-yazdir').then((m) => m.RentalPrint),
  },
  {
    // Okuma OperationsWrite ya da FinanceWrite (muhasebe tahsilat için açar) — kapı sunucuda.
    path: 'kiralar/:id',
    title: 'Kira — RentACar',
    canDeactivate: [unsavedChangesGuard],
    loadComponent: () => import('./kira-formu').then((m) => m.RentalFormPage),
  },
]);
