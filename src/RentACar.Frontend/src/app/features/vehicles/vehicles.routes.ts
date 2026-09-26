import type { Routes } from '@angular/router';

import { unsavedChangesGuard } from '@core/form/kaydedilmemis-degisiklik';
import { withTranslationBlock } from '@core/i18n/ceviri-blogu';
import { permissionGuard } from '@core/oturum/session-guard';

import { anyPermissionGuard } from './vehicle-guards';

/**
 * F6.2a araç ekranları (Blazor `/vehicles`, `/vehicles/detayli`, `/vehicles/{id}`, `/araclar/{id}`,
 * `/arac-durum`, `/arac-sahipleri`, `/segmentler`, `/arac-tipleri`). İzinler uçlarla aynı: okuma
 * OperationsWrite VEYA ViewReports, detaylı liste ViewReports, durum panosu ve tanımlar OperationsWrite.
 * `araclar/yeni` ve `araclar/detayli`, `araclar/:id`'den ÖNCE eşleşmeli.
 */
export const VEHICLE_ROUTES: Routes = withTranslationBlock('arac', [
  {
    path: 'araclar',
    title: 'Araçlar — RentACar',
    canMatch: [anyPermissionGuard('OperationsWrite', 'ViewReports')],
    loadComponent: () =>
      import('@features/vehicles/vehicle-list/vehicle-list').then((m) => m.VehicleList),
  },
  {
    path: 'araclar/detayli',
    title: 'Detaylı Araç Listesi — RentACar',
    canMatch: [permissionGuard('ViewReports')],
    loadComponent: () =>
      import('@features/vehicles/vehicle-detailed-list/vehicle-detailed-list').then(
        (m) => m.VehicleDetailedList,
      ),
  },
  {
    path: 'araclar/yeni',
    title: 'Yeni Araç — RentACar',
    canMatch: [permissionGuard('OperationsWrite')],
    loadComponent: () =>
      import('@features/vehicles/vehicle-form/vehicle-form').then((m) => m.VehicleForm),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'araclar/:id',
    title: 'Araç Kartı — RentACar',
    canMatch: [anyPermissionGuard('OperationsWrite', 'ViewReports')],
    loadComponent: () =>
      import('@features/vehicles/vehicle-form/vehicle-form').then((m) => m.VehicleForm),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'araclar/:id/detay',
    title: 'Araç Detayı — RentACar',
    canMatch: [anyPermissionGuard('OperationsWrite', 'ViewReports')],
    loadComponent: () =>
      import('@features/vehicles/vehicle-detail/vehicle-detail').then((m) => m.VehicleDetail),
  },
  {
    path: 'arac-durum',
    title: 'Araç Güncel Durum — RentACar',
    canMatch: [permissionGuard('OperationsWrite')],
    loadComponent: () =>
      import('@features/vehicles/vehicle-status-board/vehicle-status-board').then(
        (m) => m.VehicleStatusBoard,
      ),
  },
  ...(['sahip', 'segment', 'tip'] as const).map((definition) => ({
    path: { sahip: 'arac-sahipleri', segment: 'segmentler', tip: 'arac-tipleri' }[definition],
    title: {
      sahip: 'Araç Sahipleri — RentACar',
      segment: 'Araç Segmentleri — RentACar',
      tip: 'Araç Tipleri — RentACar',
    }[definition],
    data: { tanim: definition },
    canMatch: [permissionGuard('OperationsWrite')],
    loadComponent: () =>
      import('@features/vehicles/vehicle-definitions/vehicle-definitions').then(
        (m) => m.VehicleDefinitions,
      ),
    canDeactivate: [unsavedChangesGuard],
  })),
]);
