import { registerLocaleData } from '@angular/common';
import localeTr from '@angular/common/locales/tr';
import {
  DEFAULT_CURRENCY_CODE,
  EnvironmentProviders,
  LOCALE_ID,
  makeEnvironmentProviders,
} from '@angular/core';

/** Uygulamanın tek yerel ayarı. Sayı/tarih biçimi, çoğul kuralları ve ay adları bundan gelir. */
export const LOCALE = 'tr';

/** Türkiye 2016'dan beri sabit UTC+3 (yaz saati yok). Anlık zamanlar İstanbul saatiyle gösterilir. */
export const ISTANBUL_OFFSET = '+0300';

registerLocaleData(localeTr, LOCALE);

/** `LOCALE_ID = 'tr'` ve varsayılan para birimi TRY. `app.config.ts` bunu kullanır. */
export function provideTurkishLocale(): EnvironmentProviders {
  return makeEnvironmentProviders([
    { provide: LOCALE_ID, useValue: LOCALE },
    { provide: DEFAULT_CURRENCY_CODE, useValue: 'TRY' },
  ]);
}
