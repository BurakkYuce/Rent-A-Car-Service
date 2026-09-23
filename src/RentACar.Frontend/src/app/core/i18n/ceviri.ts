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
export const VARSAYILAN_DIL: Dil = 'tr';

const CEVIRILER: Readonly<Record<Dil, Translation>> = { tr };

@Injectable({ providedIn: 'root' })
export class GomuluCeviriYukleyici implements TranslocoLoader {
  private readonly onyuklu = inject(ONYUKLU_CEVIRI_BLOKLARI);

  getTranslation(dil: string): Observable<Translation> {
    const ceviri = (CEVIRILER as Readonly<Record<string, Translation | undefined>>)[dil];
    if (!ceviri) return throwError(() => new Error(`Çeviri dosyası yok: ${dil}`));
    return of(this.onyuklu.reduce<Translation>((toplam, blok) => ({ ...toplam, ...blok }), ceviri));
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

/**
 * TS tarafında tipli çeviri: anahtar `tr.json`'da ya da bir blokta yoksa derleme hatası. Enjeksiyon bağlamında
 * çağrılır. Blok anahtarı yalnız o bloğu yükleyen rotanın altında çözülür (yüklenmemişse eksik anahtar davranışı).
 */
export function ceviriFonksiyonu(): (
  anahtar: CeviriAnahtari,
  parametreler?: Record<string, unknown>,
) => string {
  const transloco = inject(TranslocoService);
  return (anahtar, parametreler) => transloco.translate(anahtar, parametreler);
}
