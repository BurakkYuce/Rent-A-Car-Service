import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideApiIstemcisi } from '@core/api/api-istemcisi';
import { provideCeviri } from '@core/i18n/ceviri';
import { provideTema } from '@core/tema/tema';
import { provideTurkceYerel } from '@core/yerel/tr-yerel';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideRouter(routes),
    provideTurkceYerel(),
    ...provideCeviri(),
    provideTema(),
    // HttpClient + XSRF (XSRF-TOKEN çerezi → X-XSRF-TOKEN başlığı). F3.3 interceptor'larını buraya ekler.
    provideApiIstemcisi(),
  ],
};
