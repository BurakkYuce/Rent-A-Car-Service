import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { uniqueId } from '../alan/alan-baglami';
import { type Comparer, type SecenekOgesi, sameValue } from './secenek';
import { BaseControl, controlProviders } from './base-control';

/**
 * Radyo grubu (`role="radiogroup"`, yerel radyolar → ok tuşları tarayıcıdan). `rc-alan grup` içinde
 * kullanılır: grubun adı alanın etiketidir (`aria-labelledby`), ilk radyo alanın `for` kimliğini alır.
 */
@Component({
  selector: 'rc-radyo-grubu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: controlProviders(() => RadioGroup),
  template: `
    <div
      class="rc-secenek-grubu"
      role="radiogroup"
      [attr.aria-labelledby]="alan?.etiketKimligi ?? null"
      [attr.aria-label]="ariaEtiketi() ?? null"
      [attr.aria-invalid]="ariaInvalid()"
      [attr.aria-describedby]="ariaDescribedBy()"
      [attr.aria-required]="ariaRequired()"
    >
      @for (secenek of secenekler(); track $index) {
        <label class="rc-secenek">
          <input
            type="radio"
            [name]="name"
            [attr.id]="$first ? itemId() : null"
            [checked]="secili(secenek.deger)"
            [disabled]="pasif() || secenek.pasif"
            (change)="notify(secenek.deger)"
            (blur)="touch()"
          />
          <span>{{ secenek.etiket }}</span>
        </label>
      }
    </div>
  `,
})
export class RadioGroup<T> extends BaseControl<T> {
  readonly secenekler = input.required<readonly SecenekOgesi<T>[]>();
  readonly karsilastir = input<Comparer<T>>(sameValue);
  protected readonly name = uniqueId('rc-radyo');

  protected secili(value: T): boolean {
    const selected = this.deger();
    return selected !== null && this.karsilastir()(value, selected);
  }
}
