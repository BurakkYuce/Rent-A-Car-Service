import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';

import { provideApiIstemcisi } from '@core/api/api-istemcisi';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    // HttpClient + XSRF (XSRF-TOKEN çerezi → X-XSRF-TOKEN başlığı). F3.3 interceptor'larını buraya ekler.
    provideApiIstemcisi(),
  ],
};
