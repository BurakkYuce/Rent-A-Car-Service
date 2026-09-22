import { ChangeDetectionStrategy, Component, input, numberAttribute } from '@angular/core';
import { TemelKontrol, kontrolSaglayicilari } from './temel-kontrol';

/** Tek satır metin. Boş metin `null` olarak bildirilir (sunucuda "boş" ile "yok" ayrımı tek). */
@Component({
  selector: 'rc-metin-girdisi',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: kontrolSaglayicilari(() => MetinGirdisi),
  template: `
    <input
      class="rc-girdi"
      [type]="tur()"
      [id]="ogeKimligi()"
      [value]="deger() ?? ''"
      [disabled]="pasif()"
      [attr.placeholder]="yerTutucu() || null"
      [attr.maxlength]="azamiUzunluk() ?? null"
      [attr.autocomplete]="otomatikTamamlama()"
      [attr.inputmode]="girdiModu() ?? null"
      [attr.list]="liste() ?? null"
      [attr.aria-label]="ariaEtiketi() ?? null"
      [attr.aria-invalid]="ariaGecersiz()"
      [attr.aria-describedby]="ariaAciklayan()"
      [attr.aria-required]="ariaZorunlu()"
      (input)="yazildi($event)"
      (blur)="dokun()"
    />
  `,
})
export class MetinGirdisi extends TemelKontrol<string> {
  readonly tur = input<'text' | 'email' | 'tel' | 'search' | 'url'>('text');
  readonly yerTutucu = input('');
  readonly azamiUzunluk = input<number | undefined, unknown>(undefined, {
    transform: (v: unknown) => (v === undefined || v === null ? undefined : numberAttribute(v)),
  });
  /** Tarayıcı otomatik doldurması; kişisel veri alanlarında (TC, telefon) `off` bırakılır. */
  readonly otomatikTamamlama = input('off');
  readonly girdiModu = input<string | undefined>(undefined);
  /** "Seç veya yaz" önerileri: sayfadaki `<datalist>`'in kimliği (serbest metin de kabul edilir). */
  readonly liste = input<string | undefined>(undefined);

  protected yazildi(olay: Event): void {
    const metin = (olay.target as HTMLInputElement).value;
    this.bildir(metin === '' ? null : metin);
  }
}
