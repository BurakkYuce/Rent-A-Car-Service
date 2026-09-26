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
import { ONYUKLU_CEVIRI_BLOKLARI } from './onyuklu-ceviri';

/**
 * i18n (Transloco). Bugün yalnız Türkçe var; anahtarlar ileride başka dil eklenebilecek şekilde
 * `src/i18n/<dil>.json`'da. Revlo HTTP ile yüklüyordu; burada ÇEKİRDEK `tr.json` (kabuk/core/shared
 * metinleri) pakete gömülür: ilk çizimde anahtar yanıp sönmez, ek istek yok, CSP `connect-src`
 * etkilenmez. Özellik metinleri `src/i18n/bloklar/<blok>.json`'da; rota parçasıyla tembel yüklenir ve
 * rota çözülmeden birleştirilir (`ceviriBlogu`, `ceviri-blogu.ts`). Yeni dil eklenirse `CEVIRILER`'e
 * dinamik `import()` ile (ayrı parça) girer.
 */
export const DILLER = ['tr'] as const;
export type Dil = (typeof DILLER)[number];
export const DEFAULT_LANGUAGE: Dil = 'tr';

const TRANSLATIONS: Readonly<Record<Dil, Translation>> = { tr };

@Injectable({ providedIn: 'root' })
export class EmbeddedTranslationLoader implements TranslocoLoader {
  private readonly onyuklu = inject(ONYUKLU_CEVIRI_BLOKLARI);

  getTranslation(dil: string): Observable<Translation> {
    const translation = (TRANSLATIONS as Readonly<Record<string, Translation | undefined>>)[dil];
    if (!translation) return throwError(() => new Error(`Çeviri dosyası yok: ${dil}`));
    return of(
      this.onyuklu.reduce<Translation>((total, block) => ({ ...total, ...block }), translation),
    );
  }
}

/** Eksik anahtar geliştirmede bağırır, üretimde anahtarın kendisi görünür (boş metin yerine). */
export class MissingTranslationHandler implements TranslocoMissingHandler {
  handle(key: string): string {
    return isDevMode() ? `EKSİK: ${key}` : key;
  }
}

export function provideTranslation(): EnvironmentProviders[] {
  return [
    ...provideTransloco({
      config: {
        availableLangs: [...DILLER],
        defaultLang: DEFAULT_LANGUAGE,
        fallbackLang: DEFAULT_LANGUAGE,
        reRenderOnLangChange: false,
        prodMode: !isDevMode(),
        missingHandler: { logMissingKey: isDevMode(), useFallbackTranslation: false },
      },
      loader: EmbeddedTranslationLoader,
    }),
    provideTranslocoMissingHandler(MissingTranslationHandler),
    // TS tarafındaki `translate()` çağrıları da ilk çizimden önce hazır metni bulsun.
    provideAppInitializer(() => firstValueFrom(inject(TranslocoService).load(DEFAULT_LANGUAGE))),
  ];
}

/**
 * TS tarafında tipli çeviri: anahtar `tr.json`'da ya da bir blokta yoksa derleme hatası. Enjeksiyon bağlamında
 * çağrılır. Blok anahtarı yalnız o bloğu yükleyen rotanın altında çözülür (yüklenmemişse eksik anahtar davranışı).
 */
export function translationFunction(): (
  key: CeviriAnahtari,
  parameters?: Record<string, unknown>,
) => string {
  const transloco = inject(TranslocoService);
  return (key, parameters) => transloco.translate(key, parameters);
}
