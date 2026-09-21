import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { tekilKimlik } from '../alan/alan-baglami';
import { type Karsilastirici, type SecenekOgesi, ayniDeger } from './secenek';
import { TemelKontrol, kontrolSaglayicilari } from './temel-kontrol';

/**
 * Radyo grubu (`role="radiogroup"`, yerel radyolar → ok tuşları tarayıcıdan). `rc-alan grup` içinde
 * kullanılır: grubun adı alanın etiketidir (`aria-labelledby`), ilk radyo alanın `for` kimliğini alır.
 */
@Component({
  selector: 'rc-radyo-grubu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: kontrolSaglayicilari(() => RadyoGrubu),
  template: `
    <div
      class="rc-secenek-grubu"
      role="radiogroup"
      [attr.aria-labelledby]="alan?.etiketKimligi ?? null"
      [attr.aria-label]="ariaEtiketi() ?? null"
      [attr.aria-invalid]="ariaGecersiz()"
      [attr.aria-describedby]="ariaAciklayan()"
      [attr.aria-required]="ariaZorunlu()"
    >
      @for (secenek of secenekler(); track $index) {
        <label class="rc-secenek">
          <input
            type="radio"
            [name]="ad"
            [attr.id]="$first ? ogeKimligi() : null"
            [checked]="secili(secenek.deger)"
            [disabled]="pasif() || secenek.pasif"
            (change)="bildir(secenek.deger)"
            (blur)="dokun()"
          />
          <span>{{ secenek.etiket }}</span>
        </label>
      }
    </div>
  `,
})
export class RadyoGrubu<T> extends TemelKontrol<T> {
  readonly secenekler = input.required<readonly SecenekOgesi<T>[]>();
  readonly karsilastir = input<Karsilastirici<T>>(ayniDeger);
  protected readonly ad = tekilKimlik('rc-radyo');

  protected secili(deger: T): boolean {
    const secili = this.deger();
    return secili !== null && this.karsilastir()(deger, secili);
  }
}
