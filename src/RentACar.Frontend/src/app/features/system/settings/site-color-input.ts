import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { BaseControl, controlProviders } from '@shared/form/controls/base-control';

import { isHexColor } from './settings-model';

/**
 * "Kendi ana rengim": yerel renk seçici (`<input type="color">`) + `#rrggbb` metin alanı, ikisi aynı değeri
 * gösterir; "Temizle" değeri `null` yapar (seçili setin ana rengi kullanılır). Metin alanı yazılanı AYNEN bildirir
 * (biçim sunucuda doğrulanır, hata alanın altında) — renk seçicide geçerli renk yoksa setin ana rengi gösterilir.
 * Etiket (`rc-alan`) metin alanına bağlanır; renk seçicinin kendi erişilebilir adı vardır.
 */
@Component({
  selector: 'rc-site-renk-girdisi',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe],
  providers: controlProviders(() => SiteColorInput),
  styles: `
    .renk {
      display: flex;
      flex-wrap: wrap;
      gap: var(--rc-bosluk-2);
      align-items: center;
    }

    .secici {
      flex: none;
      width: var(--rc-kontrol-yukseklik);
      height: var(--rc-kontrol-yukseklik);
      padding: var(--rc-bosluk-0-5);
      border: 1px solid var(--rc-kenar-kontrol);
      border-radius: var(--rc-yaricap-md);
      background-color: var(--rc-yuzey);
      cursor: pointer;

      &:disabled {
        cursor: not-allowed;
      }
    }

    .metin {
      flex: 1 1 8rem;
      max-width: 10rem;
    }
  `,
  template: `
    <div class="renk">
      <input
        class="secici"
        type="color"
        [value]="pickerValue()"
        [disabled]="pasif()"
        [attr.aria-label]="'sistem.ayarlar.renk.secici' | transloco"
        (input)="picked($event)"
        (blur)="touch()"
      />
      <input
        class="rc-girdi metin"
        type="text"
        [id]="itemId()"
        [value]="deger() ?? ''"
        [disabled]="pasif()"
        maxlength="7"
        placeholder="#rrggbb"
        autocomplete="off"
        spellcheck="false"
        [attr.aria-invalid]="ariaInvalid()"
        [attr.aria-describedby]="ariaDescribedBy()"
        [attr.aria-required]="ariaRequired()"
        (input)="written($event)"
        (blur)="touch()"
      />
      <button
        type="button"
        class="rc-dugme rc-dugme--kucuk"
        [disabled]="pasif() || deger() === null"
        (click)="clear()"
      >
        {{ 'sistem.ayarlar.renk.temizle' | transloco }}
      </button>
    </div>
  `,
})
export class SiteColorInput extends BaseControl<string> {
  /** Değer yokken renk seçicinin göstereceği renk (seçili setin ana rengi). */
  readonly varsayilan = input<string | null>(null);

  protected readonly pickerValue = computed(() => {
    const v = this.deger();
    if (isHexColor(v)) return v;
    const fallback = this.varsayilan();
    return isHexColor(fallback) ? fallback : '#000000';
  });

  protected picked(evt: Event): void {
    // Tarayıcı renk seçicisi daima küçük harf `#rrggbb` verir.
    this.notify((evt.target as HTMLInputElement).value);
  }

  protected written(evt: Event): void {
    const text = (evt.target as HTMLInputElement).value;
    this.notify(text === '' ? null : text);
  }

  protected clear(): void {
    this.notify(null);
    this.touch();
  }
}
