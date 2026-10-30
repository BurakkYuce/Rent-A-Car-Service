import { ChangeDetectionStrategy, Component, input, numberAttribute } from '@angular/core';
import { BaseControl, controlProviders } from './base-control';

/** Tek satır metin. Boş metin `null` olarak bildirilir (sunucuda "boş" ile "yok" ayrımı tek). */
@Component({
  selector: 'rc-metin-girdisi',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: controlProviders(() => TextInput),
  template: `
    <input
      class="rc-girdi"
      [type]="tur()"
      [id]="itemId()"
      [value]="deger() ?? ''"
      [disabled]="pasif()"
      [attr.placeholder]="yerTutucu() || null"
      [attr.maxlength]="azamiUzunluk() ?? null"
      [attr.autocomplete]="otomatikTamamlama()"
      [attr.inputmode]="girdiModu() ?? null"
      [attr.list]="liste() ?? null"
      [attr.aria-label]="ariaEtiketi() ?? null"
      [attr.aria-invalid]="ariaInvalid()"
      [attr.aria-describedby]="ariaDescribedBy()"
      [attr.aria-required]="ariaRequired()"
      (input)="written($event)"
      (blur)="touch()"
    />
  `,
})
export class TextInput extends BaseControl<string> {
  /** `password`: yeni hesap parolası (F12.2 firma oluşturma) — `otomatikTamamlama="new-password"` ile. */
  readonly tur = input<'text' | 'email' | 'tel' | 'search' | 'url' | 'password'>('text');
  readonly yerTutucu = input('');
  readonly azamiUzunluk = input<number | undefined, unknown>(undefined, {
    transform: (v: unknown) => (v === undefined || v === null ? undefined : numberAttribute(v)),
  });
  /** Tarayıcı otomatik doldurması; kişisel veri alanlarında (TC, telefon) `off` bırakılır. */
  readonly otomatikTamamlama = input('off');
  readonly girdiModu = input<string | undefined>(undefined);
  /** "Seç veya yaz" önerileri: sayfadaki `<datalist>`'in kimliği (serbest metin de kabul edilir). */
  readonly liste = input<string | undefined>(undefined);

  protected written(evt: Event): void {
    const text = (evt.target as HTMLInputElement).value;
    this.notify(text === '' ? null : text);
  }
}
