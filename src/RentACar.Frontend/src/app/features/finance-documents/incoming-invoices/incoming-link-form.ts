import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  input,
  type OnInit,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import { toApiError } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { WarningBannerService } from '@core/geri-bildirim/warning-banner-service';
import { translationFunction } from '@core/i18n/ceviri';
import { genelGosterilir } from '@core/oturum/session-interceptor';
import { mergeServerValues } from '@features/planlama-ortak/form-yardimcilari';
import { Alan } from '@shared/form/alan/alan';
import { SearchSelection } from '@shared/form/arama-secim/search-selection';
import {
  type SecimSecenegi,
  serverSelectionSource,
} from '@shared/form/arama-secim/selection-source';
import { formSubmission } from '@shared/form/form-submission';
import { FormErrors } from '@shared/form/form-errors';
import { MoneyInput } from '@shared/form/kontroller/money-input';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Selection } from '@shared/form/kontroller/selection';

import {
  EXPENSE_TYPES,
  type ExpenseType,
  type IncomingInvoiceDetail,
  type IncomingInvoiceLinkResult,
} from '../document-model';
import {
  type IncomingLinkForm as LinkValue,
  incomingLinkRequest,
  incomingToLinkForm,
} from '../document-requests';
import { INCOMING, recordPath } from '../document.store';

/**
 * Gelen e-fatura KDV kırılımı + araç/kategori/tedarikçi bağı (`PUT /gelen-efatura/{id}/bag`) — deftere YAZMAZ ama
 * giderleştirmenin girdisidir. TAM DEĞİŞTİRME: `surum` ZORUNLU (detaydan); sunucu kırılım tutarlılığını (Σ matrah =
 * net, Σ KDV = KDV, kademe KDV'si matrah × oran) DENETLER — istemci toplamaz. 409 `cakisma`: güncel kayıt okunur ve
 * KİRLİ forma birleşir (dokunulmayan alan güncellenir, dokunulan korunur, ikisi de değiştiyse işaretlenir); form SİLİNMEZ.
 */
@Component({
  selector: 'rc-incoming-link-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    SearchSelection,
    FormErrors,
    MoneyInput,
    Selection,
  ],
  templateUrl: './incoming-link-form.html',
  styleUrl: '../finance-documents.scss',
})
export class IncomingLinkForm implements OnInit {
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastService);
  private readonly banner = inject(WarningBannerService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = translationFunction();

  readonly invoiceId = input.required<string>();
  readonly saved = output<IncomingInvoiceLinkResult>();
  readonly closed = output<void>();
  readonly dirtyChange = output<boolean>();

  protected readonly vehicles = serverSelectionSource('arac');
  protected readonly customers = serverSelectionSource('musteri');
  /** Aktif gider kategorileri (`/secim/gider-kategorisi`: OperationsWrite VEYA FinanceWrite; #300). */
  protected readonly categories = serverSelectionSource('gider-kategorisi');
  protected readonly typeOptions: readonly SecenekOgesi<ExpenseType>[] = EXPENSE_TYPES.map((x) => ({
    deger: x,
    etiket: this.t(`finansBelge.gider.turler.${x}`),
  }));
  /** Okunan kayıt (sürüm + birleştirme tabanı). `null` iken Kaydet pasif (sürüm okunmadan PUT gitmez). */
  protected readonly base = signal<IncomingInvoiceDetail | null>(null);

  protected readonly form = new FormGroup({
    kdv20Matrah: new FormControl<string | null>(null),
    kdv20: new FormControl<string | null>(null),
    kdv10Matrah: new FormControl<string | null>(null),
    kdv10: new FormControl<string | null>(null),
    kdv1Matrah: new FormControl<string | null>(null),
    kdv1: new FormControl<string | null>(null),
    kdv0Matrah: new FormControl<string | null>(null),
    arac: new FormControl<SecimSecenegi | null>(null),
    kategori: new FormControl<SecimSecenegi | null>(null),
    cari: new FormControl<SecimSecenegi | null>(null),
    giderTipi: new FormControl<ExpenseType | null>(null),
  });
  protected readonly submission = formSubmission();

  constructor() {
    this.form.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.dirtyChange.emit(this.form.dirty));
  }

  ngOnInit(): void {
    this.read();
  }

  protected save(): void {
    const base = this.base();
    if (base === null) return;
    const body = incomingLinkRequest(this.form.getRawValue() as LinkValue, base.surum);
    this.submission.gonder(
      this.form,
      (key) =>
        this.api.put<IncomingInvoiceLinkResult>(
          recordPath(INCOMING, this.invoiceId(), '/bag'),
          body,
          {
            islemAnahtari: key,
          },
        ),
      {
        esleme: { aracId: 'arac', cariId: 'cari', giderKategoriId: 'kategori' },
        basarili: (r) => {
          this.toast.basari(this.t('finansBelge.gelen.baglandi'));
          this.dirtyChange.emit(false);
          this.saved.emit(r);
          this.read();
        },
        hata: (h) => {
          if (h.kod === 'cakisma') this.read();
        },
      },
    );
  }

  private read(): void {
    this.api
      .get<IncomingInvoiceDetail>(recordPath(INCOMING, this.invoiceId()))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (d) => this.arrived(d),
        error: (raw: unknown) => {
          const error = toApiError(raw);
          if (!genelGosterilir(error)) this.toast.hata(error.detay);
        },
      });
  }

  /** Temiz form sıfırlanır; kirli formda dokunulan alan korunur, çakışan işaretlenir (taban = önceki okuma). */
  private arrived(d: IncomingInvoiceDetail): void {
    const fresh = incomingToLinkForm(d.fatura);
    const previous = this.base();
    if (previous === null || !this.form.dirty) {
      this.form.reset({ ...fresh });
    } else {
      const conflicts = mergeServerValues(
        this.form,
        { ...fresh },
        { ...incomingToLinkForm(previous.fatura) },
        this.t('finansBelge.cakismaAlan'),
      );
      if (conflicts.length > 0)
        this.banner.show({
          tur: 'uyari',
          mesaj: this.t('finansBelge.cakismaBant', { sayi: conflicts.length }),
          kod: 'cakisma',
        });
    }
    this.base.set(d);
  }
}
