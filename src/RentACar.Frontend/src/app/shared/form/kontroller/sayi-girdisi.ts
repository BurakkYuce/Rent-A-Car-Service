import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  input,
  numberAttribute,
  signal,
} from '@angular/core';
import { invariantOndalik, ondalikBicimle, ondalikCoz } from '@core/form/ondalik';
import { AyristiranKontrol, kontrolSaglayicilari } from './temel-kontrol';

/**
 * Sayı girdisi (adet, km, gün). Türkçe yazım (`1.234`, `12,5`); değer `number`. Para İÇİN DEĞİL —
 * tutar `rc-para-girdisi` ile. Fazla kesir hanesi yuvarlanmaz, reddedilir (`sayiGecersiz`).
 */
@Component({
  selector: 'rc-sayi-girdisi',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: kontrolSaglayicilari(() => SayiGirdisi, { dogrulayici: true }),
  template: `
    <input
      class="rc-girdi rc-girdi--sayi"
      autocomplete="off"
      [attr.inputmode]="kesir() > 0 ? 'decimal' : 'numeric'"
      [id]="ogeKimligi()"
      [value]="metin()"
      [disabled]="pasif()"
      [attr.placeholder]="yerTutucu() || null"
      [attr.aria-label]="ariaEtiketi() ?? null"
      [attr.aria-invalid]="ariaGecersiz()"
      [attr.aria-describedby]="ariaAciklayan()"
      [attr.aria-required]="ariaZorunlu()"
      (input)="yazildi($event)"
      (blur)="birakildi()"
    />
  `,
})
export class SayiGirdisi extends AyristiranKontrol<number> {
  readonly kesir = input(0, { transform: numberAttribute });
  readonly negatif = input(false, { transform: booleanAttribute });
  readonly yerTutucu = input('');

  protected readonly metin = signal('');

  protected override disaridanYazildi(deger: number | null): void {
    this.metin.set(this.bicimle(deger));
    this.hataAyarla(null);
  }

  protected yazildi(olay: Event): void {
    const yazilan = (olay.target as HTMLInputElement).value;
    this.metin.set(yazilan);
    const cozum = ondalikCoz(yazilan, {
      kesir: this.kesir(),
      negatif: this.negatif(),
      fazlaHane: 'reddet',
    });
    this.hataAyarla(cozum.gecerli ? null : { sayiGecersiz: true });
    this.bildir(cozum.gecerli && cozum.deger !== null ? Number(cozum.deger) : null);
  }

  protected birakildi(): void {
    if (this.ayristirmaHatasi() === null) this.metin.set(this.bicimle(this.deger()));
    this.dokun();
  }

  private bicimle(deger: number | null): string {
    const kanonik = invariantOndalik(deger, { kesir: this.kesir() });
    // Tam sayıda gereksiz ",0" yok; kesirliyse hane sayısı kadar.
    return ondalikBicimle(kanonik, this.kesir());
  }
}
