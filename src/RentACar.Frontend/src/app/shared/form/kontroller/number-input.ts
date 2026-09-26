import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  input,
  numberAttribute,
  signal,
} from '@angular/core';
import { invariantDecimal, formatDecimal, parseDecimal } from '@core/form/ondalik';
import { ParsingControl, controlProviders } from './base-control';

/**
 * Sayı girdisi (adet, km, gün). Türkçe yazım (`1.234`, `12,5`); değer `number`. Para İÇİN DEĞİL —
 * tutar `rc-para-girdisi` ile. Fazla kesir hanesi yuvarlanmaz, reddedilir (`sayiGecersiz`).
 */
@Component({
  selector: 'rc-sayi-girdisi',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: controlProviders(() => NumberInput, { dogrulayici: true }),
  template: `
    <input
      class="rc-girdi rc-girdi--sayi"
      autocomplete="off"
      [attr.inputmode]="kesir() > 0 ? 'decimal' : 'numeric'"
      [id]="itemId()"
      [value]="metin()"
      [disabled]="pasif()"
      [attr.placeholder]="yerTutucu() || null"
      [attr.aria-label]="ariaEtiketi() ?? null"
      [attr.aria-invalid]="ariaInvalid()"
      [attr.aria-describedby]="ariaDescribedBy()"
      [attr.aria-required]="ariaRequired()"
      (input)="written($event)"
      (blur)="released()"
    />
  `,
})
export class NumberInput extends ParsingControl<number> {
  readonly kesir = input(0, { transform: numberAttribute });
  readonly negatif = input(false, { transform: booleanAttribute });
  readonly yerTutucu = input('');

  protected readonly metin = signal('');

  protected override writtenExternally(value: number | null): void {
    this.metin.set(this.bicimle(value));
    this.setError(null);
  }

  protected written(evt: Event): void {
    const written = (evt.target as HTMLInputElement).value;
    this.metin.set(written);
    const resolution = parseDecimal(written, {
      kesir: this.kesir(),
      negatif: this.negatif(),
      fazlaHane: 'reddet',
    });
    this.setError(resolution.gecerli ? null : { sayiGecersiz: true });
    this.notify(resolution.gecerli && resolution.deger !== null ? Number(resolution.deger) : null);
  }

  protected released(): void {
    if (this.parseError() === null) this.metin.set(this.bicimle(this.deger()));
    this.touch();
  }

  private bicimle(value: number | null): string {
    const canonical = invariantDecimal(value, { kesir: this.kesir() });
    // Tam sayıda gereksiz ",0" yok; kesirliyse hane sayısı kadar.
    return formatDecimal(canonical, this.kesir());
  }
}
