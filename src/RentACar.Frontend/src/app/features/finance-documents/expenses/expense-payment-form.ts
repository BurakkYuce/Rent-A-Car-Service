import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  type OnInit,
  inject,
  input,
  output,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import { paraBicimle } from '@core/bicim/bicim';
import { moneySubmission } from '@core/form/money-submission';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { toNumber } from '@features/vehicles/vehicle-model';
import { ParaPipe } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { ParaGirdisi } from '@shared/form/kontroller/para-girdisi';
import { MoneySubmitBar } from '@shared/form/money-submit/money-submit-bar';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';

import type { ExpensePayment, ExpensePaymentRequest, ExpenseRow } from '../document-model';
import {
  type ExpensePaymentForm as PaymentValue,
  expensePaymentRequest,
} from '../document-requests';
import { EXPENSES, recordPath } from '../document.store';

/** Gider ödeme formunun donmuş deneme kapsamı (sayfa `PendingMoneyAttempts` anahtarı). */
export const expensePaymentScope = (id: string) => `gider-odeme:${id}`;

/**
 * Açık hesap giderine ödeme TAKİBİ (`POST /giderler/{id}/odeme`; deftere YAZMAZ, gider başına danışma kilidi altında
 * kalan). Gönderim çekirdek `MoneySubmission`: uçuşta kilit, belirsiz sonuçta gövde donar ve gider başına sayfada
 * saklanır (başka satıra geçip dönünce kilitli geri gelir). Tutar boş → kalanın tamamı (SUNUCU). Bileşen gider başına
 * yeniden kurulur (`@for … track id`): başka giderin tutarı taşınmaz. Başarı mesajı sunucunun yazdığı tutarla.
 */
@Component({
  selector: 'rc-expense-payment-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    MoneySubmitBar,
    MetinGirdisi,
    ParaGirdisi,
    ParaPipe,
    TarihSecici,
  ],
  template: `
    <section class="rc-bolum" aria-labelledby="rc-gider-odeme">
      <h2 id="rc-gider-odeme">
        {{ 'finansBelge.gider.odemeBaslik' | transloco: { no: expense().no } }}
      </h2>
      <p class="not">
        {{ 'finansBelge.gider.odemeAciklama' | transloco }}
        {{ 'finansBelge.kalan' | transloco }}: {{ num(expense().kalan) | para: expense().doviz }}
      </p>
      <form class="form" [formGroup]="form" (ngSubmit)="submit()">
        <div class="rc-form-izgara">
          <rc-alan
            [etiket]="'finansBelge.tutar' | transloco"
            [ipucu]="'finansBelge.gider.tutarIpucu' | transloco"
          >
            <rc-para-girdisi formControlName="tutar" [paraBirimi]="expense().doviz" />
          </rc-alan>
          <rc-alan
            [etiket]="'finansBelge.gider.odemeTarihi' | transloco"
            [ipucu]="'finansBelge.bosBugun' | transloco"
          >
            <rc-tarih-secici formControlName="tarih" />
          </rc-alan>
          <rc-alan [etiket]="'finansBelge.ceza.makbuzNo' | transloco">
            <rc-metin-girdisi formControlName="makbuzNo" [azamiUzunluk]="32" />
          </rc-alan>
          <rc-alan [etiket]="'finansBelge.aciklama' | transloco">
            <rc-metin-girdisi formControlName="aciklama" [azamiUzunluk]="512" />
          </rc-alan>
        </div>
        <rc-money-submit
          [submission]="submission"
          type="submit"
          [label]="'finansBelge.gider.ode' | transloco"
        >
          <button
            type="button"
            class="rc-dugme rc-dugme--hayalet rc-dugme--kucuk"
            [disabled]="submission.sending()"
            (click)="closed.emit()"
          >
            {{ 'finansBelge.kapat' | transloco }}
          </button>
        </rc-money-submit>
      </form>
    </section>
  `,
  styleUrl: '../finance-documents.scss',
})
export class ExpensePaymentForm implements OnInit {
  private readonly toast = inject(ToastServisi);
  private readonly t = ceviriFonksiyonu();

  readonly expense = input.required<ExpenseRow>();
  readonly paid = output<ExpensePayment | null>();
  readonly closed = output<void>();
  readonly dirtyChange = output<boolean>();

  protected readonly num = toNumber;
  protected readonly form = new FormGroup({
    tutar: new FormControl<string | null>(null),
    tarih: new FormControl<string | null>(null),
    makbuzNo: new FormControl<string | null>(null, Validators.maxLength(32)),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
  });
  protected readonly submission = moneySubmission<ExpensePaymentRequest>({
    scope: () => expensePaymentScope(this.expense().id),
  });

  constructor() {
    this.form.valueChanges
      .pipe(takeUntilDestroyed(inject(DestroyRef)))
      .subscribe(() => this.dirtyChange.emit(this.form.dirty));
  }

  ngOnInit(): void {
    this.submission.restore(this.form);
  }

  protected submit(): void {
    const e = this.expense();
    void this.submission.run<ExpensePayment>({
      form: this.form,
      build: () => ({
        path: recordPath(EXPENSES, e.id, '/odeme'),
        body: expensePaymentRequest(this.form.getRawValue() as PaymentValue),
      }),
      success: (p) => {
        this.toast.basari(
          this.t('finansBelge.gider.odemeYazildi', {
            tutar: paraBicimle(toNumber(p.tutar), e.doviz),
            kalan: paraBicimle(toNumber(p.kalanSonrasi), e.doviz),
          }),
        );
        this.reset();
        this.paid.emit(p);
      },
      afterDuplicate: () => this.reset(),
      settled: (reason) => {
        if (reason !== 'done') this.paid.emit(null);
      },
    });
  }

  private reset(): void {
    this.form.reset();
    this.submission.renew();
    this.dirtyChange.emit(false);
  }
}
