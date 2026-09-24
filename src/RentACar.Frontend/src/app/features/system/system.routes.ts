import type { Routes } from '@angular/router';

import { kaydedilmemisDegisiklikGuard } from '@core/form/kaydedilmemis-degisiklik';
import { ceviriBloguyla } from '@core/i18n/ceviri-blogu';
import { izinGuard } from '@core/oturum/oturum-guard';

/**
 * F11.2b sistem ve web sitesi ekranları (Blazor rotalarıyla aynı yollar). İzin kapıları uçlarla birebir:
 * - ManageUsers: kullanıcılar, ekran yetkileri, firma ayarları, mesaj şablonları, denetim;
 * - OperationsWrite: ofisler, ilanlar (+ web sitesi modülü, sayfada), site içeriği, blog, gelen talepler;
 * - yalnız oturum: bildirimler, genel arama, kendi parolası.
 */
export const SYSTEM_ROUTES: Routes = ceviriBloguyla('sistem', [
  {
    path: 'kullanicilar',
    title: 'Kullanıcılar — RentACar',
    canMatch: [izinGuard('ManageUsers')],
    loadComponent: () => import('./users/users-page').then((m) => m.UsersPage),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'yetki',
    title: 'Ekran Yetkileri — RentACar',
    canMatch: [izinGuard('ManageUsers')],
    loadComponent: () => import('./permissions/permissions-page').then((m) => m.PermissionsPage),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'ayarlar',
    title: 'Ayarlar — RentACar',
    canMatch: [izinGuard('ManageUsers')],
    loadComponent: () => import('./settings/settings-page').then((m) => m.SettingsPage),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'mesaj-sablonlari',
    title: 'Mesaj Şablonları — RentACar',
    canMatch: [izinGuard('ManageUsers')],
    loadComponent: () =>
      import('./messages/message-templates-page').then((m) => m.MessageTemplatesPage),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'denetim',
    title: 'Denetim — RentACar',
    canMatch: [izinGuard('ManageUsers')],
    loadComponent: () => import('./audit/audit-page').then((m) => m.AuditPage),
  },
  {
    path: 'lokasyonlar',
    title: 'Lokasyonlar — RentACar',
    canMatch: [izinGuard('OperationsWrite')],
    loadComponent: () => import('./locations/location-page').then((m) => m.LocationPage),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'web-sitesi',
    title: 'Web Sitesi — RentACar',
    canMatch: [izinGuard('OperationsWrite')],
    loadComponent: () => import('./website/website-hub').then((m) => m.WebsiteHub),
  },
  {
    path: 'web-sitesi/arac-ekle',
    title: 'İlan: Araç Ekle — RentACar',
    canMatch: [izinGuard('OperationsWrite')],
    loadComponent: () => import('./website/listing-create').then((m) => m.ListingCreate),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'web-sitesi/ilan/:id/fiyat',
    title: 'İlan Fiyatı — RentACar',
    canMatch: [izinGuard('OperationsWrite')],
    loadComponent: () => import('./website/listing-price').then((m) => m.ListingPrice),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'web-sitesi/ilan/:id/ozellikler',
    title: 'İlan Özellikleri — RentACar',
    canMatch: [izinGuard('OperationsWrite')],
    loadComponent: () => import('./website/listing-features').then((m) => m.ListingFeatures),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'site-icerik',
    title: 'Site İçeriği — RentACar',
    canMatch: [izinGuard('OperationsWrite')],
    loadComponent: () => import('./content/site-content-page').then((m) => m.SiteContentPage),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'blog-yonetim',
    title: 'Blog — RentACar',
    canMatch: [izinGuard('OperationsWrite')],
    loadComponent: () => import('./blog/blog-page').then((m) => m.BlogPage),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
  {
    path: 'blog-yonetim/:id/onizleme',
    title: 'Blog Önizleme — RentACar',
    canMatch: [izinGuard('OperationsWrite')],
    loadComponent: () => import('./blog/blog-preview-page').then((m) => m.BlogPreviewPage),
  },
  {
    path: 'gelen-talepler',
    title: 'Gelen Talepler — RentACar',
    canMatch: [izinGuard('OperationsWrite')],
    loadComponent: () =>
      import('./requests/booking-requests-page').then((m) => m.BookingRequestsPage),
    canDeactivate: [kaydedilmemisDegisiklikGuard],
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
    canDeactivate: [kaydedilmemisDegisiklikGuard],
  },
]);
