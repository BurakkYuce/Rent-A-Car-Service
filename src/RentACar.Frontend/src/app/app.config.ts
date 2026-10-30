import { LocationStrategy } from '@angular/common';
import {
  ApplicationConfig,
  ErrorHandler,
  inject,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideRouter, RouteReuseStrategy, withNavigationErrorHandler } from '@angular/router';

import { provideApiClient } from '@core/api/api-client';
import { CONFIRM_PROMPT } from '@core/form/unsaved-changes';
import { cdkConfirmPrompt } from '@core/feedback/confirm-service';
import { RcErrorHandler } from '@core/errors/client-error';
import { provideTranslation } from '@core/i18n/translation';
import { SESSION_CONTEXT } from '@core/session/session-context';
import { sessionInterceptor } from '@core/session/session-interceptor';
import { SessionService } from '@core/session/session-service';
import { ChunkErrorService, isChunkLoadError } from '@core/version/chunk-error';
import { TabRouteStrategy } from '@core/tabs/tab-strategy';
import { provideTheme } from '@core/theme/theme';
import { provideTurkishLocale } from '@core/locale/tr-locale';

import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideRouter(
      routes,
      // Tembel sayfa parçası yüklenemedi (yayından sonra): hedef adrese kontrollü TEK yenileme.
      withNavigationErrorHandler((error) => {
        if (isChunkLoadError(error.error)) {
          inject(ChunkErrorService).isle(inject(LocationStrategy).prepareExternalUrl(error.url));
        }
      }),
    ),
    // Sekmeli çalışma alanı (F3.2): açık sekmenin sayfası başka sekmeye geçince yaşamaya devam eder.
    { provide: RouteReuseStrategy, useExisting: TabRouteStrategy },
    provideTurkishLocale(),
    ...provideTranslation(),
    provideTheme(),
    // HttpClient + XSRF (XSRF-TOKEN çerezi → X-XSRF-TOKEN başlığı) + kod bazlı oturum/geri bildirim (F3.3).
    provideApiClient(sessionInterceptor),
    { provide: SESSION_CONTEXT, useFactory: () => inject(SessionService).context },
    { provide: ErrorHandler, useClass: RcErrorHandler },
    // Kaydedilmemiş değişiklik sorusu (F3.6 guard'ı) tarayıcı confirm'ü yerine CDK onay diyaloğuyla.
    { provide: CONFIRM_PROMPT, useFactory: cdkConfirmPrompt },
  ],
};
