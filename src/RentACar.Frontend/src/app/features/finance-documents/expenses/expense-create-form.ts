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
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { FinanceAccountItem, SelectionItem } from '@core/api/ui-tipleri';
import { formatMoney } from '@core/bicim/bicim';
import { moneySubmission } from '@core/form/money-submission';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { requestContext } from '@core/oturum/request-context';
import { SessionService } from '@core/oturum/session-service';
import { rentalPickSource } from '@features/crm/crm.store';
import { toNumber } from '@features/vehicles/vehicle-model';
import { Alan } from '@shared/form/alan/alan';
import { SearchSelection } from '@shared/form/arama-secim/search-selection';
import {
  type SecimSecenegi,
  serverSelectionSource,
} from '@shared/form/arama-secim/selection-source';
import { TextInput } from '@shared/form/kontroller/text-input';
import { MoneyInput } from '@shared/form/kontroller/money-input';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Selection } from '@shared/form/kontroller/selection';
import { MoneySubmitBar } from '@shared/form/money-submit/money-submit-bar';
import { DatePicker } from '@shared/form/tarih/date-picker';

import {
  CURRENCIES,
  type DocumentResult,
  type ExpenseCreateRequest,
  EXPENSE_PRESETS,
  EXPENSE_TYPES,
  type ExpenseRow,
  type ExpenseType,
  PAYMENT_METHODS,
  type PaymentMethod,
  VAT_LABELS,
  VAT_RATES,
  type VatRate,
} from '../document-model';
import { type ExpenseForm, expenseRequest } from '../document-requests';
import { EXPENSES, recordPath } from '../document.store';
import { currencyMismatch } from './currency-rules';

/**
 * Yeni gider — PARA (`POST /giderler`; Borç Gider(net) + Borç KDV / Alacak Kasa·Banka·Cari(brüt)). KDV ve baz tutar
 * SUNUCUDA (satır bazında kuruşa yuvarlama, KurCozucu); istemci net + oran + (isteğe bağlı açık) kur gönderir.
 * Gönderim çekirdek `MoneySubmission` (uçuşta kilit, belirsizde donmuş gövde, `mevcut`suz 409'da anahtar korunur).
 * Döviz değişince açık kur ve dövizi uymayan kasa/banka hesabı temizlenir (r300 M2: USD kuru EUR'ya gidiyordu).
 * Şubeye bağlı kullanıcıda şube kendi şubesiyle ön-dolu (sunucu kendi şubesini zorunlu tutar).
 */
@Component({
  selector: 'rc-expense-create-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    SearchSelection,
    MoneySubmitBar,
    TextInput,
    MoneyInput,
    Selection,
    DatePicker,
  ],
  templateUrl: './expense-create-form.html',
  styleUrl: '../finance-documents.scss',
})
export class ExpenseCreateForm {
  private readonly api = inject(ApiIstemcisi);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly session = inject(SessionService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = translationFunction();

  readonly accounts = input<readonly FinanceAccountItem[] | undefined>(undefined);
  readonly branches = input<readonly SelectionItem[] | undefined>(undefined);
  readonly saved = output<DocumentResult | null>();
  readonly dirtyChange = output<boolean>();

  protected readonly vehicles = serverSelectionSource('arac');
  protected readonly customers = serverSelectionSource('musteri');
  /** Kira sözleşmesi (`/crm/secim/kira`: OperationsWrite VEYA FinanceWrite, şube kapsamlı; #300). */
  protected readonly rentals = rentalPickSource();
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
  /** Şubeye bağlı kullanıcının şubesi (ön-doldurma; sunucu kapsamı ayrıca zorlar). */
  private readonly ownBranch = computed(() => {
    const s = this.session.ben()?.subeKapsami;
    return s && !s.tumSubeler ? s.subeAd : null;
  });

  protected readonly form = new FormGroup({
    tip: new FormControl<ExpenseType | null>('Genel', Validators.required),
    arac: new FormControl<SecimSecenegi | null>(null),
    cari: new FormControl<SecimSecenegi | null>(null),
    kira: new FormControl<SecimSecenegi | null>(null),
    netTutar: new FormControl<string | null>(null, Validators.required),
    kdvOrani: new FormControl<VatRate | null>('0.20', Validators.required),
    odemeYontemi: new FormControl<PaymentMethod | null>('Nakit', Validators.required),
    doviz: new FormControl<string | null>('TRY'),
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
  /**
   * Tutar girdisinin para simgesi: FORM DEĞERİNDEN (r300b N2). Donmuş deneme `emitEvent:false` ile geri yüklenir;
   * yalnız `valueChanges`'e bağlı sinyal eski dövizi (₺) gösteriyordu.
   */
  protected readonly currency = signal<string | null>('TRY');
  protected readonly submission = moneySubmission<ExpenseCreateRequest>({
    scope: () => 'yeni-gider',
  });

  constructor() {
    this.form.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.dirtyChange.emit(this.form.dirty));
    this.form.controls.doviz.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((currencyCode) => {
        this.currency.set(currencyCode);
        this.currencyChanged(currencyCode);
      });
    this.reset();
    this.submission.restore(this.form);
    this.currency.set(this.form.controls.doviz.value);
  }

  protected submit(): void {
    void this.submission.run<DocumentResult>({
      form: this.form,
      fieldMap: () => ({ aracId: 'arac', cariId: 'cari', kiraId: 'kira' }),
      build: () => ({
        path: EXPENSES,
        body: expenseRequest(this.form.getRawValue() as ExpenseForm),
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

  /** Açık kur yalnız seçildiği dövize aittir; hesap dövizi uymuyorsa hesap da bırakılır. */
  private currencyChanged(currency: string | null): void {
    if (this.form.controls.kur.value !== null) this.form.controls.kur.reset(null);
    const account = (this.accounts() ?? []).find((a) => a.id === this.form.controls.hesapId.value);
    if (account && currencyMismatch(account.doviz, currency))
      this.form.controls.hesapId.reset(null);
  }

  private announce(r: DocumentResult): void {
    this.api
      .get<{ gider: ExpenseRow }>(recordPath(EXPENSES, r.id), {
        context: requestContext({ sessiz: true }),
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (d) =>
          this.toast.basari(
            this.t('finansBelge.gider.kaydedildiTutar', {
              no: d.gider.no,
              tutar: formatMoney(toNumber(d.gider.genelToplam), d.gider.doviz),
            }),
          ),
        error: () => this.toast.basari(this.t('finansBelge.gider.kaydedildi', { no: r.no })),
      });
  }

  private reset(): void {
    this.form.reset({
      tip: 'Genel',
      kdvOrani: '0.20',
      odemeYontemi: 'Nakit',
      doviz: 'TRY',
      sube: this.ownBranch(),
    });
    this.submission.renew();
    this.dirtyChange.emit(false);
  }
}
