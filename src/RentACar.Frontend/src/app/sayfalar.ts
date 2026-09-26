import type { Routes } from '@angular/router';

import { unsavedChangesGuard } from '@core/form/kaydedilmemis-degisiklik';
import { ceviriBlogu, withTranslationBlock } from '@core/i18n/ceviri-blogu';
import { permissionGuard } from '@core/oturum/session-guard';
import { FINANCE_ROUTES } from '@features/finance/finance.routes';
import { CRM_ROUTES } from '@features/crm/crm.routes';
import { CUSTOMER_ROUTES } from '@features/customers/customers.routes';
import { DEFINITION_ROUTES } from '@features/definitions/definitions.routes';
import { FINANCE_DOCUMENT_ROUTES } from '@features/finance-documents/finance-documents.routes';
import { RENTAL_FORM_ROUTES } from '@features/kira-formu/kira-formu.routes';
import { PRICING_ROUTES } from '@features/pricing/pricing.routes';
import { REPORT_ROUTES } from '@features/reports/reports.routes';
import { RESERVATION_ROUTES } from '@features/rezervasyonlar/rezervasyonlar.routes';
import { SYSTEM_ROUTES } from '@features/system/system.routes';
import { SERVICE_INSURANCE_ROUTES } from '@features/service-insurance/service-insurance.routes';
import { VEHICLE_FINANCE_ROUTES } from '@features/vehicle-finance/vehicle-finance.routes';
import { VEHICLE_ROUTES } from '@features/vehicles/vehicles.routes';

/**
 * Kabuk içindeki sayfalar (oturum şart; `canMatch: [oturumGuard]` kabuk rotasında). Hepsi tembel.
 *
 * Kurallar (F3.2):
 * - `title` zorunlu (`'<Ad> — RentACar'`): belge başlığı ve SEKME etiketi buradan gelir.
 * - Her sayfa bir sekmedir: başka sekmeye geçilince bileşen yaşamaya devam eder. Kayıt sayfası
 *   `:id` parametresiyle (`kiralar/:id`) — farklı kayıt ayrı sekme; tarayıcıda yalnız desen + id
 *   saklanır. `:id` dışında yol parametresi olan sayfa sekme olur ama yenilemede geri gelmez.
 * - Tek seferlik akış `data: { sekme: false }` ile sekme dışı kalır.
 * - Kirli form: `canDeactivate: [kaydedilmemisDegisiklikGuard]` (sekme KAPATILIRKEN sorar).
 * - İzin: `canMatch: [izinGuard('FinanceWrite')]`.
 * - Çeviri: özellik metinleri ilk pakette değil; sayfa kendi bloğunu yükler: `canActivate: [ceviriBlogu('panel')]`
 *   ya da özellik rota dizisi `ceviriBloguyla('<blok>', [...])` (AGENTS.md "i18n").
 */
export const PAGES: Routes = [
  {
    path: '',
    pathMatch: 'full',
    title: 'Yeni arayüz — RentACar',
    loadComponent: () => import('@features/yer-tutucu/placeholder').then((m) => m.Placeholder),
    canActivate: [ceviriBlogu('vitrin')],
  },
  {
    // F4.5 Panel (Blazor `/`). Kök yerine `/panel`: kök F3 vitrin/e2e/görsel tabanlarının çapası. F4.6: pilot
    // girişinin varsayılan inişi (`PILOT_INIS`), menü "Panel" kaydı (`/app/panel`, sahip spa) ve Blazor `/`'ın
    // pilot yönlendirmesi bu rota. İzin kapısı içerikte (sunucu).
    path: 'panel',
    title: 'Panel — RentACar',
    loadComponent: () => import('@features/panel/panel-page').then((m) => m.PanelPage),
    canActivate: [ceviriBlogu('panel')],
  },
  {
    // F4.2 kira listesi. Kayıt formu (`kiralar/yeni`, `kiralar/:id`) F4.3'te; liste yalnız bağlantı verir.
    path: 'kiralar',
    title: 'Kira listesi — RentACar',
    loadComponent: () =>
      import('@features/kiralar/kira-listesi/kira-listesi').then((m) => m.RentalList),
    canActivate: [ceviriBlogu('kiralar')],
  },
  // F5.2b planlama ekranları (Blazor /takvim, /musaitlik, /rez-sartlari, /filo-kiralama). Kayıt sayfası
  // `filo-kiralama/:id`; `yeni` ondan ÖNCE eşleşmeli. F5.3 parite: Blazor sayfalarının dördü de
  // `[Authorize(Policy = "izin:OperationsWrite")]`, uç grupları da OperationsWrite ister — izinsiz rol
  // (ör. Muhasebe) sayfayı açıp API 403'ü görmek yerine uyarı bandıyla ana sayfaya döner.
  ...withTranslationBlock('planlama', [
    {
      path: 'takvim',
      title: 'Rezervasyon Takvimi — RentACar',
      canMatch: [permissionGuard('OperationsWrite')],
      loadComponent: () => import('@features/takvim/calendar-page').then((m) => m.CalendarPage),
    },
    {
      path: 'musaitlik',
      title: 'Müsait Araç Ara — RentACar',
      canMatch: [permissionGuard('OperationsWrite')],
      loadComponent: () =>
        import('@features/musaitlik/availability-page').then((m) => m.AvailabilityPage),
    },
    {
      path: 'rez-sartlari',
      title: 'Rez Şartları — RentACar',
      canMatch: [permissionGuard('OperationsWrite')],
      loadComponent: () =>
        import('@features/rez-sartlari/reservation-terms').then((m) => m.ReservationTerms),
      canDeactivate: [unsavedChangesGuard],
    },
    {
      path: 'filo-kiralama',
      title: 'Filo Kiralama — RentACar',
      canMatch: [permissionGuard('OperationsWrite')],
      loadComponent: () => import('@features/filo-kiralama/fleet-list').then((m) => m.FleetList),
    },
    {
      path: 'filo-kiralama/yeni',
      title: 'Yeni Filo Sözleşmesi — RentACar',
      canMatch: [permissionGuard('OperationsWrite')],
      loadComponent: () => import('@features/filo-kiralama/fleet-new').then((m) => m.FleetNew),
      canDeactivate: [unsavedChangesGuard],
    },
    {
      path: 'filo-kiralama/:id',
      title: 'Filo Sözleşmesi — RentACar',
      canMatch: [permissionGuard('OperationsWrite')],
      loadComponent: () =>
        import('@features/filo-kiralama/fleet-detail').then((m) => m.FleetDetail),
      canDeactivate: [unsavedChangesGuard],
    },
  ]),
  ...withTranslationBlock('vitrin', [
    {
      // F3.7 vitrin dizini: çekirdeğin her parçasına bağlantı (e2e: axe iki tema, 320–1440 taşma, görsel).
      path: 'vitrin',
      title: 'Vitrin — RentACar',
      loadComponent: () =>
        import('@features/vitrin/vitrin-dizini/showcase-index').then((m) => m.ShowcaseIndex),
    },
    {
      path: 'vitrin/tokenlar',
      title: 'Token vitrini — RentACar',
      loadComponent: () =>
        import('@features/vitrin/tokenlar-vitrini/tokens-showcase').then((m) => m.TokensShowcase),
    },
    {
      path: 'vitrin/primitifler',
      title: 'Primitif vitrini — RentACar',
      loadComponent: () =>
        import('@features/vitrin/primitif-vitrini/primitive-showcase').then(
          (m) => m.PrimitiveShowcase,
        ),
    },
    {
      path: 'vitrin/kabuk',
      title: 'Kabuk vitrini — RentACar',
      loadComponent: () =>
        import('@features/vitrin/kabuk-vitrini/shell-showcase').then((m) => m.ShellShowcase),
    },
    {
      // F3.3 oturum/geri bildirim vitrini (F3.7 vitrininin parçası); e2e bunun üstünde koşar.
      path: 'vitrin/geri-bildirim',
      title: 'Geri bildirim vitrini — RentACar',
      loadComponent: () =>
        import('@features/vitrin/feedback-showcase').then((m) => m.FeedbackShowcase),
    },
    {
      // F3.6 form seti vitrini (F3.7 vitrininin parçası); e2e bunun üstünde koşar.
      path: 'vitrin/form',
      title: 'Form vitrini — RentACar',
      loadComponent: () =>
        import('@features/vitrin/form-vitrini/form-showcase').then((m) => m.FormShowcase),
      canDeactivate: [unsavedChangesGuard],
    },
    {
      path: 'vitrin/tanim',
      title: 'Tanım vitrini — RentACar',
      loadComponent: () =>
        import('@features/vitrin/tanim-vitrini/definition-showcase').then(
          (m) => m.DefinitionShowcase,
        ),
      canDeactivate: [unsavedChangesGuard],
    },
    {
      // F3.5 tablo motoru vitrini: TanStack table-core + virtual-core yalnız bu parçaya girer.
      path: 'vitrin/tablo',
      title: 'Tablo vitrini — RentACar',
      loadComponent: () =>
        import('@features/vitrin/tablo-vitrini/table-showcase').then((m) => m.TableShowcase),
    },
  ]),
  // F4.3 kira formu: /kiralar/yeni, /kiralar/:id, /kiralar/:id/yazdir.
  ...RENTAL_FORM_ROUTES,
  // F5.2a rezervasyonlar + teklifler: liste, /yeni, /:id (teklif kaydı salt okunur + eylemler).
  ...RESERVATION_ROUTES,
  // F6.2a araçlar: liste, detaylı liste, kart (yeni/:id + foto), detay, durum panosu, tanımlar.
  ...VEHICLE_ROUTES,
  // F6.2b araç finans: kredi (+ taksit ödeme), müşteri taksit, sipariş, BAF, hasar, filo plan.
  ...VEHICLE_FINANCE_ROUTES,
  // F8.2b finans belgeleri: faturalar (+ detay listesi, yazdır), cezalar, giderler, gelen e-fatura, araç satışları.
  ...FINANCE_DOCUMENT_ROUTES,
  // F8.2a finans (PARA): kasa hub, nakit işlem, bakiye düzeltme, cari virman, toplu işlemler, depozito, cari ekstre,
  // otomatik tahsilat, dönem kapanışı, kurlar.
  ...FINANCE_ROUTES,
  // F7.2 cariler: liste, kart (yeni/:id), 360° detay (+ ekstre sekmesi).
  ...CUSTOMER_ROUTES,
  // F7.2 CRM: anket, şikayet, assistans, hukuk, CRM analiz.
  ...CRM_ROUTES,
  // F9.2 servis / sigorta / vade: servisler (+ kayıt), sigorta, MTV, muayene (+ kayıtlar, ödeme), vade panosu.
  ...SERVICE_INSURANCE_ROUTES,
  // F9.2 fiyat / tarife: 8 tanım ekranı, fiyat hesapla, maliyet hesapla + teklifler, tarife aktar.
  ...PRICING_ROUTES,
  // F10.2 raporlar: /raporlar/<kod> (tek ortak rapor ekranı; araç karnesi /raporlar/arac-karne/:id).
  ...REPORT_ROUTES,
  // F11.2a tanımlar (1. yarı): genel tanım CRUD'u + şubeler, doluluk kuralları, dokümanlar, takvim aboneliği.
  ...DEFINITION_ROUTES,
  // F11.2b sistem + web sitesi: kullanıcılar, yetki, ayarlar, mesaj şablonları, denetim, ofisler, ilanlar, site
  // içeriği, blog, gelen talepler, bildirimler, arama, parola.
  ...SYSTEM_ROUTES,
];
