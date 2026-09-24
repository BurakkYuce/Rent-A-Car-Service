import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { FinansHesapOgesi, SecimOgesi } from '@core/api/ui-tipleri';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { Alan } from '@shared/form/alan/alan';
import { AramaSecim } from '@shared/form/arama-secim/arama-secim';
import { type SecimSecenegi, sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { ParaGirdisi } from '@shared/form/kontroller/para-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';

import { DocumentNotice } from '../document-notice';
import {
  CURRENCIES,
  type DocumentResult,
  EXPENSE_PRESETS,
  EXPENSE_TYPES,
  type ExpenseType,
  PAYMENT_METHODS,
  type PaymentMethod,
  VAT_LABELS,
  VAT_RATES,
  type VatRate,
} from '../document-model';
import {
  type ExpenseForm,
  type FormNotice,
  expenseRequest,
  formNotice,
} from '../document-requests';
import { EXPENSES } from '../document.store';

const EMPTY = { tip: 'Genel', kdvOrani: '0.20', odemeYontemi: 'Nakit', doviz: 'TRY' } as const;

/**
 * Yeni gider — PARA (`POST /giderler`; Borç Gider(net) + Borç KDV / Alacak Kasa·Banka·Cari(brüt)). KDV ve baz tutar
 * SUNUCUDA (satır bazında kuruşa yuvarlama, KurCozucu); istemci net + oran + (isteğe bağlı açık) kur gönderir.
 * `Idempotency-Key` ZORUNLU: mantıksal gönderim başına bir anahtar, hata/oturum düşmesi sonrası tekrar AYNI anahtarla.
 */
@Component({
  selector: 'rc-expense-create-form',
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
    Secim,
    TarihSecici,
  ],
  templateUrl: './expense-create-form.html',
  styleUrl: '../finance-documents.scss',
})
export class ExpenseCreateForm {
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastServisi);
  private readonly t = ceviriFonksiyonu();

  readonly accounts = input<readonly FinansHesapOgesi[] | undefined>(undefined);
  readonly branches = input<readonly SecimOgesi[] | undefined>(undefined);
  readonly saved = output<DocumentResult>();
  readonly dirtyChange = output<boolean>();

  protected readonly vehicles = sunucuSecimKaynagi('arac');
  protected readonly customers = sunucuSecimKaynagi('musteri');
  protected readonly presets = EXPENSE_PRESETS;
  protected readonly typeOptions: readonly SecenekOgesi<ExpenseType>[] = EXPENSE_TYPES.map((x) => ({
    deger: x,
    etiket: this.t(`finansBelge.gider.turler.${x}`),
  }));
  protected readonly methodOptions: readonly SecenekOgesi<PaymentMethod>[] = PAYMENT_METHODS.map(
    (x) => ({ deger: x, etiket: this.t(`finansBelge.odemeYontemleri.${x}`) }),
  );
  protected readonly vatOptions: readonly SecenekOgesi<VatRate>[] = VAT_RATES.map((v) => ({
    deger: v,
    etiket: VAT_LABELS[v],
  }));
  protected readonly currencyOptions: readonly SecenekOgesi<string>[] = CURRENCIES.map((c) => ({
    deger: c,
    etiket: c,
  }));
  protected readonly accountOptions = computed<readonly SecenekOgesi<string>[]>(() =>
    (this.accounts() ?? []).map((a) => ({ deger: a.id, etiket: `${a.etiket} (${a.tur})` })),
  );
  protected readonly notice = signal<FormNotice | null>(null);

  protected readonly form = new FormGroup({
    tip: new FormControl<ExpenseType | null>(EMPTY.tip, Validators.required),
    arac: new FormControl<SecimSecenegi | null>(null),
    cari: new FormControl<SecimSecenegi | null>(null),
    netTutar: new FormControl<string | null>(null, Validators.required),
    kdvOrani: new FormControl<VatRate | null>(EMPTY.kdvOrani, Validators.required),
    odemeYontemi: new FormControl<PaymentMethod | null>(EMPTY.odemeYontemi, Validators.required),
    doviz: new FormControl<string | null>(EMPTY.doviz),
    kur: new FormControl<string | null>(null),
    hesapId: new FormControl<string | null>(null),
    tarih: new FormControl<string | null>(null),
    odemeTarihi: new FormControl<string | null>(null),
    vade: new FormControl<string | null>(null),
    hazirAciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
    sube: new FormControl<string | null>(null, Validators.maxLength(64)),
    evrakNo: new FormControl<string | null>(null, Validators.maxLength(64)),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
  });
  protected readonly currency = toSignal(this.form.controls.doviz.valueChanges, {
    initialValue: this.form.controls.doviz.value,
  });
  protected readonly submission = formGonderimi();

  constructor() {
    this.form.valueChanges
      .pipe(takeUntilDestroyed(inject(DestroyRef)))
      .subscribe(() => this.dirtyChange.emit(this.form.dirty));
  }

  protected submit(): void {
    this.notice.set(null);
    const body = expenseRequest(this.form.getRawValue() as ExpenseForm);
    this.submission.gonder(
      this.form,
      (key) => this.api.post<DocumentResult>(EXPENSES, body, { islemAnahtari: key }),
      {
        esleme: { aracId: 'arac', cariId: 'cari' },
        basarili: (r) => {
          this.toast.basari(this.t('finansBelge.gider.kaydedildi', { no: r.no }));
          this.reset();
          this.saved.emit(r);
        },
        hata: (h) => {
          this.notice.set(formNotice(h));
          if (h.kod === 'mukerrer') {
            if (h.mevcut?.ayniIcerik) this.reset();
            this.saved.emit({ id: h.mevcut?.id ?? '', no: h.mevcut?.belgeNo ?? '' });
          }
        },
      },
    );
  }

  private reset(): void {
    this.form.reset({ ...EMPTY });
    this.submission.kilit.yenile();
    this.dirtyChange.emit(false);
  }
}
