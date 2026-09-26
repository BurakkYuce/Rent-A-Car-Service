import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  type OnInit,
  computed,
  inject,
  input,
  output,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import { formatMoney } from '@core/bicim/bicim';
import { moneySubmission } from '@core/form/money-submission';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { toNumber } from '@features/vehicles/vehicle-model';
import { Alan } from '@shared/form/alan/alan';
import { TextInput } from '@shared/form/kontroller/text-input';
import { MoneyInput } from '@shared/form/kontroller/money-input';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Selection } from '@shared/form/kontroller/selection';
import { MoneyNoticeView } from '@shared/form/money-submit/money-notice';
import { MoneySubmitBar } from '@shared/form/money-submit/money-submit-bar';
import { DatePicker } from '@shared/form/tarih/date-picker';

import {
  ACCOUNT_KINDS,
  type AccountKind,
  type PenaltyDetail,
  type PenaltyPaymentRequest,
  type PenaltyPaymentResult,
} from '../document-model';
import {
  type PenaltyPaymentForm as PaymentValue,
  penaltyPaymentRequest,
} from '../document-requests';
import { PENALTIES, recordPath } from '../document.store';

/** Ceza ödeme formunun donmuş deneme kapsamı (sayfa `PendingMoneyAttempts` anahtarı). */
export const penaltyPaymentScope = (id: string) => `ceza-odeme:${id}`;

/**
 * Ceza kalem ödemesi — PARA (`POST /cezalar/{id}/odeme`; Borç Gider / Alacak Kasa·Banka). Gönderim
 * çekirdek `MoneySubmission`: uçuşta kilit; belirsiz sonuçta gövde donar (ceza başına sayfada saklanır), tekrar yalnız
 * o gövdeyle; `mevcut`suz 409'da anahtar KORUNUR (r300 M3). Tutar boş → kalemin kalanının tamamı (SUNUCU). Kalem
 * değişince tutar TEMİZLENİR (eski kalemin tutarı yeni kaleme gitmesin). Bileşen ceza başına yeniden kurulur.
 */
@Component({
  selector: 'rc-penalty-payment-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    MoneyNoticeView,
    MoneySubmitBar,
    TextInput,
    MoneyInput,
    Selection,
    DatePicker,
  ],
  templateUrl: './penalty-payment-form.html',
  styleUrl: '../finance-documents.scss',
})
export class PenaltyPaymentForm implements OnInit {
  private readonly toast = inject(ToastService);
  private readonly t = translationFunction();

  readonly detail = input.required<PenaltyDetail>();
  readonly paid = output<PenaltyPaymentResult | null>();
  readonly dirtyChange = output<boolean>();

  protected readonly lineOptions = computed<readonly SecenekOgesi<string>[]>(() =>
    this.detail()
      .kalemler.filter((k) => (toNumber(k.kalan) ?? 0) > 0)
      .map((k) => ({
        deger: k.id,
        etiket: this.t('finansBelge.ceza.kalemSecenek', {
          sira: k.sira,
          sebep: k.sebep ?? '—',
          kalan: formatMoney(toNumber(k.kalan)),
        }),
      })),
  );
  protected readonly accountOptions: readonly SecenekOgesi<AccountKind>[] = ACCOUNT_KINDS.map(
    (a) => ({ deger: a, etiket: a }),
  );

  protected readonly form = new FormGroup({
    satirId: new FormControl<string | null>(null, Validators.required),
    tutar: new FormControl<string | null>(null),
    hesap: new FormControl<AccountKind | null>('Kasa', Validators.required),
    tarih: new FormControl<string | null>(null),
    makbuzNo: new FormControl<string | null>(null, Validators.maxLength(64)),
    islemYapan: new FormControl<string | null>(null, Validators.maxLength(128)),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
  });
  protected readonly submission = moneySubmission<PenaltyPaymentRequest>({
    scope: () => penaltyPaymentScope(this.detail().ceza.id),
  });

  constructor() {
    const destroyRef = inject(DestroyRef);
    this.form.controls.satirId.valueChanges
      .pipe(takeUntilDestroyed(destroyRef))
      .subscribe(() => this.form.controls.tutar.reset(null));
    this.form.valueChanges
      .pipe(takeUntilDestroyed(destroyRef))
      .subscribe(() => this.dirtyChange.emit(this.form.dirty));
  }

  ngOnInit(): void {
    this.submission.restore(this.form);
  }

  protected submit(): void {
    const id = this.detail().ceza.id;
    void this.submission.run<PenaltyPaymentResult>({
      form: this.form,
      build: () => ({
        path: recordPath(PENALTIES, id, '/odeme'),
        body: penaltyPaymentRequest(this.form.getRawValue() as PaymentValue),
      }),
      success: (r) => {
        this.toast.basari(
          this.t('finansBelge.ceza.odemeYazildi', {
            tutar: formatMoney(toNumber(r.tutar)),
            kalan: formatMoney(toNumber(r.cezaKalan)),
          }),
        );
        this.reset();
        this.paid.emit(r);
      },
      afterDuplicate: () => this.reset(),
      settled: (reason) => {
        if (reason !== 'done') this.paid.emit(null);
      },
    });
  }

  private reset(): void {
    this.form.reset({ hesap: 'Kasa' });
    this.submission.renew();
    this.dirtyChange.emit(false);
  }
}
