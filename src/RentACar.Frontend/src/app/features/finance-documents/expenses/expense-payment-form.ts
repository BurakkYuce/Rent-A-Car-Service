import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { paraBicimle } from '@core/bicim/bicim';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { toNumber } from '@features/vehicles/vehicle-model';
import { ParaPipe } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { ParaGirdisi } from '@shared/form/kontroller/para-girdisi';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';

import { DocumentNotice } from '../document-notice';
import type { ExpensePayment, ExpenseRow } from '../document-model';
import {
  type ExpensePaymentForm as PaymentValue,
  type FormNotice,
  expensePaymentRequest,
  formNotice,
} from '../document-requests';
import { EXPENSES, recordPath } from '../document.store';

/**
 * Açık hesap giderine ödeme TAKİBİ (`POST /giderler/{id}/odeme`; deftere YAZMAZ, gider başına danışma kilidi altında
 * kalan). `Idempotency-Key` ZORUNLU; tekrar AYNI anahtarla, 2xx/`mukerrer` sonrası yeni. Tutar boş → kalanın tamamı
 * (SUNUCU). Bileşen gider başına yeniden kurulur (`@for … track id`): başka giderin tutarı taşınmaz.
 */
@Component({
  selector: 'rc-expense-payment-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    DocumentNotice,
    FormHatalari,
    MetinGirdisi,
    ParaGirdisi,
    ParaPipe,
    TarihSecici,
  ],
  template: `
    <section class="bolum" aria-labelledby="rc-gider-odeme">
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
        <rc-form-hatalari [hatalar]="submission.genelHatalar()" />
        <rc-document-notice [notice]="notice()" />
        <div class="form__eylemler">
          <button
            type="submit"
            class="rc-dugme rc-dugme--birincil rc-dugme--kucuk"
            [disabled]="submission.gonderiliyor()"
          >
            {{
              submission.gonderiliyor()
                ? ('form.gonderiliyor' | transloco)
                : ('finansBelge.gider.ode' | transloco)
            }}
          </button>
          <button
            type="button"
            class="rc-dugme rc-dugme--hayalet rc-dugme--kucuk"
            (click)="closed.emit()"
          >
            {{ 'finansBelge.kapat' | transloco }}
          </button>
        </div>
      </form>
    </section>
  `,
  styleUrl: '../finance-documents.scss',
})
export class ExpensePaymentForm {
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastServisi);
  private readonly t = ceviriFonksiyonu();

  readonly expense = input.required<ExpenseRow>();
  readonly paid = output<ExpensePayment | null>();
  readonly closed = output<void>();
  readonly dirtyChange = output<boolean>();

  protected readonly num = toNumber;
  protected readonly notice = signal<FormNotice | null>(null);
  protected readonly form = new FormGroup({
    tutar: new FormControl<string | null>(null),
    tarih: new FormControl<string | null>(null),
    makbuzNo: new FormControl<string | null>(null, Validators.maxLength(32)),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
  });
  protected readonly submission = formGonderimi();

  constructor() {
    this.form.valueChanges
      .pipe(takeUntilDestroyed(inject(DestroyRef)))
      .subscribe(() => this.dirtyChange.emit(this.form.dirty));
  }

  protected submit(): void {
    this.notice.set(null);
    const e = this.expense();
    const body = expensePaymentRequest(this.form.getRawValue() as PaymentValue);
    this.submission.gonder(
      this.form,
      (key) =>
        this.api.post<ExpensePayment>(recordPath(EXPENSES, e.id, '/odeme'), body, {
          islemAnahtari: key,
        }),
      {
        basarili: (p) => {
          this.toast.basari(
            this.t('finansBelge.gider.odemeYazildi', {
              tutar: paraBicimle(toNumber(p.tutar), e.doviz),
              kalan: paraBicimle(toNumber(p.kalanSonrasi), e.doviz),
            }),
          );
          this.reset();
          this.paid.emit(p);
        },
        hata: (h) => {
          this.notice.set(formNotice(h));
          if (h.kod === 'mukerrer') {
            if (h.mevcut?.ayniIcerik) this.reset();
            this.paid.emit(null);
          }
        },
      },
    );
  }

  private reset(): void {
    this.form.reset();
    this.submission.kilit.yenile();
    this.dirtyChange.emit(false);
  }
}
