import type { Routes } from '@angular/router';
import { kaydedilmemisDegisiklikGuard } from '@core/form/kaydedilmemis-degisiklik';
import { izinGuard } from '@core/oturum/oturum-guard';

/**
 * Kira formu rotaları (F4.3) — `sayfalar.ts`'e tek satırla eklenir. TEK form bileşeni iki rotada
 * (Blazor `KiraForm.razor` gibi); sıra önemli: `kiralar/yeni` ve `kiralar/:id/yazdir`, `kiralar/:id`'den
 * önce. Form ve sekmeleri tembel parça (ilk pakete girmez).
 */
export const KIRA_FORMU_ROTALARI: Routes = [
  {
    // Sorgu sözleşmesi: ?varac=&vfrom=&vto=&vgrup=&musteriId= (Blazor müsaitlik/araç durumu bağlantıları).
    path: 'kiralar/yeni',
    title: 'Yeni Kira — RentACar',
    canMatch: [izinGuard('OperationsWrite')],
    canDeactivate: [kaydedilmemisDegisiklikGuard],
    loadComponent: () => import('./kira-formu').then((m) => m.KiraFormuSayfasi),
  },
  {
    // Sözleşme çıktısı: sunucunun PDF ucuna tam sayfa gezinme (Blazor RentalPrint ile aynı); sekme açmaz.
    path: 'kiralar/:id/yazdir',
    title: 'Sözleşme yazdır — RentACar',
    data: { sekme: false },
    loadComponent: () => import('./kira-yazdir').then((m) => m.KiraYazdir),
  },
  {
    // Okuma OperationsWrite ya da FinanceWrite (muhasebe tahsilat için açar) — kapı sunucuda.
    path: 'kiralar/:id',
    title: 'Kira — RentACar',
    canDeactivate: [kaydedilmemisDegisiklikGuard],
    loadComponent: () => import('./kira-formu').then((m) => m.KiraFormuSayfasi),
  },
];
