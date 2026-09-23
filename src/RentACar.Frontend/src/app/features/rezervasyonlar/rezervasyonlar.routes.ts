import type { Routes } from '@angular/router';
import { kaydedilmemisDegisiklikGuard } from '@core/form/kaydedilmemis-degisiklik';
import { ceviriBloguyla } from '@core/i18n/ceviri-blogu';
import { izinGuard } from '@core/oturum/oturum-guard';

/**
 * F5.2a rezervasyon ve teklif rotaları — `sayfalar.ts`'e tek satırla eklenir. Hepsi tembel parça; metinler
 * `rezervasyon` çeviri bloğunda (rota yükler). Uçların tamamı OperationsWrite ister (Blazor grubu + menü kaydı):
 * rota kapısı aynı izni ister, asıl kapı sunucuda. Sıra önemli: `…/yeni`, `…/:id`'den önce.
 */
export const REZERVASYON_ROTALARI: Routes = ceviriBloguyla('rezervasyon', [
  {
    path: 'rezervasyonlar',
    title: 'Rezervasyonlar — RentACar',
    canMatch: [izinGuard('OperationsWrite')],
    loadComponent: () =>
      import('./rezervasyon-listesi/rezervasyon-listesi').then((m) => m.RezervasyonListesi),
  },
  {
    path: 'rezervasyonlar/yeni',
    title: 'Yeni Rezervasyon — RentACar',
    canMatch: [izinGuard('OperationsWrite')],
    canDeactivate: [kaydedilmemisDegisiklikGuard],
    loadComponent: () =>
      import('./rezervasyon-formu/rezervasyon-formu').then((m) => m.RezervasyonFormuSayfasi),
  },
  {
    path: 'rezervasyonlar/:id',
    title: 'Rezervasyon — RentACar',
    canMatch: [izinGuard('OperationsWrite')],
    canDeactivate: [kaydedilmemisDegisiklikGuard],
    loadComponent: () =>
      import('./rezervasyon-formu/rezervasyon-formu').then((m) => m.RezervasyonFormuSayfasi),
  },
  {
    path: 'teklifler',
    title: 'Teklifler — RentACar',
    canMatch: [izinGuard('OperationsWrite')],
    loadComponent: () =>
      import('../teklifler/teklif-listesi/teklif-listesi').then((m) => m.TeklifListesi),
  },
  {
    path: 'teklifler/yeni',
    title: 'Yeni Teklif — RentACar',
    canMatch: [izinGuard('OperationsWrite')],
    canDeactivate: [kaydedilmemisDegisiklikGuard],
    loadComponent: () =>
      import('../teklifler/teklif-formu/teklif-formu').then((m) => m.TeklifFormuSayfasi),
  },
  {
    path: 'teklifler/:id',
    title: 'Teklif — RentACar',
    canMatch: [izinGuard('OperationsWrite')],
    loadComponent: () =>
      import('../teklifler/teklif-detayi/teklif-detayi').then((m) => m.TeklifDetayi),
  },
]);
