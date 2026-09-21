import {
  EnvironmentProviders,
  inject,
  Injectable,
  isDevMode,
  provideAppInitializer,
} from '@angular/core';
import {
  provideTransloco,
  provideTranslocoMissingHandler,
  Translation,
  TranslocoLoader,
  TranslocoMissingHandler,
  TranslocoService,
} from '@jsverse/transloco';
import { firstValueFrom, Observable, of, throwError } from 'rxjs';
import tr from '../../../i18n/tr.json';
import type { CeviriAnahtari } from './ceviri-anahtarlari';

/**
 * i18n (Transloco). Bugün yalnız Türkçe var; anahtarlar ileride başka dil eklenebilecek şekilde
 * `src/i18n/<dil>.json`'da. Revlo HTTP ile yüklüyordu; burada `tr.json` pakete gömülür:
 * ilk çizimde anahtar yanıp sönmez, ek istek yok, CSP `connect-src` etkilenmez. Yeni dil eklenirse
 * `CEVIRILER`'e dinamik `import()` ile (ayrı parça) girer.
 */
export const DILLER = ['tr'] as const;
export type Dil = (typeof DILLER)[number];
export const VARSAYILAN_DIL: Dil = 'tr';

const CEVIRILER: Readonly<Record<Dil, Translation>> = { tr };

@Injectable({ providedIn: 'root' })
export class GomuluCeviriYukleyici implements TranslocoLoader {
  getTranslation(dil: string): Observable<Translation> {
    const ceviri = (CEVIRILER as Readonly<Record<string, Translation | undefined>>)[dil];
    return ceviri ? of(ceviri) : throwError(() => new Error(`Çeviri dosyası yok: ${dil}`));
  }
}

/** Eksik anahtar geliştirmede bağırır, üretimde anahtarın kendisi görünür (boş metin yerine). */
export class EksikCeviriIsleyici implements TranslocoMissingHandler {
  handle(anahtar: string): string {
    return isDevMode() ? `EKSİK: ${anahtar}` : anahtar;
  }
}

export function provideCeviri(): EnvironmentProviders[] {
  return [
    ...provideTransloco({
      config: {
        availableLangs: [...DILLER],
        defaultLang: VARSAYILAN_DIL,
        fallbackLang: VARSAYILAN_DIL,
        reRenderOnLangChange: false,
        prodMode: !isDevMode(),
        missingHandler: { logMissingKey: isDevMode(), useFallbackTranslation: false },
      },
      loader: GomuluCeviriYukleyici,
    }),
    provideTranslocoMissingHandler(EksikCeviriIsleyici),
    // TS tarafındaki `translate()` çağrıları da ilk çizimden önce hazır metni bulsun.
    provideAppInitializer(() => firstValueFrom(inject(TranslocoService).load(VARSAYILAN_DIL))),
  ];
}

/** TS tarafında tipli çeviri: anahtar `tr.json`'da yoksa derleme hatası. Enjeksiyon bağlamında çağrılır. */
export function ceviriFonksiyonu(): (
  anahtar: CeviriAnahtari,
  parametreler?: Record<string, unknown>,
) => string {
  const transloco = inject(TranslocoService);
  return (anahtar, parametreler) => transloco.translate(anahtar, parametreler);
}
