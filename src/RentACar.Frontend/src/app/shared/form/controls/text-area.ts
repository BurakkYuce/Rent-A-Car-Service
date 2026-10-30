import { ChangeDetectionStrategy, Component, input, numberAttribute } from '@angular/core';
import { BaseControl, controlProviders } from './base-control';

/** Çok satırlı metin (açıklama, not). */
@Component({
  selector: 'rc-metin-alani',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: controlProviders(() => TextArea),
  template: `
    <textarea
      class="rc-girdi"
      [id]="itemId()"
      [value]="deger() ?? ''"
      [disabled]="pasif()"
      [rows]="satir()"
      [attr.placeholder]="yerTutucu() || null"
      [attr.maxlength]="azamiUzunluk() ?? null"
      [attr.aria-label]="ariaEtiketi() ?? null"
      [attr.aria-invalid]="ariaInvalid()"
      [attr.aria-describedby]="ariaDescribedBy()"
      [attr.aria-required]="ariaRequired()"
      (input)="written($event)"
      (blur)="touch()"
    ></textarea>
  `,
})
export class TextArea extends BaseControl<string> {
  readonly yerTutucu = input('');
  readonly satir = input(3, { transform: numberAttribute });
  readonly azamiUzunluk = input<number | undefined, unknown>(undefined, {
    transform: (v: unknown) => (v === undefined || v === null ? undefined : numberAttribute(v)),
  });

  protected written(evt: Event): void {
    const text = (evt.target as HTMLTextAreaElement).value;
    this.notify(text === '' ? null : text);
  }
}
