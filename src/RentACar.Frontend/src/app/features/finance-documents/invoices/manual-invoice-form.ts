import { ChangeDetectionStrategy, Component, DestroyRef, inject, output } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { moneySubmission } from '@core/form/money-submission';
import { formatMoney } from '@core/bicim/bicim';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { requestContext } from '@core/oturum/request-context';
import { toNumber } from '@features/vehicles/vehicle-model';
import { Alan } from '@shared/form/alan/alan';
import { SearchSelection } from '@shared/form/arama-secim/search-selection';
import {
  type SecimSecenegi,
  serverSelectionSource,
} from '@shared/form/arama-secim/selection-source';
import { TextInput } from '@shared/form/kontroller/text-input';
import { MoneyInput } from '@shared/form/kontroller/money-input';
import { NumberInput } from '@shared/form/kontroller/number-input';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Selection } from '@shared/form/kontroller/selection';
import { MoneySubmitBar } from '@shared/form/money-submit/money-submit-bar';
import { DatePicker } from '@shared/form/tarih/date-picker';

import {
  type DocumentResult,
  INVOICE_DELIVERY_TYPES,
  INVOICE_PAYMENT_TYPES,
  type InvoiceDetail,
  type ManualInvoiceRequest,
  VAT_LABELS,
  VAT_RATES,
  type VatRate,
  ZERO_VAT_REASONS,
} from '../document-model';
import {
  type ManualInvoiceForm as ManualInvoiceValue,
  manualInvoiceRequest,
} from '../document-requests';
import { INVOICES, recordPath } from '../document.store';

const EMPTY = { kdvOrani: '0.20' } as const;

/**
 * Manuel (kiradan bağımsız) fatura — PARA: `POST /faturalar/manuel`, `Idempotency-Key` ZORUNLU; kesilen fatura
 * DEĞİŞMEZ ve boşluksuz GİB numarası alır. KDV ve genel toplam SUNUCUDA; istemci net + oran gönderir. Gönderim
 * yaşam döngüsü çekirdek `MoneySubmission`: uçuşta form kilitli, belirsiz sonuçta gövde DONAR ve tekrar yalnız o
 * gövdeyle gider (r300 HIGH-1: düzeltilmiş tutar aynı anahtarla gidip ikinci fatura kestiriyordu). Başarıda genel
 * toplam sunucudan okunup gösterilir.
 */
@Component({
  selector: 'rc-manual-invoice-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    SearchSelection,
    MoneySubmitBar,
    TextInput,
    MoneyInput,
    NumberInput,
    Selection,
    DatePicker,
  ],
  templateUrl: './manual-invoice-form.html',
  styleUrl: '../finance-documents.scss',
})
export class ManualInvoiceForm {
  private readonly api = inject(ApiIstemcisi);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = translationFunction();

  readonly saved = output<DocumentResult | null>();
  readonly dirtyChange = output<boolean>();

  protected readonly customers = serverSelectionSource('musteri');
  protected readonly paymentTypes = INVOICE_PAYMENT_TYPES;
  protected readonly deliveryTypes = INVOICE_DELIVERY_TYPES;
  protected readonly vatOptions: readonly SecenekOgesi<VatRate>[] = VAT_RATES.map((v) => ({
    deger: v,
    etiket: VAT_LABELS[v],
  }));
  protected readonly zeroReasonOptions: readonly SecenekOgesi<string>[] = ZERO_VAT_REASONS.map(
    (r) => ({ deger: r, etiket: r }),
  );

  protected readonly form = new FormGroup({
    cari: new FormControl<SecimSecenegi | null>(null, Validators.required),
    netTutar: new FormControl<string | null>(null, Validators.required),
    kdvOrani: new FormControl<VatRate | null>(EMPTY.kdvOrani, Validators.required),
    tarih: new FormControl<string | null>(null),
    vadeTarihi: new FormControl<string | null>(null),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
    islemSube: new FormControl<string | null>(null, Validators.maxLength(128)),
    evrakNo: new FormControl<string | null>(null, Validators.maxLength(64)),
    faturaOzelKod: new FormControl<string | null>(null, Validators.maxLength(64)),
    odemeTuru: new FormControl<string | null>(null, Validators.maxLength(32)),
    gonderimSekli: new FormControl<string | null>(null, Validators.maxLength(32)),
    kdvSifirSebep: new FormControl<string | null>(null),
    otv: new FormControl<string | null>(null),
    tevkifatOran: new FormControl<number | null>(null, [Validators.min(0), Validators.max(100)]),
    tevkifatTutar: new FormControl<string | null>(null),
    damgaVergisi: new FormControl<string | null>(null),
  });
  protected readonly submission = moneySubmission<ManualInvoiceRequest>({
    scope: () => 'manuel-fatura',
  });

  constructor() {
    this.form.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.dirtyChange.emit(this.form.dirty));
    this.submission.restore(this.form);
  }

  protected submit(): void {
    void this.submission.run<DocumentResult>({
      form: this.form,
      fieldMap: () => ({ cariId: 'cari' }),
      build: () => ({
        path: `${INVOICES}/manuel`,
        body: manualInvoiceRequest(this.form.getRawValue() as ManualInvoiceValue),
      }),
      success: (r) => {
        this.reset();
        this.announce(r);
        this.saved.emit(r);
      },
      afterDuplicate: () => this.reset(),
      settled: (reason) => {
        if (reason !== 'done') this.saved.emit(null);
      },
    });
  }

  /** Başarı mesajı sunucunun kestiği genel toplamla (fatura yanıtı yalnız no döner; detay okunur). */
  private announce(r: DocumentResult): void {
    this.api
      .get<InvoiceDetail>(recordPath(INVOICES, r.id), { context: requestContext({ sessiz: true }) })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (d) =>
          this.toast.basari(
            this.t('finansBelge.fatura.manuelKesildiTutar', {
              no: d.no,
              tutar: formatMoney(toNumber(d.genelToplam), d.doviz),
            }),
          ),
        error: () => this.toast.basari(this.t('finansBelge.fatura.manuelKesildi', { no: r.no })),
      });
  }

  private reset(): void {
    this.form.reset({ ...EMPTY });
    this.submission.renew();
    this.dirtyChange.emit(false);
  }
}
