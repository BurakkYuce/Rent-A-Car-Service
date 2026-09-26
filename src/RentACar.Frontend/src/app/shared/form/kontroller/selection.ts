import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { type Comparer, type SecenekOgesi, sameValue } from './secenek';
import { BaseControl, controlProviders } from './base-control';

/**
 * Kısa, sabit listeler için yerel `<select>` (klavye, mobil seçici ve ekran okuyucu tarayıcıdan).
 * Uzun ya da sunucudan aranan listeler için `rc-arama-secim`. Seçenek değeri herhangi bir tip;
 * DOM'da sıra numarasıyla taşınır.
 */
@Component({
  selector: 'rc-secim',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe],
  providers: controlProviders(() => Selection),
  template: `
    <select
      class="rc-girdi"
      [id]="itemId()"
      [disabled]="pasif()"
      [attr.aria-label]="ariaEtiketi() ?? null"
      [attr.aria-invalid]="ariaInvalid()"
      [attr.aria-describedby]="ariaDescribedBy()"
      [attr.aria-required]="ariaRequired()"
      (change)="selected($event)"
      (blur)="touch()"
    >
      <option value="" [selected]="selectedIndex() === -1">
        {{ bosEtiket() || ('form.secim.seciniz' | transloco) }}
      </option>
      @for (secenek of secenekler(); track $index) {
        <option [value]="$index" [selected]="selectedIndex() === $index" [disabled]="secenek.pasif">
          {{ secenek.etiket }}
        </option>
      }
    </select>
  `,
})
export class Selection<T> extends BaseControl<T> {
  readonly secenekler = input.required<readonly SecenekOgesi<T>[]>();
  readonly bosEtiket = input('');
  readonly karsilastir = input<Comparer<T>>(sameValue);

  protected readonly selectedIndex = computed(() => {
    const value = this.deger();
    if (value === null) return -1;
    const equal = this.karsilastir();
    return this.secenekler().findIndex((s) => equal(s.deger, value));
  });

  protected selected(evt: Event): void {
    const raw = (evt.target as HTMLSelectElement).value;
    this.notify(raw === '' ? null : (this.secenekler()[Number(raw)]?.deger ?? null));
  }
}
