import {
  ChangeDetectionStrategy,
  Component,
  type OnInit,
  computed,
  inject,
  input,
  output,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import type { FinanceAccountItem } from '@core/api/ui-tipleri';
import { formatMoney } from '@core/bicim/bicim';
import { moneySubmission } from '@core/form/money-submission';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { Alan } from '@shared/form/alan/alan';
import { MoneyInput } from '@shared/form/kontroller/money-input';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Selection } from '@shared/form/kontroller/selection';
import { MoneySubmitBar } from '@shared/form/money-submit/money-submit-bar';

import { round2 } from '../money-math';
import {
  type AccountKind,
  type PolicyDetail,
  type PolicyPaymentRequest,
  type PolicyRow,
  REGULATION,
  num,
  recordPath,
} from '../service-insurance-model';

/**
 * Sigorta ödemesi (Blazor `/regulasyon-odeme/sigorta`) — PARA: prim + zeyil ek prim, Borç Gider[araç] / Alacak
 * Kasa-Banka. Yapısal idempotency (poliçe başına tek ödeme; ödenmiş poliçe → 409 `mukerrer` + `mevcut`). Dövizli
 * poliçede kur boş = sunucu çözer (firma kuru → TCMB; bulunamazsa 400), TRY'de kur alanı yok. Tutar hesabı SUNUCUDA.
 */
@Component({
  selector: 'rc-policy-pay-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, TranslocoPipe, Alan, MoneySubmitBar, MoneyInput, Selection],
  template: `
    <section class="rc-bolum" aria-labelledby="rc-police-ode-baslik">
      <h2 id="rc-police-ode-baslik">{{ 'servisSigorta.sigorta.ode' | transloco }}</h2>
      <p class="not">{{ 'servisSigorta.sigorta.odeAciklama' | transloco }}</p>
      <p class="sonraki">
        {{ 'servisSigorta.sigorta.odenecekPrim' | transloco: { prim: premiumText() } }}
      </p>
      <form class="form" [formGroup]="form" (ngSubmit)="pay()">
        <div class="rc-form-izgara">
          <rc-alan [etiket]="'servisSigorta.para.hesap' | transloco">
            <rc-secim formControlName="hesap" [secenekler]="kindOptions" />
          </rc-alan>
          @if (accountOptions().length > 0) {
            <rc-alan [etiket]="'servisSigorta.para.hesapId' | transloco">
              <rc-secim
                formControlName="hesapId"
                [secenekler]="accountOptions()"
                [bosEtiket]="'servisSigorta.para.hesapBelirtilmemis' | transloco"
              />
            </rc-alan>
          }
          <rc-alan
            [etiket]="'servisSigorta.sigorta.zeyilEkPrim' | transloco"
            [ipucu]="'servisSigorta.sigorta.zeyilEkPrimIpucu' | transloco"
          >
            <rc-para-girdisi formControlName="zeyilEkPrim" [paraBirimi]="policy().doviz" />
          </rc-alan>
          @if (foreign()) {
            <rc-alan
              [etiket]="'servisSigorta.para.kur' | transloco"
              [ipucu]="'servisSigorta.para.kurIpucu' | transloco: { doviz: policy().doviz }"
            >
              <rc-para-girdisi formControlName="kur" [kesir]="6" yerTutucu="" />
            </rc-alan>
          }
        </div>
        <rc-money-submit
          [submission]="payment"
          type="submit"
          ikon="cash"
          [label]="'servisSigorta.sigorta.ode' | transloco"
          [disabled]="refreshing()"
          (abandoned)="settled.emit()"
        />
      </form>
    </section>
  `,
})
export class PolicyPayPanel implements OnInit {
  readonly policy = input.required<PolicyRow>();
  readonly accounts = input<readonly FinanceAccountItem[]>([]);
  readonly refreshing = input(false);
  readonly settled = output<void>();

  private readonly toast = inject(ToastService);
  private readonly t = translationFunction();

  protected readonly form = new FormGroup({
    hesap: new FormControl<AccountKind | null>('Kasa', Validators.required),
    hesapId: new FormControl<string | null>(null),
    zeyilEkPrim: new FormControl<string | null>(null),
    kur: new FormControl<string | null>(null),
  });
  /** Ödenen poliçede panel gizlenir: `mukerrer` bildirimi toast'ta (not panelle kaybolurdu). */
  protected readonly payment = moneySubmission<PolicyPaymentRequest>({
    scope: () => `sigorta:${this.policy().id}`,
    duplicateDisplay: 'toast',
    // E29 yapısal (poliçe başına tek ödeme): ödemeyi başkası yapmış olabilir — nötr metin (r316 L1).
    recordedMessage: 'servisSigorta.para.policeZatenOdendi',
  });

  protected readonly kindOptions: readonly SecenekOgesi<AccountKind>[] = [
    { deger: 'Kasa', etiket: this.t('servisSigorta.para.kasa') },
    { deger: 'Banka', etiket: this.t('servisSigorta.para.banka') },
  ];
  private readonly accountKind = toSignal(this.form.controls.hesap.valueChanges, {
    initialValue: this.form.controls.hesap.value,
  });
  protected readonly accountOptions = computed<readonly SecenekOgesi<string>[]>(() =>
    this.accounts()
      .filter((h) => h.tur !== null && h.tur === this.accountKind())
      .map((h) => ({ deger: h.id, etiket: h.etiket })),
  );
  protected readonly foreign = computed(() => this.policy().doviz !== 'TRY');
  protected readonly premiumText = computed(() =>
    formatMoney(num(this.policy().prim), this.policy().doviz),
  );

  constructor() {
    // Kasa ↔ Banka değişince başka türün hesabı seçili kalmasın (inceleme L3).
    this.form.controls.hesap.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      const id = this.form.controls.hesapId.value;
      if (id !== null && !this.accountOptions().some((h) => h.deger === id))
        this.form.controls.hesapId.setValue(null);
    });
  }

  ngOnInit(): void {
    this.payment.restore(this.form);
  }

  hasPendingWork(): boolean {
    return this.hasPendingPayment() || this.form.dirty;
  }

  hasPendingPayment(): boolean {
    return this.payment.pending();
  }

  protected pay(): void {
    if (this.refreshing()) return;
    const p = this.policy();
    void this.payment.run<PolicyDetail>({
      form: this.form,
      build: () => {
        const v = this.form.getRawValue();
        return {
          path: recordPath(`${REGULATION}/sigortalar`, p.id, '/odeme'),
          target: `sigorta:${p.id}`,
          body: {
            hesap: v.hesap ?? 'Kasa',
            hesapId: v.hesapId,
            zeyilEkPrim: v.zeyilEkPrim,
            kur: this.foreign() ? v.kur : null,
          },
          // Sunucunun `mevcut.tutar`'ı prim + zeyil ek primidir (inceleme L4): bildirimde aynı toplam.
          content: {
            tutar: round2((num(p.prim) ?? 0) + (num(v.zeyilEkPrim) ?? 0)),
            doviz: p.doviz,
          },
        };
      },
      success: (d) => {
        this.toast.basari(
          this.t('servisSigorta.sigorta.odendiBildirim', {
            tutar: formatMoney(num(d.odeme?.tutar ?? null), d.odeme?.doviz ?? p.doviz),
          }),
        );
        this.form.reset({ hesap: 'Kasa' });
      },
      settled: () => this.settled.emit(),
    });
  }
}
