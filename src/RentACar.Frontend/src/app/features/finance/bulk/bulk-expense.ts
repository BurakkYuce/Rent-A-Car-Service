import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormArray, FormControl, FormGroup, Validators } from '@angular/forms';

import { paraBicimle } from '@core/bicim/bicim';
import {
  type KaydedilmemisDegisiklikSahibi,
  sayfaTerkKorumasi,
} from '@core/form/kaydedilmemis-degisiklik';
import { yeniIslemAnahtari } from '@core/form/gonderim-kilidi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { type SecimSecenegi, sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';

import {
  type BulkExpenseRequest,
  type BulkPostingResult,
  EXPENSE_TYPES,
  PAYMENT_METHODS,
  type PaymentMethod,
  bulkExpenseBody,
  financePath,
} from '../finance-model';
import { AccountList, FIN_COMMON, toAmount } from '../finance-shared';
import { moneyAction } from '../money-action';

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
export class BulkExpense implements KaydedilmemisDegisiklikSahibi {
  private readonly toast = inject(ToastServisi);
  private readonly t = ceviriFonksiyonu();
  protected readonly accounts = inject(AccountList);
  protected readonly customers = sunucuSecimKaynagi('musteri');
  protected readonly vehicles = sunucuSecimKaynagi('arac');
  protected readonly action = moneyAction<BulkExpenseRequest>();
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
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
    this.accounts.load();
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.action.pending() || this.form.dirty;
  }

  kaydedilmemisDegisiklikMesaji(): string | null {
    return this.action.pending() ? this.t('finans.islem.terkMesaji') : null;
  }

  protected get rows(): readonly ExpenseRow[] {
    return this.form.controls.satirlar.controls;
  }

  protected addRow(): void {
    this.form.controls.satirlar.push(this.newRow());
    this.form.markAsDirty();
  }

  protected removeRow(index: number): void {
    if (this.rows.length <= 1) return;
    this.form.controls.satirlar.removeAt(index);
    this.form.markAsDirty();
  }

  protected submit(): void {
    const map: Record<string, string> = { cariId: 'cari' };
    this.rows.forEach((_, i) => {
      map[`satirlar[${i}].netTutar`] = `satirlar.${i}.netTutar`;
      map[`satirlar[${i}].aracId`] = `satirlar.${i}.arac`;
      map[`satirlar[${i}].aciklama`] = `satirlar.${i}.aciklama`;
    });
    void this.action.run<BulkPostingResult>({
      form: this.form,
      fieldMap: map,
      build: () => {
        const v = this.form.getRawValue();
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
          content: { tutar: null, doviz: 'TRY', hesap: body.odemeYontemi },
        };
      },
      success: (r) => {
        this.toast.basari(
          this.t('finans.topluGider.kaydedildi', {
            adet: r.adet,
            toplam: paraBicimle(toAmount(r.toplam)),
          }),
        );
        this.resetForm();
      },
      afterDuplicate: () => this.resetForm(),
      settled: () => undefined,
    });
  }

  protected abandon(): void {
    void this.action.abandon(() => undefined);
  }

  private newRow(): ExpenseRow {
    return new FormGroup({
      id: new FormControl<string>(yeniIslemAnahtari(), { nonNullable: true }),
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
