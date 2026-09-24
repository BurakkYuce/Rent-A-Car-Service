import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { Alan } from '@shared/form/alan/alan';
import { AramaSecim } from '@shared/form/arama-secim/arama-secim';
import { type SecimSecenegi, sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { ParaGirdisi } from '@shared/form/kontroller/para-girdisi';
import { SayiGirdisi } from '@shared/form/kontroller/sayi-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';

import { DocumentNotice } from '../document-notice';
import {
  type DocumentResult,
  INVOICE_DELIVERY_TYPES,
  INVOICE_PAYMENT_TYPES,
  VAT_LABELS,
  VAT_RATES,
  type VatRate,
  ZERO_VAT_REASONS,
} from '../document-model';
import {
  type FormNotice,
  type ManualInvoiceForm as ManualInvoiceValue,
  formNotice,
  manualInvoiceRequest,
} from '../document-requests';
import { INVOICES } from '../document.store';

/**
 * Manuel (kiradan bağımsız) fatura — PARA: `POST /faturalar/manuel`, `Idempotency-Key` ZORUNLU. KDV ve genel toplam
 * SUNUCUDA hesaplanır (satır bazında kuruşa yuvarlama); istemci yalnız net tutarı ve oranı gönderir. Mantıksal
 * gönderim başına bir anahtar: doğrulama hatası/ağ hatası/oturum düşmesinden sonraki tekrar AYNI anahtarla gider,
 * 2xx ya da `mukerrer` sonrası yenilenir (`formGonderimi`). Hatada form KORUNUR.
 */
@Component({
  selector: 'rc-manual-invoice-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    AramaSecim,
    DocumentNotice,
    FormHatalari,
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
  private readonly toast = inject(ToastServisi);
  private readonly t = ceviriFonksiyonu();

  readonly saved = output<DocumentResult>();
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
  protected readonly notice = signal<FormNotice | null>(null);

  protected readonly form = new FormGroup({
    cari: new FormControl<SecimSecenegi | null>(null, Validators.required),
    netTutar: new FormControl<string | null>(null, Validators.required),
    kdvOrani: new FormControl<VatRate | null>('0.20', Validators.required),
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
  protected readonly submission = formGonderimi();

  constructor() {
    this.form.valueChanges
      .pipe(takeUntilDestroyed(inject(DestroyRef)))
      .subscribe(() => this.dirtyChange.emit(this.form.dirty));
  }

  protected submit(): void {
    this.notice.set(null);
    const body = manualInvoiceRequest(this.form.getRawValue() as ManualInvoiceValue);
    this.submission.gonder(
      this.form,
      (key) => this.api.post<DocumentResult>(`${INVOICES}/manuel`, body, { islemAnahtari: key }),
      {
        esleme: { cariId: 'cari' },
        basarili: (r) => {
          this.toast.basari(this.t('finansBelge.fatura.manuelKesildi', { no: r.no }));
          this.reset();
          this.saved.emit(r);
        },
        hata: (h) => {
          this.notice.set(formNotice(h));
          // Aynı içerik zaten kesilmiş: form temizlenir; farklı içerikte girilen YAZILMADI → form korunur.
          if (h.kod === 'mukerrer') {
            if (h.mevcut?.ayniIcerik) this.reset();
            this.saved.emit({ id: h.mevcut?.id ?? '', no: h.mevcut?.belgeNo ?? '' });
          }
        },
      },
    );
  }

  private reset(): void {
    this.form.reset({ kdvOrani: '0.20' });
    this.submission.kilit.yenile();
    this.dirtyChange.emit(false);
  }
}
