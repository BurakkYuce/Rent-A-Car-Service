import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideCeviri } from '@core/i18n/ceviri';
import { provideTema } from '@core/tema/tema';
import { provideTurkceYerel } from '@core/yerel/tr-yerel';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideTurkceYerel(),
    ...provideCeviri(),
    provideTema(),
  ],
};
