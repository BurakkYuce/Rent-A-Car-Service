import { LocationStrategy } from '@angular/common';
import {
  ApplicationConfig,
  ErrorHandler,
  inject,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideRouter, RouteReuseStrategy, withNavigationErrorHandler } from '@angular/router';

import { provideApiIstemcisi } from '@core/api/api-istemcisi';
import { ONAY_ISTEMI } from '@core/form/kaydedilmemis-degisiklik';
import { cdkOnayIstemi } from '@core/geri-bildirim/onay-servisi';
import { RcHataIsleyici } from '@core/hata/istemci-hata';
import { provideCeviri } from '@core/i18n/ceviri';
import { OTURUM_BAGLAMI } from '@core/oturum/oturum-baglami';
import { oturumInterceptor } from '@core/oturum/oturum-interceptor';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { ParcaHatasiServisi, parcaYuklemeHatasiMi } from '@core/surum/parca-hatasi';
import { SekmeRotaStratejisi } from '@core/sekme/sekme-stratejisi';
import { provideTema } from '@core/tema/tema';
import { provideTurkceYerel } from '@core/yerel/tr-yerel';

import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideRouter(
      routes,
      // Tembel sayfa parçası yüklenemedi (yayından sonra): hedef adrese kontrollü TEK yenileme.
      withNavigationErrorHandler((hata) => {
        if (parcaYuklemeHatasiMi(hata.error)) {
          inject(ParcaHatasiServisi).isle(inject(LocationStrategy).prepareExternalUrl(hata.url));
        }
      }),
    ),
    // Sekmeli çalışma alanı (F3.2): açık sekmenin sayfası başka sekmeye geçince yaşamaya devam eder.
    { provide: RouteReuseStrategy, useExisting: SekmeRotaStratejisi },
    provideTurkceYerel(),
    ...provideCeviri(),
    provideTema(),
    // HttpClient + XSRF (XSRF-TOKEN çerezi → X-XSRF-TOKEN başlığı) + kod bazlı oturum/geri bildirim (F3.3).
    provideApiIstemcisi(oturumInterceptor),
    { provide: OTURUM_BAGLAMI, useFactory: () => inject(OturumServisi).baglam },
    { provide: ErrorHandler, useClass: RcHataIsleyici },
    // Kaydedilmemiş değişiklik sorusu (F3.6 guard'ı) tarayıcı confirm'ü yerine CDK onay diyaloğuyla.
    { provide: ONAY_ISTEMI, useFactory: cdkOnayIstemi },
  ],
};
