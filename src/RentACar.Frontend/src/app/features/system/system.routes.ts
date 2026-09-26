import type { Routes } from '@angular/router';

import { unsavedChangesGuard } from '@core/form/kaydedilmemis-degisiklik';
import { withTranslationBlock } from '@core/i18n/ceviri-blogu';
import { permissionGuard } from '@core/oturum/session-guard';

/**
 * F11.2b sistem ve web sitesi ekranları (Blazor rotalarıyla aynı yollar). İzin kapıları uçlarla birebir:
 * - ManageUsers: kullanıcılar, ekran yetkileri, firma ayarları, mesaj şablonları, denetim;
 * - OperationsWrite: ofisler, ilanlar (+ web sitesi modülü, sayfada), site içeriği, blog, gelen talepler;
 * - yalnız oturum: bildirimler, genel arama, kendi parolası.
 */
export const SYSTEM_ROUTES: Routes = withTranslationBlock('sistem', [
  {
    path: 'kullanicilar',
    title: 'Kullanıcılar — RentACar',
    canMatch: [permissionGuard('ManageUsers')],
    loadComponent: () => import('./users/users-page').then((m) => m.UsersPage),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'yetki',
    title: 'Ekran Yetkileri — RentACar',
    canMatch: [permissionGuard('ManageUsers')],
    loadComponent: () => import('./permissions/permissions-page').then((m) => m.PermissionsPage),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'ayarlar',
    title: 'Ayarlar — RentACar',
    canMatch: [permissionGuard('ManageUsers')],
    loadComponent: () => import('./settings/settings-page').then((m) => m.SettingsPage),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'mesaj-sablonlari',
    title: 'Mesaj Şablonları — RentACar',
    canMatch: [permissionGuard('ManageUsers')],
    loadComponent: () =>
      import('./messages/message-templates-page').then((m) => m.MessageTemplatesPage),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'denetim',
    title: 'Denetim — RentACar',
    canMatch: [permissionGuard('ManageUsers')],
    loadComponent: () => import('./audit/audit-page').then((m) => m.AuditPage),
  },
  {
    path: 'lokasyonlar',
    title: 'Lokasyonlar — RentACar',
    canMatch: [permissionGuard('OperationsWrite')],
    loadComponent: () => import('./locations/location-page').then((m) => m.LocationPage),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'web-sitesi',
    title: 'Web Sitesi — RentACar',
    canMatch: [permissionGuard('OperationsWrite')],
    loadComponent: () => import('./website/website-hub').then((m) => m.WebsiteHub),
  },
  {
    path: 'web-sitesi/arac-ekle',
    title: 'İlan: Araç Ekle — RentACar',
    canMatch: [permissionGuard('OperationsWrite')],
    loadComponent: () => import('./website/listing-create').then((m) => m.ListingCreate),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'web-sitesi/ilan/:id/fiyat',
    title: 'İlan Fiyatı — RentACar',
    canMatch: [permissionGuard('OperationsWrite')],
    loadComponent: () => import('./website/listing-price').then((m) => m.ListingPrice),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'web-sitesi/ilan/:id/ozellikler',
    title: 'İlan Özellikleri — RentACar',
    canMatch: [permissionGuard('OperationsWrite')],
    loadComponent: () => import('./website/listing-features').then((m) => m.ListingFeatures),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'site-icerik',
    title: 'Site İçeriği — RentACar',
    canMatch: [permissionGuard('OperationsWrite')],
    loadComponent: () => import('./content/site-content-page').then((m) => m.SiteContentPage),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'blog-yonetim',
    title: 'Blog — RentACar',
    canMatch: [permissionGuard('OperationsWrite')],
    loadComponent: () => import('./blog/blog-page').then((m) => m.BlogPage),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'blog-yonetim/:id/onizleme',
    title: 'Blog Önizleme — RentACar',
    canMatch: [permissionGuard('OperationsWrite')],
    loadComponent: () => import('./blog/blog-preview-page').then((m) => m.BlogPreviewPage),
  },
  {
    path: 'gelen-talepler',
    title: 'Gelen Talepler — RentACar',
    canMatch: [permissionGuard('OperationsWrite')],
    loadComponent: () =>
      import('./requests/booking-requests-page').then((m) => m.BookingRequestsPage),
    canDeactivate: [unsavedChangesGuard],
  },
  {
    path: 'bildirimler',
    title: 'Bildirimler — RentACar',
    loadComponent: () =>
      import('./notifications/notifications-page').then((m) => m.NotificationsPage),
  },
  {
    path: 'ara',
    title: 'Arama — RentACar',
    loadComponent: () => import('./search/search-page').then((m) => m.SearchPage),
  },
  {
    path: 'profil/sifre-degistir',
    title: 'Parola Değiştir — RentACar',
    loadComponent: () => import('./profile/password-page').then((m) => m.PasswordPage),
    canDeactivate: [unsavedChangesGuard],
  },
]);
