import { ChangeDetectionStrategy, Component, DestroyRef, inject, output } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { paraBicimle } from '@core/bicim/bicim';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { toNumber } from '@features/vehicles/vehicle-model';
import { Alan } from '@shared/form/alan/alan';
import { AramaSecim } from '@shared/form/arama-secim/arama-secim';
import { type SecimSecenegi, sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { ParaGirdisi } from '@shared/form/kontroller/para-girdisi';
import { SayiGirdisi } from '@shared/form/kontroller/sayi-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';

import { DocumentSubmitBar } from '../document-submit-bar';
import {
  type DocumentResult,
  INVOICE_DELIVERY_TYPES,
  INVOICE_PAYMENT_TYPES,
  type InvoiceDetail,
  VAT_LABELS,
  VAT_RATES,
  type VatRate,
  ZERO_VAT_REASONS,
} from '../document-model';
import {
  type ManualInvoiceForm as ManualInvoiceValue,
  manualInvoiceRequest,
} from '../document-requests';
import { DocumentSubmission } from '../document-submission';
import { INVOICES, recordPath } from '../document.store';

const EMPTY = { kdvOrani: '0.20' } as const;

/**
 * Manuel (kiradan bağımsız) fatura — PARA: `POST /faturalar/manuel`, `Idempotency-Key` ZORUNLU; kesilen fatura
 * DEĞİŞMEZ ve boşluksuz GİB numarası alır. KDV ve genel toplam SUNUCUDA; istemci net + oran gönderir. Gönderim
 * yaşam döngüsü {@link DocumentSubmission}: uçuşta form kilitli, belirsiz sonuçta gövde DONAR ve tekrar yalnız o
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
    AramaSecim,
    DocumentSubmitBar,
    MetinGirdisi,
    ParaGirdisi,
    SayiGirdisi,
    Secim,
    TarihSecici,
  ],
  templateUrl: './manual-invoice-form.html',
  styleUrl: '../finance-documents.scss',
})
export class ManualInvoiceForm {
  private readonly api = inject(ApiIstemcisi);
  private readonly confirm = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();

  readonly saved = output<DocumentResult | null>();
  readonly dirtyChange = output<boolean>();

  protected readonly customers = sunucuSecimKaynagi('musteri');
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
  protected readonly submission = new DocumentSubmission(this.form, () => 'manuel-fatura', {
    cariId: 'cari',
  });

  constructor() {
    this.form.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.dirtyChange.emit(this.form.dirty));
    this.submission.restore();
  }

  protected submit(): void {
    this.submission.submit<DocumentResult>(
      `${INVOICES}/manuel`,
      () => manualInvoiceRequest(this.form.getRawValue() as ManualInvoiceValue),
      {
        succeeded: (r) => {
          this.reset();
          this.announce(r);
          this.saved.emit(r);
        },
        recorded: () => this.reset(),
        reload: () => this.saved.emit(null),
      },
    );
  }

  protected async abandon(): Promise<void> {
    const yes = await this.confirm.sor({
      baslik: this.t('finansBelge.vazgecBaslik'),
      mesaj: this.t('finansBelge.vazgecMesaj'),
      tehlikeli: true,
    });
    if (yes) this.submission.abandon();
  }

  /** Başarı mesajı sunucunun kestiği genel toplamla (fatura yanıtı yalnız no döner; detay okunur). */
  private announce(r: DocumentResult): void {
    this.api
      .get<InvoiceDetail>(recordPath(INVOICES, r.id), { context: istekBaglami({ sessiz: true }) })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (d) =>
          this.toast.basari(
            this.t('finansBelge.fatura.manuelKesildiTutar', {
              no: d.no,
              tutar: paraBicimle(toNumber(d.genelToplam), d.doviz),
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
