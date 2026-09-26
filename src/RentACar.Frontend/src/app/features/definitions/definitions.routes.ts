import type { Routes } from '@angular/router';

import { unsavedChangesGuard } from '@core/form/kaydedilmemis-degisiklik';
import { withTranslationBlock } from '@core/i18n/ceviri-blogu';
import { permissionGuard } from '@core/oturum/session-guard';

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
export const DEFINITION_ROUTES: Routes = withTranslationBlock('tanimlar', [
  ...(Object.keys(DEFINITION_PATHS) as DefinitionKind[]).map((definition) => ({
    path: DEFINITION_PATHS[definition],
    title: `${TITLES[definition]} — RentACar`,
    data: { definition },
    canMatch: [permissionGuard(DEFINITION_PERMISSION[definition] ?? 'OperationsWrite')],
    loadComponent: () => import('./definition-page').then((m) => m.DefinitionPage),
    canDeactivate: [unsavedChangesGuard],
  })),
  // F11.2c: tanım CRUD'u + ekrana özel işlem (eşleşmeyen grup ataması / oranları yansıt).
  {
    path: 'arac-gruplari',
    title: 'Araç Grupları — RentACar',
    canMatch: [permissionGuard('OperationsWrite')],
    loadComponent: () =>
      import('./vehicle-groups/vehicle-group-page').then((m) => m.VehicleGroupPage),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'rezervasyon-kaynaklari',
    title: 'Rezervasyon Kaynakları — RentACar',
    canMatch: [permissionGuard('OperationsWrite')],
    loadComponent: () =>
      import('./reservation-sources/reservation-source-page').then((m) => m.ReservationSourcePage),
    canDeactivate: [unsavedChangesGuard],
  },
  // F11.2d: KVKK ekranları — uçlar gibi yalnız ManageUsers.
  {
    path: 'personel',
    title: 'Personel — RentACar',
    canMatch: [permissionGuard('ManageUsers')],
    loadComponent: () => import('./personnel/personnel-page').then((m) => m.PersonnelPage),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'ice-aktar',
    title: 'Veri İçe Aktar — RentACar',
    canMatch: [permissionGuard('ManageUsers')],
    loadComponent: () => import('./import/import-page').then((m) => m.ImportPage),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'subeler',
    title: 'Şube Tanımları — RentACar',
    canMatch: [permissionGuard('ManageUsers')],
    loadComponent: () => import('./branches/branch-page').then((m) => m.BranchPage),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'doluluk-kurallari',
    title: 'Doluluk Fiyat Kuralları — RentACar',
    canMatch: [permissionGuard('OperationsWrite')],
    loadComponent: () => import('./occupancy/occupancy-page').then((m) => m.OccupancyPage),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'dokumanlar',
    title: 'Dokümanlar — RentACar',
    loadComponent: () => import('./documents/documents-page').then((m) => m.DocumentsPage),
    canDeactivate: [unsavedChangesGuard],
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
