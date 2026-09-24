import type { Routes } from '@angular/router';

import { kaydedilmemisDegisiklikGuard } from '@core/form/kaydedilmemis-degisiklik';
import { ceviriBloguyla } from '@core/i18n/ceviri-blogu';
import { izinGuard } from '@core/oturum/oturum-guard';
import { anyPermissionGuard } from '@features/vehicles/vehicle-guards';

const catalog = () => import('@features/pricing/catalog/catalog-page').then((m) => m.CatalogPage);

/** Tanım ekranı rotası (ortak bileşen, rota verisi `catalog`). */
function catalogRoute(path: string, title: string, guard = izinGuard('OperationsWrite')) {
  return {
    path,
    title: `${title} — RentACar`,
    canMatch: [guard],
    data: { catalog: path },
    loadComponent: catalog,
    canDeactivate: [kaydedilmemisDegisiklikGuard],
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
export const PRICING_ROUTES: Routes = ceviriBloguyla('fiyat-tarife', [
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
    canMatch: [izinGuard('FinanceWrite')],
    loadComponent: () =>
      import('@features/pricing/cost/cost-calculator').then((m) => m.CostCalculator),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
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
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'tarife-aktar',
    title: 'Tarife İçe Aktar — RentACar',
    canMatch: [izinGuard('ManageUsers')],
    loadComponent: () => import('@features/pricing/import/rate-import').then((m) => m.RateImport),
  },
]);
