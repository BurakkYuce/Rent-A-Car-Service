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
import { SERVER_ERROR } from '@core/form/sunucu-hatalari';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { Icon } from '../../ikon/icon';
import { FIELD_CONTEXT, type AlanBaglami, uniqueId } from './alan-baglami';
import { errorMessages } from './error-messages';

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
  imports: [Icon],
  providers: [{ provide: FIELD_CONTEXT, useFactory: () => inject(Alan).context }],
  host: { class: 'rc-alan', '[class.rc-alan--hatali]': 'showError()' },
  template: `
    @if (grup()) {
      <span class="rc-alan__etiket" [id]="context.etiketKimligi" [class.rc-gorunmez]="etiketGizli()"
        >{{ etiket() }}
        @if (isRequired()) {
          <span class="rc-alan__zorunlu" aria-hidden="true">*</span>
        }
      </span>
    } @else {
      <label
        class="rc-alan__etiket"
        [id]="context.etiketKimligi"
        [attr.for]="context.kimlik"
        [class.rc-gorunmez]="etiketGizli()"
        >{{ etiket() }}
        @if (isRequired()) {
          <span class="rc-alan__zorunlu" aria-hidden="true">*</span>
        }
      </label>
    }
    <ng-content />
    @if (ipucu()) {
      <p class="rc-alan__ipucu" [id]="hintId">{{ ipucu() }}</p>
    }
    <div class="rc-alan__hata" [id]="errorId" aria-live="polite">
      @for (mesaj of messages(); track $index) {
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

  private readonly identity = uniqueId();
  protected readonly hintId = `${this.identity}-ipucu`;
  protected readonly errorId = `${this.identity}-hata`;

  protected readonly isRequired = computed(() => {
    const force = this.zorunlu();
    if (force !== undefined) return force;
    this.surum();
    const check = this.ngKontrol()?.control;
    return (
      !!check &&
      (check.hasValidator(Validators.required) || check.hasValidator(Validators.requiredTrue))
    );
  });

  private readonly hatalar = computed(() => {
    this.surum();
    const check = this.ngKontrol()?.control;
    if (!check || !check.invalid || check.disabled) return null;
    const server = check.errors?.[SERVER_ERROR] !== undefined;
    return server || check.touched ? check.errors : null;
  });

  protected readonly showError = computed(() => this.hatalar() !== null);

  protected readonly messages = computed(() =>
    errorMessages(this.hatalar(), (key: CeviriAnahtari, p?: Record<string, unknown>) =>
      this.transloco.translate(key, p),
    ),
  );

  readonly context: AlanBaglami = {
    kimlik: `${this.identity}-kontrol`,
    etiketKimligi: `${this.identity}-etiket`,
    gecersiz: this.showError,
    zorunlu: this.isRequired,
    aciklayanlar: computed(() => {
      const identities = [
        this.ipucu() ? this.hintId : null,
        this.showError() ? this.errorId : null,
      ].filter((k): k is string => k !== null);
      return identities.length > 0 ? identities.join(' ') : null;
    }),
  };

  ngAfterContentInit(): void {
    const check = this.ngKontrol()?.control;
    if (!check) return;
    check.events
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.surum.update((s) => s + 1));
    this.surum.update((s) => s + 1);
  }
}
