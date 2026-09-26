import type { Routes } from '@angular/router';

import { unsavedChangesGuard } from '@core/form/kaydedilmemis-degisiklik';
import { withTranslationBlock } from '@core/i18n/ceviri-blogu';
import { permissionGuard } from '@core/oturum/session-guard';
import { anyPermissionGuard } from '@features/vehicles/vehicle-guards';

const catalog = () => import('@features/pricing/catalog/catalog-page').then((m) => m.CatalogPage);

/** Tanım ekranı rotası (ortak bileşen, rota verisi `catalog`). */
function catalogRoute(path: string, title: string, guard = permissionGuard('OperationsWrite')) {
  return {
    path,
    title: `${title} — RentACar`,
    canMatch: [guard],
    data: { catalog: path },
    loadComponent: catalog,
    canDeactivate: [unsavedChangesGuard],
  };
}

/**
 * F9.2 fiyat / tarife ekranları — Blazor yollarıyla AYNI (kesişte yönlendirme birebir). İzinler uçlarla aynı:
 * - tanımlar (tarifeler, tarife grupları, sigorta ürünleri, ek hizmetler, tarife matrisi, kira kuralları, broker
 *   yasakları) OperationsWrite; servis tanımları okuması OperationsWrite ∨ FinanceWrite ∨ ViewReports (yazma OW);
 * - fiyat hesapla OperationsWrite ∨ ViewReports;
 * - maliyet hesapla FinanceWrite; kayıtlı teklifler FinanceWrite ∨ ViewReports (kaydet/sil FinanceWrite);
 * - tarife aktar ManageUsers.
 */
export const PRICING_ROUTES: Routes = withTranslationBlock('fiyat-tarife', [
  catalogRoute('tarifeler', 'Tarifeler'),
  catalogRoute('tarife-gruplari', 'Tarife Grupları'),
  catalogRoute('sigorta-urunleri', 'Sigorta Ürünleri'),
  catalogRoute('ek-hizmetler', 'Ek Hizmetler'),
  catalogRoute('tarife-matris', 'Tarife Matrisi'),
  catalogRoute('kira-kurallari', 'Kiralama Kuralları'),
  catalogRoute('broker-yasaklari', 'Broker Yasakları'),
  catalogRoute(
    'servis-tanimlari',
    'Servis Tanımları',
    anyPermissionGuard('OperationsWrite', 'FinanceWrite', 'ViewReports'),
  ),
  {
    path: 'fiyat-hesapla',
    title: 'Fiyat Hesapla — RentACar',
    canMatch: [anyPermissionGuard('OperationsWrite', 'ViewReports')],
    loadComponent: () =>
      import('@features/pricing/quote/quote-calculator').then((m) => m.QuoteCalculator),
  },
  {
    path: 'maliyet-hesapla',
    title: 'Maliyet Hesapla — RentACar',
    canMatch: [permissionGuard('FinanceWrite')],
    loadComponent: () =>
      import('@features/pricing/cost/cost-calculator').then((m) => m.CostCalculator),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'maliyet-teklifleri',
    title: 'Maliyet Teklifleri — RentACar',
    canMatch: [anyPermissionGuard('FinanceWrite', 'ViewReports')],
    loadComponent: () => import('@features/pricing/cost/offer-list').then((m) => m.OfferList),
  },
  {
    path: 'maliyet-teklifleri/:id',
    title: 'Maliyet Teklifi — RentACar',
    canMatch: [anyPermissionGuard('FinanceWrite', 'ViewReports')],
    loadComponent: () =>
      import('@features/pricing/cost/cost-calculator').then((m) => m.CostCalculator),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'tarife-aktar',
    title: 'Tarife İçe Aktar — RentACar',
    canMatch: [permissionGuard('ManageUsers')],
    loadComponent: () => import('@features/pricing/import/rate-import').then((m) => m.RateImport),
  },
]);
