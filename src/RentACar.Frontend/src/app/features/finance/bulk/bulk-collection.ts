import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormArray, FormControl, FormGroup, Validators } from '@angular/forms';

import { moneySubmission } from '@core/form/money-submission';
import { paraBicimle } from '@core/bicim/bicim';
import {
  type KaydedilmemisDegisiklikSahibi,
  sayfaTerkKorumasi,
} from '@core/form/kaydedilmemis-degisiklik';
import { yeniIslemAnahtari } from '@core/form/gonderim-kilidi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { type SecimSecenegi, sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';

import {
  type AccountKind,
  type BulkCollectionRequest,
  type BulkPostingResult,
  bulkCollectionBody,
  financePath,
  rowErrorMap,
} from '../finance-model';
import {
  AccountList,
  CHANNEL_OPTIONS,
  FIN_COMMON,
  clearAccountOnKindChange,
  kindOptions,
  toAmount,
} from '../finance-shared';

type CollectionRow = FormGroup<{
  id: FormControl<string>;
  cari: FormControl<SecimSecenegi | null>;
  tutar: FormControl<string | null>;
  aciklama: FormControl<string | null>;
}>;

/**
 * Toplu Tahsilat — çok cari (`/app/toplu-tahsilat`, Blazor `TopluTahsilat.razor`): satır başına cari + tutar (TRY).
 * ATOMİK — bir satır geçersizse hiçbiri yazılmaz; her satır ayrı dengeli kayıt + ayrı No. `POST finans/toplu-tahsilat`
 * (E03): parti anahtarı = `Idempotency-Key`, satır anahtarı sunucuda `RowKey(parti, i)`. Satır listesi gönderimde
 * DONAR: kayıp yanıttan sonraki tekrar AYNI satırlarla gider (ikinci parti yazılmaz). Blazor'daki "cariId;tutar"
 * metin kutusunun yerine satır düzenleyici (aynı alanlar; cari kimliği aranarak seçilir).
 */
@Component({
  selector: 'rc-bulk-collection',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...FIN_COMMON],
  providers: [AccountList],
  templateUrl: './bulk-collection.html',
  styleUrl: '../finance.scss',
})
export class BulkCollection implements KaydedilmemisDegisiklikSahibi {
  private readonly toast = inject(ToastServisi);
  private readonly t = ceviriFonksiyonu();
  protected readonly accounts = inject(AccountList);
  protected readonly customers = sunucuSecimKaynagi('musteri');
  protected readonly kindOptions = kindOptions(this.t);
  protected readonly channelOptions = CHANNEL_OPTIONS;
  protected readonly action = moneySubmission<BulkCollectionRequest>({
    scope: () => 'toplu-tahsilat',
  });
  /** Gönderilen (donmuş) kopyadaki satırların kimlikleri, gövdedeki sırayla — sunucu satır hataları buna göre eşlenir. */
  private sentRowIds: readonly string[] = [];

  protected readonly form = new FormGroup({
    hesap: new FormControl<AccountKind | null>('Kasa', Validators.required),
    hesapId: new FormControl<string | null>(null),
    kanal: new FormControl<string | null>('Masaüstü'),
    satirlar: new FormArray<CollectionRow>([this.newRow()]),
  });
  private readonly accountKind = toSignal(this.form.controls.hesap.valueChanges, {
    initialValue: this.form.controls.hesap.value,
  });
  protected readonly accountOptions = computed(() => this.accounts.options(this.accountKind()));

  constructor() {
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
    this.accounts.load();
    clearAccountOnKindChange(this.accounts, this.form.controls.hesap, this.form.controls.hesapId);
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

  kaydedilmemisDegisiklikVar(): boolean {
    return this.action.pending() || this.form.dirty;
  }

  kaydedilmemisDegisiklikMesaji(): string | null {
    return this.action.pending() ? this.t('finans.islem.terkMesaji') : null;
  }

  protected get rows(): readonly CollectionRow[] {
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
      fieldMap: () =>
        rowErrorMap(this.sentRowIds, this.rowIds(), {
          cariId: 'cari',
          tutar: 'tutar',
          aciklama: 'aciklama',
        }),
      build: () => {
        const v = this.form.getRawValue();
        this.sentRowIds = v.satirlar.map((r) => r.id);
        const body = bulkCollectionBody(
          v.satirlar.map((r) => ({
            cariId: r.cari?.id ?? null,
            tutar: r.tutar,
            aciklama: r.aciklama,
          })),
          v,
        );
        return {
          path: financePath('/toplu-tahsilat'),
          body,
          content: { tutar: null, doviz: 'TRY' },
        };
      },
      success: (r) => {
        this.toast.basari(
          this.t('finans.topluTahsilat.kaydedildi', {
            adet: r.adet,
            toplam: paraBicimle(toAmount(r.toplam)),
          }),
        );
        this.resetForm();
      },
      afterDuplicate: () => this.resetForm(),
    });
  }

  private rowIds(): string[] {
    return this.rows.map((r) => r.controls.id.value);
  }

  private newRow(): CollectionRow {
    return new FormGroup({
      id: new FormControl<string>(yeniIslemAnahtari(), { nonNullable: true }),
      cari: new FormControl<SecimSecenegi | null>(null, Validators.required),
      tutar: new FormControl<string | null>(null, Validators.required),
      aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
    });
  }

  private resetForm(): void {
    const v = this.form.getRawValue();
    this.form.controls.satirlar.clear();
    this.form.controls.satirlar.push(this.newRow());
    this.form.reset({ hesap: v.hesap, hesapId: v.hesapId, kanal: v.kanal });
  }
}
