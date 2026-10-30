import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { uniqueId } from '@shared/form/field/field-context';
import { BaseControl, controlProviders } from '@shared/form/controls/base-control';

import type { SitePaletteOption } from './settings-model';

/**
 * Site palet seti seçimi: renk çipli radyo kartları (`role="radiogroup"`, yerel radyolar → ok tuşları tarayıcıdan).
 * `rc-alan grup` içinde kullanılır. Değer set anahtarı; `null` = temanın varsayılan (ilk) seti — kartta seçili
 * görünür. Çipler süs (`aria-hidden`): erişilebilir ad setin adıdır. Renkler sunucu kataloğundan (`#rrggbb`) gelir ve
 * yalnız CSSOM üzerinden (`[style.background-color]`) basılır.
 */
@Component({
  selector: 'rc-site-palet-secici',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe],
  providers: controlProviders(() => SitePalettePicker),
  styles: `
    .paletler {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(min(100%, 14rem), 1fr));
      gap: var(--rc-bosluk-2);
    }

    .palet {
      display: flex;
      gap: var(--rc-bosluk-2);
      align-items: center;
      min-height: var(--rc-kontrol-yukseklik-buyuk);
      padding: var(--rc-bosluk-2);
      border: 1px solid var(--rc-kenar);
      border-radius: var(--rc-yaricap-md);
      background-color: var(--rc-yuzey);
      cursor: pointer;

      &:has(input:checked) {
        border-color: var(--rc-vurgu);
        background-color: var(--rc-yuzey-vurgu);
      }

      &:has(input:focus-visible) {
        outline: var(--rc-odak-kalinlik) solid var(--rc-odak);
        outline-offset: var(--rc-odak-bosluk);
      }

      &:has(input:disabled) {
        color: var(--rc-metin-pasif);
        cursor: not-allowed;
      }

      > input {
        margin: 0;
        accent-color: var(--rc-vurgu);
      }
    }

    .cipler {
      display: inline-flex;
      flex: none;
    }

    .cip {
      width: var(--rc-bosluk-4);
      height: var(--rc-bosluk-6);
      border: 1px solid var(--rc-kenar-kontrol);

      &:first-child {
        border-radius: var(--rc-yaricap-sm) 0 0 var(--rc-yaricap-sm);
      }

      &:last-child {
        border-radius: 0 var(--rc-yaricap-sm) var(--rc-yaricap-sm) 0;
      }

      & + .cip {
        border-inline-start: 0;
      }
    }

    .ad {
      min-width: 0;
      font-size: var(--rc-yazi-sm);
    }

    .varsayilan {
      display: block;
      color: var(--rc-metin-ikincil);
      font-size: var(--rc-yazi-xs);
    }
  `,
  template: `
    <div
      class="paletler"
      role="radiogroup"
      [attr.aria-labelledby]="alan?.etiketKimligi ?? null"
      [attr.aria-label]="ariaEtiketi() ?? null"
      [attr.aria-invalid]="ariaInvalid()"
      [attr.aria-describedby]="ariaDescribedBy()"
      [attr.aria-required]="ariaRequired()"
    >
      @for (p of paletler(); track p.anahtar) {
        <label class="palet" [attr.data-palet]="p.anahtar">
          <input
            type="radio"
            [name]="name"
            [attr.id]="$first ? itemId() : null"
            [checked]="isSelected(p.anahtar, $first)"
            [disabled]="pasif()"
            (change)="notify(p.anahtar)"
            (blur)="touch()"
          />
          <span class="cipler" aria-hidden="true">
            <span class="cip" [style.background-color]="p.zemin"></span>
            <span class="cip" [style.background-color]="p.murekkep"></span>
            <span class="cip" [style.background-color]="p.ana"></span>
            @if (p.vurgu) {
              <span class="cip" [style.background-color]="p.vurgu"></span>
            }
            <span class="cip" [style.background-color]="p.geceZemin"></span>
          </span>
          <span class="ad"
            >{{ p.ad }}
            @if ($first) {
              <span class="varsayilan">{{ 'sistem.ayarlar.palet.varsayilan' | transloco }}</span>
            }
          </span>
        </label>
      }
    </div>
  `,
})
export class SitePalettePicker extends BaseControl<string> {
  readonly paletler = input.required<readonly SitePaletteOption[]>();
  protected readonly name = uniqueId('rc-palet');

  /** Seçili: değer bu set, ya da değer yokken (temanın varsayılanı) ilk set. */
  protected isSelected(key: string, first: boolean): boolean {
    const value = this.deger();
    return value === key || ((value === null || value === '') && first);
  }
}
