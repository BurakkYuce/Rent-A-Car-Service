import {
  AfterContentInit,
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  booleanAttribute,
  computed,
  contentChild,
  inject,
  input,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NgControl, Validators } from '@angular/forms';
import { TranslocoService } from '@jsverse/transloco';
import { SUNUCU_HATASI } from '@core/form/sunucu-hatalari';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { Ikon } from '../../ikon/ikon';
import { ALAN_BAGLAMI, type AlanBaglami, tekilKimlik } from './alan-baglami';
import { hataMesajlari } from './hata-mesajlari';

/**
 * Form alanı: etiket + zorunlu işareti + kontrol + ipucu + hata yuvası. İçindeki CVA kontrolünü
 * (`formControlName` / `formControl`) bulur; hatayı dokunulduktan (ya da gönderimde
 * `markAllAsTouched`'tan) sonra, sunucu hatasını HEMEN gösterir. Kontrole `ALAN_BAGLAMI` ile
 * `id`, `aria-describedby`, `aria-invalid`, `aria-required` verir.
 *
 * ```html
 * <rc-alan etiket="Plaka" ipucu="34 ABC 123">
 *   <rc-metin-girdisi formControlName="plaka" />
 * </rc-alan>
 * ```
 * Radyo grubu gibi çok öğeli kontrolde `grup` verilir: etiket `<label for>` değil, grubun
 * `aria-labelledby`'ı olur.
 */
@Component({
  selector: 'rc-alan',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Ikon],
  providers: [{ provide: ALAN_BAGLAMI, useFactory: () => inject(Alan).baglam }],
  host: { class: 'rc-alan', '[class.rc-alan--hatali]': 'hataGoster()' },
  template: `
    @if (grup()) {
      <span class="rc-alan__etiket" [id]="baglam.etiketKimligi" [class.rc-gorunmez]="etiketGizli()"
        >{{ etiket() }}
        @if (zorunluMu()) {
          <span class="rc-alan__zorunlu" aria-hidden="true">*</span>
        }
      </span>
    } @else {
      <label
        class="rc-alan__etiket"
        [id]="baglam.etiketKimligi"
        [attr.for]="baglam.kimlik"
        [class.rc-gorunmez]="etiketGizli()"
        >{{ etiket() }}
        @if (zorunluMu()) {
          <span class="rc-alan__zorunlu" aria-hidden="true">*</span>
        }
      </label>
    }
    <ng-content />
    @if (ipucu()) {
      <p class="rc-alan__ipucu" [id]="ipucuKimligi">{{ ipucu() }}</p>
    }
    <div class="rc-alan__hata" [id]="hataKimligi" aria-live="polite">
      @for (mesaj of mesajlar(); track $index) {
        <p><rc-ikon ad="alert-circle" [boyut]="14" />{{ mesaj }}</p>
      }
    </div>
  `,
})
export class Alan implements AfterContentInit {
  readonly etiket = input.required<string>();
  readonly ipucu = input<string>('');
  readonly grup = input(false, { transform: booleanAttribute });
  readonly etiketGizli = input(false, { transform: booleanAttribute });
  /** Zorunlu işaretini doğrulayıcıdan bağımsız zorla (ör. koşullu zorunluluk). */
  readonly zorunlu = input<boolean | undefined>(undefined);

  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly ngKontrol = contentChild(NgControl, { descendants: true });
  /** Kontrol durumu (değer/durum/dokunma) her değiştiğinde artar; computed'lar bunu okur. */
  private readonly surum = signal(0);

  private readonly kimlik = tekilKimlik();
  protected readonly ipucuKimligi = `${this.kimlik}-ipucu`;
  protected readonly hataKimligi = `${this.kimlik}-hata`;

  protected readonly zorunluMu = computed(() => {
    const zorla = this.zorunlu();
    if (zorla !== undefined) return zorla;
    this.surum();
    const kontrol = this.ngKontrol()?.control;
    return (
      !!kontrol &&
      (kontrol.hasValidator(Validators.required) || kontrol.hasValidator(Validators.requiredTrue))
    );
  });

  private readonly hatalar = computed(() => {
    this.surum();
    const kontrol = this.ngKontrol()?.control;
    if (!kontrol || !kontrol.invalid || kontrol.disabled) return null;
    const sunucu = kontrol.errors?.[SUNUCU_HATASI] !== undefined;
    return sunucu || kontrol.touched ? kontrol.errors : null;
  });

  protected readonly hataGoster = computed(() => this.hatalar() !== null);

  protected readonly mesajlar = computed(() =>
    hataMesajlari(this.hatalar(), (anahtar: CeviriAnahtari, p?: Record<string, unknown>) =>
      this.transloco.translate(anahtar, p),
    ),
  );

  readonly baglam: AlanBaglami = {
    kimlik: `${this.kimlik}-kontrol`,
    etiketKimligi: `${this.kimlik}-etiket`,
    gecersiz: this.hataGoster,
    zorunlu: this.zorunluMu,
    aciklayanlar: computed(() => {
      const kimlikler = [
        this.ipucu() ? this.ipucuKimligi : null,
        this.hataGoster() ? this.hataKimligi : null,
      ].filter((k): k is string => k !== null);
      return kimlikler.length > 0 ? kimlikler.join(' ') : null;
    }),
  };

  ngAfterContentInit(): void {
    const kontrol = this.ngKontrol()?.control;
    if (!kontrol) return;
    kontrol.events
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.surum.update((s) => s + 1));
    this.surum.update((s) => s + 1);
  }
}
