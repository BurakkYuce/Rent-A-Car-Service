import type { Routes } from '@angular/router';

import { kaydedilmemisDegisiklikGuard } from '@core/form/kaydedilmemis-degisiklik';
import { ceviriBloguyla } from '@core/i18n/ceviri-blogu';
import { izinGuard } from '@core/oturum/oturum-guard';

import { DEFINITION_PATHS, DEFINITION_PERMISSION, type DefinitionKind } from './definition-paths';

/** Sekme/belge başlıkları (Blazor `PageTitle` paritesi). */
const TITLES: Readonly<Record<DefinitionKind, string>> = {
  brand: 'Marka Tanımları',
  cancelReason: 'İptal Sebebi Tanımları',
  country: 'Ülke Tanımları',
  customerGroup: 'Müşteri Grubu Tanımları',
  department: 'Departman Tanımları',
  accessory: 'Aksesuar Tanımları',
  bank: 'Banka Tanımları',
  currency: 'Döviz Tanımları',
  customCode: 'Özel Kod Tanımları',
  expenseCategory: 'Gider Türü Tanımları',
  account: 'Kasa/Banka Hesapları',
  drop: 'Drop Matris',
  paymentType: 'Ödeme Tipleri',
  fuelKind: 'Yakıt Türleri',
  transmissionType: 'Vites Türleri',
  vehicleColor: 'Renkler',
  accountCode: 'Hesap Kodları',
  insuranceCompany: 'Sigorta Şirketleri',
  vatRate: 'KDV Oranları',
  penaltyType: 'Ceza Türleri',
  documentTemplate: 'Belge Şablonları',
};

/**
 * F11.2a tanım ekranları (1. yarı; Blazor rotalarıyla aynı yollar). İzin kapıları uçlarla birebir: genel
 * tanımlar, drop ve doluluk OperationsWrite; şubeler ManageUsers; dokümanlar, firma belgeleri ve takvim
 * aboneliği yalnız oturum (yükle/sil düğmeleri sayfada OperationsWrite'la gizlenir, uç ayrıca ister).
 */
export const DEFINITION_ROUTES: Routes = ceviriBloguyla('tanimlar', [
  ...(Object.keys(DEFINITION_PATHS) as DefinitionKind[]).map((definition) => ({
    path: DEFINITION_PATHS[definition],
    title: `${TITLES[definition]} — RentACar`,
    data: { definition },
    canMatch: [izinGuard(DEFINITION_PERMISSION[definition] ?? 'OperationsWrite')],
    loadComponent: () => import('./definition-page').then((m) => m.DefinitionPage),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  })),
  // F11.2c: tanım CRUD'u + ekrana özel işlem (eşleşmeyen grup ataması / oranları yansıt).
  {
    path: 'arac-gruplari',
    title: 'Araç Grupları — RentACar',
    canMatch: [izinGuard('OperationsWrite')],
    loadComponent: () =>
      import('./vehicle-groups/vehicle-group-page').then((m) => m.VehicleGroupPage),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'rezervasyon-kaynaklari',
    title: 'Rezervasyon Kaynakları — RentACar',
    canMatch: [izinGuard('OperationsWrite')],
    loadComponent: () =>
      import('./reservation-sources/reservation-source-page').then((m) => m.ReservationSourcePage),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  // F11.2d: KVKK ekranları — uçlar gibi yalnız ManageUsers.
  {
    path: 'personel',
    title: 'Personel — RentACar',
    canMatch: [izinGuard('ManageUsers')],
    loadComponent: () => import('./personnel/personnel-page').then((m) => m.PersonnelPage),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'ice-aktar',
    title: 'Veri İçe Aktar — RentACar',
    canMatch: [izinGuard('ManageUsers')],
    loadComponent: () => import('./import/import-page').then((m) => m.ImportPage),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'subeler',
    title: 'Şube Tanımları — RentACar',
    canMatch: [izinGuard('ManageUsers')],
    loadComponent: () => import('./branches/branch-page').then((m) => m.BranchPage),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'doluluk-kurallari',
    title: 'Doluluk Fiyat Kuralları — RentACar',
    canMatch: [izinGuard('OperationsWrite')],
    loadComponent: () => import('./occupancy/occupancy-page').then((m) => m.OccupancyPage),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'dokumanlar',
    title: 'Dokümanlar — RentACar',
    loadComponent: () => import('./documents/documents-page').then((m) => m.DocumentsPage),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'firma-belgeleri',
    title: 'Firma Belgeleri — RentACar',
    loadComponent: () =>
      import('./documents/platform-documents-page').then((m) => m.PlatformDocumentsPage),
  },
  {
    path: 'takvim-abonelik',
    title: 'Takvim Aboneliği — RentACar',
    loadComponent: () =>
      import('./calendar/calendar-subscription-page').then((m) => m.CalendarSubscriptionPage),
  },
]);
