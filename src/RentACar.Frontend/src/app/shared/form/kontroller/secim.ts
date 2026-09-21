import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { type Karsilastirici, type SecenekOgesi, ayniDeger } from './secenek';
import { TemelKontrol, kontrolSaglayicilari } from './temel-kontrol';

/**
 * Kısa, sabit listeler için yerel `<select>` (klavye, mobil seçici ve ekran okuyucu tarayıcıdan).
 * Uzun ya da sunucudan aranan listeler için `rc-arama-secim`. Seçenek değeri herhangi bir tip;
 * DOM'da sıra numarasıyla taşınır.
 */
@Component({
  selector: 'rc-secim',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe],
  providers: kontrolSaglayicilari(() => Secim),
  template: `
    <select
      class="rc-girdi"
      [id]="ogeKimligi()"
      [disabled]="pasif()"
      [attr.aria-label]="ariaEtiketi() ?? null"
      [attr.aria-invalid]="ariaGecersiz()"
      [attr.aria-describedby]="ariaAciklayan()"
      [attr.aria-required]="ariaZorunlu()"
      (change)="secildi($event)"
      (blur)="dokun()"
    >
      <option value="" [selected]="seciliSira() === -1">
        {{ bosEtiket() || ('form.secim.seciniz' | transloco) }}
      </option>
      @for (secenek of secenekler(); track $index) {
        <option [value]="$index" [selected]="seciliSira() === $index" [disabled]="secenek.pasif">
          {{ secenek.etiket }}
        </option>
      }
    </select>
  `,
})
export class Secim<T> extends TemelKontrol<T> {
  readonly secenekler = input.required<readonly SecenekOgesi<T>[]>();
  readonly bosEtiket = input('');
  readonly karsilastir = input<Karsilastirici<T>>(ayniDeger);

  protected readonly seciliSira = computed(() => {
    const deger = this.deger();
    if (deger === null) return -1;
    const esit = this.karsilastir();
    return this.secenekler().findIndex((s) => esit(s.deger, deger));
  });

  protected secildi(olay: Event): void {
    const ham = (olay.target as HTMLSelectElement).value;
    this.bildir(ham === '' ? null : (this.secenekler()[Number(ham)]?.deger ?? null));
  }
}
