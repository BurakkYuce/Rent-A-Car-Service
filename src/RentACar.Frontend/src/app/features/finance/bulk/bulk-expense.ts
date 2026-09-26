import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormArray, FormControl, FormGroup, Validators } from '@angular/forms';

import { moneySubmission } from '@core/form/money-submission';
import { formatMoney } from '@core/bicim/bicim';
import { type UnsavedChangesOwner, pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import { newOperationKey } from '@core/form/submit-lock';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import {
  type SecimSecenegi,
  serverSelectionSource,
} from '@shared/form/arama-secim/selection-source';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';

import {
  type BulkExpenseRequest,
  type BulkPostingResult,
  EXPENSE_TYPES,
  PAYMENT_METHODS,
  type PaymentMethod,
  bulkExpenseBody,
  financePath,
  rowErrorMap,
} from '../finance-model';
import { AccountList, FIN_COMMON, toAmount } from '../finance-shared';

type ExpenseRow = FormGroup<{
  id: FormControl<string>;
  netTutar: FormControl<string | null>;
  aciklama: FormControl<string | null>;
  arac: FormControl<SecimSecenegi | null>;
}>;

/**
 * Toplu Gider (`/app/toplu-gider`, Blazor `TopluGider.razor`): tek seferde çok gider kalemi, ATOMİK + dengeli defter
 * (`POST finans/toplu-gider`, E22 — parti anahtarı `Idempotency-Key`, tekrar 409). Tip/ödeme/KDV/cari/vade/hesap tüm
 * kalemler için ortak; araç SATIR bazlı (her satır ayrı gider, tek gider araçlara bölünmez). Açık Hesap ödemede
 * tedarikçi cari zorunlu (sunucu). Vade ve Hesap No belge bilgisidir. Satır listesi gönderimde donar.
 */
@Component({
  selector: 'rc-bulk-expense',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...FIN_COMMON],
  providers: [AccountList],
  templateUrl: './bulk-expense.html',
  styleUrl: '../finance.scss',
})
export class BulkExpense implements UnsavedChangesOwner {
  private readonly toast = inject(ToastService);
  private readonly t = translationFunction();
  protected readonly accounts = inject(AccountList);
  protected readonly customers = serverSelectionSource('musteri');
  protected readonly vehicles = serverSelectionSource('arac');
  protected readonly action = moneySubmission<BulkExpenseRequest>({ scope: () => 'toplu-gider' });
  /** Gönderilen (donmuş) kopyadaki satırların kimlikleri, gövdedeki sırayla — sunucu satır hataları buna göre eşlenir. */
  private sentRowIds: readonly string[] = [];
  protected readonly typeOptions: readonly SecenekOgesi<string>[] = EXPENSE_TYPES.map((x) => ({
    deger: x,
    etiket: this.t(`finans.topluGider.tipler.${x}`),
  }));
  protected readonly methodOptions: readonly SecenekOgesi<PaymentMethod>[] = PAYMENT_METHODS.map(
    (x) => ({
      deger: x,
      etiket: this.t(`finans.topluGider.yontemler.${x}`),
    }),
  );

  protected readonly form = new FormGroup({
    tip: new FormControl<string | null>('Genel', Validators.required),
    odemeYontemi: new FormControl<PaymentMethod | null>('Nakit', Validators.required),
    kdvOrani: new FormControl<number | null>(0.2, [
      Validators.required,
      Validators.min(0),
      Validators.max(1),
    ]),
    cari: new FormControl<SecimSecenegi | null>(null),
    vade: new FormControl<string | null>(null),
    finansalHesapId: new FormControl<string | null>(null),
    satirlar: new FormArray<ExpenseRow>([this.newRow()]),
  });

  constructor() {
    pageLeaveGuard(() => this.hasUnsavedChanges());
    this.accounts.load();
    // Sonucu bilinmeyen toplu işlem (sayfa kapanıp açıldıysa) AYNI satırlarla + anahtarla KİLİTLİ geri gelir.
    this.action.restore(this.form, (value) => {
      const v = value as ReturnType<typeof this.form.getRawValue>;
      this.form.controls.satirlar.clear({ emitEvent: false });
      v.satirlar.forEach(() =>
        this.form.controls.satirlar.push(this.newRow(), { emitEvent: false }),
      );
      this.form.reset(v, { emitEvent: false });
      this.sentRowIds = v.satirlar.map((r) => r.id);
    });
  }

  hasUnsavedChanges(): boolean {
    return this.action.pending() || this.form.dirty;
  }

  unsavedChangesMessage(): string | null {
    return this.action.pending() ? this.t('finans.islem.terkMesaji') : null;
  }

  protected get rows(): readonly ExpenseRow[] {
    return this.form.controls.satirlar.controls;
  }

  protected addRow(): void {
    if (this.action.pending()) return;
    this.form.controls.satirlar.push(this.newRow());
    this.form.markAsDirty();
  }

  protected removeRow(index: number): void {
    if (this.rows.length <= 1 || this.action.pending()) return;
    this.form.controls.satirlar.removeAt(index);
    this.form.markAsDirty();
  }

  protected submit(): void {
    void this.action.run<BulkPostingResult>({
      form: this.form,
      fieldMap: () => ({
        cariId: 'cari',
        ...rowErrorMap(
          this.sentRowIds,
          this.rows.map((r) => r.controls.id.value),
          { netTutar: 'netTutar', aracId: 'arac', aciklama: 'aciklama' },
        ),
      }),
      build: () => {
        const v = this.form.getRawValue();
        this.sentRowIds = v.satirlar.map((r) => r.id);
        const body = bulkExpenseBody(
          v.satirlar.map((r) => ({
            netTutar: r.netTutar,
            aciklama: r.aciklama,
            aracId: r.arac?.id ?? null,
          })),
          { ...v, cariId: v.cari?.id ?? null },
        );
        return {
          path: financePath('/toplu-gider'),
          body,
          content: { tutar: null, doviz: 'TRY' },
        };
      },
      success: (r) => {
        this.toast.basari(
          this.t('finans.topluGider.kaydedildi', {
            adet: r.adet,
            toplam: formatMoney(toAmount(r.toplam)),
          }),
        );
        this.resetForm();
      },
      afterDuplicate: () => this.resetForm(),
    });
  }

  private newRow(): ExpenseRow {
    return new FormGroup({
      id: new FormControl<string>(newOperationKey(), { nonNullable: true }),
      netTutar: new FormControl<string | null>(null, Validators.required),
      aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
      arac: new FormControl<SecimSecenegi | null>(null),
    });
  }

  private resetForm(): void {
    const v = this.form.getRawValue();
    this.form.controls.satirlar.clear();
    this.form.controls.satirlar.push(this.newRow());
    this.form.reset({
      tip: v.tip,
      odemeYontemi: v.odemeYontemi,
      kdvOrani: v.kdvOrani,
      cari: v.cari,
      finansalHesapId: v.finansalHesapId,
    });
  }
}
