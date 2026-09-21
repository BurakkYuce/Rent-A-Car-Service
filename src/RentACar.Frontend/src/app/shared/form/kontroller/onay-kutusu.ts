import { ChangeDetectionStrategy, Component, booleanAttribute, input } from '@angular/core';
import { TemelKontrol, kontrolSaglayicilari } from './temel-kontrol';

/**
 * Onay kutusu (`boolean`). `anahtar` verilirse aç/kapa anahtarı görünümü (`role="switch"`),
 * davranış aynı. Metin kutunun yanında; `rc-alan` içinde de kullanılabilir.
 */
@Component({
  selector: 'rc-onay-kutusu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: kontrolSaglayicilari(() => OnayKutusu),
  template: `
    <label class="rc-secenek">
      <input
        type="checkbox"
        [class.rc-anahtar]="anahtar()"
        [attr.role]="anahtar() ? 'switch' : null"
        [id]="ogeKimligi()"
        [checked]="deger() === true"
        [disabled]="pasif()"
        [attr.aria-label]="ariaEtiketi() ?? null"
        [attr.aria-invalid]="ariaGecersiz()"
        [attr.aria-describedby]="ariaAciklayan()"
        [attr.aria-required]="ariaZorunlu()"
        (change)="isaretlendi($event)"
        (blur)="dokun()"
      />
      <span><ng-content /></span>
    </label>
  `,
})
export class OnayKutusu extends TemelKontrol<boolean> {
  readonly anahtar = input(false, { transform: booleanAttribute });

  protected isaretlendi(olay: Event): void {
    this.bildir((olay.target as HTMLInputElement).checked);
  }
}

/** Aç/kapa anahtarı: `rc-onay-kutusu anahtar` kısayolu, ayrı seçici. */
@Component({
  selector: 'rc-anahtar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: kontrolSaglayicilari(() => Anahtar),
  template: `
    <label class="rc-secenek">
      <input
        type="checkbox"
        role="switch"
        class="rc-anahtar"
        [id]="ogeKimligi()"
        [checked]="deger() === true"
        [disabled]="pasif()"
        [attr.aria-label]="ariaEtiketi() ?? null"
        [attr.aria-invalid]="ariaGecersiz()"
        [attr.aria-describedby]="ariaAciklayan()"
        (change)="isaretlendi($event)"
        (blur)="dokun()"
      />
      <span><ng-content /></span>
    </label>
  `,
})
export class Anahtar extends TemelKontrol<boolean> {
  protected isaretlendi(olay: Event): void {
    this.bildir((olay.target as HTMLInputElement).checked);
  }
}
