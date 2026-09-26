import { Directive, TemplateRef, inject, input } from '@angular/core';

import type { TabloSutunu } from './tablo-modeli';

/** Hücre şablonunun bağlamı: `let-satir` (= `$implicit`) ve `let-deger="deger"`. */
export interface TabloHucreBaglami<T> {
  readonly $implicit: T;
  readonly deger: unknown;
}

/**
 * Özel hücre çizimi (rozet, bağlantı, düğme). Şablonsuz sütunlar `tur`'a göre biçimlenir.
 *
 * ```html
 * <rc-tablo …>
 *   <ng-template rcTabloHucre="durum" [rcTabloHucreSutunlar]="sutunlar" let-arac>
 *     <span class="rc-rozet">{{ arac.durum }}</span>
 *   </ng-template>
 * </rc-tablo>
 * ```
 * `rcTabloHucreSutunlar` (tabloya verilen sütun dizisi) tip çıkarımı içindir: `let-arac` satır
 * tipini alır. Verilmezse satır `unknown` olur.
 */
@Directive({ selector: 'ng-template[rcTabloHucre]' })
export class TableCell<T> {
  /** Sütun kodu. */
  readonly kod = input.required<string>({ alias: 'rcTabloHucre' });
  readonly rcTabloHucreSutunlar = input<readonly TabloSutunu<T>[] | undefined>(undefined);
  readonly sablon = inject<TemplateRef<TabloHucreBaglami<T>>>(TemplateRef);

  static ngTemplateContextGuard<T>(
    _directive: TableCell<T>,
    context: unknown,
  ): context is TabloHucreBaglami<T> {
    return typeof context === 'object' && context !== null;
  }
}
