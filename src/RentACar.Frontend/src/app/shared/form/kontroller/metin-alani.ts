import { ChangeDetectionStrategy, Component, input, numberAttribute } from '@angular/core';
import { TemelKontrol, kontrolSaglayicilari } from './temel-kontrol';

/** Çok satırlı metin (açıklama, not). */
@Component({
  selector: 'rc-metin-alani',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: kontrolSaglayicilari(() => MetinAlani),
  template: `
    <textarea
      class="rc-girdi"
      [id]="ogeKimligi()"
      [value]="deger() ?? ''"
      [disabled]="pasif()"
      [rows]="satir()"
      [attr.placeholder]="yerTutucu() || null"
      [attr.maxlength]="azamiUzunluk() ?? null"
      [attr.aria-label]="ariaEtiketi() ?? null"
      [attr.aria-invalid]="ariaGecersiz()"
      [attr.aria-describedby]="ariaAciklayan()"
      [attr.aria-required]="ariaZorunlu()"
      (input)="yazildi($event)"
      (blur)="dokun()"
    ></textarea>
  `,
})
export class MetinAlani extends TemelKontrol<string> {
  readonly yerTutucu = input('');
  readonly satir = input(3, { transform: numberAttribute });
  readonly azamiUzunluk = input<number | undefined, unknown>(undefined, {
    transform: (v: unknown) => (v === undefined || v === null ? undefined : numberAttribute(v)),
  });

  protected yazildi(olay: Event): void {
    const metin = (olay.target as HTMLTextAreaElement).value;
    this.bildir(metin === '' ? null : metin);
  }
}
