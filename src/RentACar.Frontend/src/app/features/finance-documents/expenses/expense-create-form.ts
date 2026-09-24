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
import type { FinansHesapOgesi, SecimOgesi } from '@core/api/ui-tipleri';
import { paraBicimle } from '@core/bicim/bicim';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { toNumber } from '@features/vehicles/vehicle-model';
import { Alan } from '@shared/form/alan/alan';
import { AramaSecim } from '@shared/form/arama-secim/arama-secim';
import { type SecimSecenegi, sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { ParaGirdisi } from '@shared/form/kontroller/para-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';

import {
  CURRENCIES,
  type DocumentResult,
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
import { DocumentSubmission } from '../document-submission';
import { DocumentSubmitBar } from '../document-submit-bar';
import { EXPENSES, recordPath } from '../document.store';
import { currencyMismatch } from './currency-rules';

/**
 * Yeni gider — PARA (`POST /giderler`; Borç Gider(net) + Borç KDV / Alacak Kasa·Banka·Cari(brüt)). KDV ve baz tutar
 * SUNUCUDA (satır bazında kuruşa yuvarlama, KurCozucu); istemci net + oran + (isteğe bağlı açık) kur gönderir.
 * Gönderim {@link DocumentSubmission} (uçuşta kilit, belirsizde donmuş gövde, `mevcut`suz 409'da anahtar korunur).
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
    AramaSecim,
    DocumentSubmitBar,
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
  private readonly confirm = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly session = inject(OturumServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();

  readonly accounts = input<readonly FinansHesapOgesi[] | undefined>(undefined);
  readonly branches = input<readonly SecimOgesi[] | undefined>(undefined);
  readonly saved = output<DocumentResult | null>();
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
  /** Şubeye bağlı kullanıcının şubesi (ön-doldurma; sunucu kapsamı ayrıca zorlar). */
  private readonly ownBranch = computed(() => {
    const s = this.session.ben()?.subeKapsami;
    return s && !s.tumSubeler ? s.subeAd : null;
  });

  protected readonly form = new FormGroup({
    tip: new FormControl<ExpenseType | null>('Genel', Validators.required),
    arac: new FormControl<SecimSecenegi | null>(null),
    cari: new FormControl<SecimSecenegi | null>(null),
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
  protected readonly submission = new DocumentSubmission(this.form, () => 'yeni-gider', {
    aracId: 'arac',
    cariId: 'cari',
  });

  constructor() {
    this.form.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.dirtyChange.emit(this.form.dirty));
    this.form.controls.doviz.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((doviz) => {
        this.currency.set(doviz);
        this.currencyChanged(doviz);
      });
    this.reset();
    this.submission.restore();
    this.currency.set(this.form.controls.doviz.value);
  }

  protected submit(): void {
    this.submission.submit<DocumentResult>(
      EXPENSES,
      () => expenseRequest(this.form.getRawValue() as ExpenseForm),
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

  /** Açık kur yalnız seçildiği dövize aittir; hesap dövizi uymuyorsa hesap da bırakılır. */
  private currencyChanged(doviz: string | null): void {
    if (this.form.controls.kur.value !== null) this.form.controls.kur.reset(null);
    const account = (this.accounts() ?? []).find((a) => a.id === this.form.controls.hesapId.value);
    if (account && currencyMismatch(account.doviz, doviz)) this.form.controls.hesapId.reset(null);
  }

  private announce(r: DocumentResult): void {
    this.api
      .get<{ gider: ExpenseRow }>(recordPath(EXPENSES, r.id), {
        context: istekBaglami({ sessiz: true }),
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (d) =>
          this.toast.basari(
            this.t('finansBelge.gider.kaydedildiTutar', {
              no: d.gider.no,
              tutar: paraBicimle(toNumber(d.gider.genelToplam), d.gider.doviz),
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
