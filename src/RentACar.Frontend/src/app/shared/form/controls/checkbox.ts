import { ChangeDetectionStrategy, Component, booleanAttribute, input } from '@angular/core';
import { BaseControl, controlProviders } from './base-control';

/**
 * Onay kutusu (`boolean`). `anahtar` verilirse aç/kapa anahtarı görünümü (`role="switch"`),
 * davranış aynı. Metin kutunun yanında; `rc-alan` içinde de kullanılabilir.
 */
@Component({
  selector: 'rc-onay-kutusu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: controlProviders(() => Checkbox),
  template: `
    <label class="rc-secenek">
      <input
        type="checkbox"
        [class.rc-anahtar]="anahtar()"
        [attr.role]="anahtar() ? 'switch' : null"
        [id]="itemId()"
        [checked]="deger() === true"
        [disabled]="pasif()"
        [attr.aria-label]="ariaEtiketi() ?? null"
        [attr.aria-invalid]="ariaInvalid()"
        [attr.aria-describedby]="ariaDescribedBy()"
        [attr.aria-required]="ariaRequired()"
        (change)="toggled($event)"
        (blur)="touch()"
      />
      <span><ng-content /></span>
    </label>
  `,
})
export class Checkbox extends BaseControl<boolean> {
  readonly anahtar = input(false, { transform: booleanAttribute });

  protected toggled(evt: Event): void {
    this.notify((evt.target as HTMLInputElement).checked);
  }
}

/** Aç/kapa anahtarı: `rc-onay-kutusu anahtar` kısayolu, ayrı seçici. */
@Component({
  selector: 'rc-anahtar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: controlProviders(() => Anahtar),
  template: `
    <label class="rc-secenek">
      <input
        type="checkbox"
        role="switch"
        class="rc-anahtar"
        [id]="itemId()"
        [checked]="deger() === true"
        [disabled]="pasif()"
        [attr.aria-label]="ariaEtiketi() ?? null"
        [attr.aria-invalid]="ariaInvalid()"
        [attr.aria-describedby]="ariaDescribedBy()"
        (change)="toggled($event)"
        (blur)="touch()"
      />
      <span><ng-content /></span>
    </label>
  `,
})
export class Anahtar extends BaseControl<boolean> {
  protected toggled(evt: Event): void {
    this.notify((evt.target as HTMLInputElement).checked);
  }
}
