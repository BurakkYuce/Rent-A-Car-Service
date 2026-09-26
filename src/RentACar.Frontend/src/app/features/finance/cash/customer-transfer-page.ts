import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, Validators } from '@angular/forms';

import { moneySubmission } from '@core/form/money-submission';
import { ApiIstemcisi, type QueryParameters } from '@core/api/api-istemcisi';
import { type UnsavedChangesOwner, pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import { bugun } from '@core/form/tarih-girdisi';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { TemelStore } from '@core/veri/temel-store';
import { currencyCode } from '@features/kira-formu/finans-paneli/finans-modeli';
import {
  type SecimSecenegi,
  serverSelectionSource,
} from '@shared/form/arama-secim/selection-source';

import {
  type CustomerTransferRequest,
  type CustomerTransferRow,
  customerTransferBody,
  financePath,
  queryParams,
} from '../finance-model';
import { CURRENCY_OPTIONS, FIN_COMMON, clearRateOnCurrencyChange } from '../finance-shared';

/**
 * Cari ↔ Cari Virman (`/app/cari-virman`, Blazor `CariVirman.razor`): kaynak cari alacaklanır (bakiye ↓), hedef cari
 * borçlanır (bakiye ↑) — `POST finans/cari-virman` (E07). Geçmiş künye kaydı olan virmanları gösterir (tutar
 * DEFTERDEN). Kapalı muhasebe dönemine virman atılamaz (sunucu `tarih` alan hatası).
 */
@Component({
  selector: 'rc-customer-transfer-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...FIN_COMMON],
  providers: [FetchPolicy],
  templateUrl: './customer-transfer-page.html',
  styleUrl: '../finance.scss',
})
export class CustomerTransferPage implements UnsavedChangesOwner {
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastService);
  private readonly t = translationFunction();
  protected readonly customers = serverSelectionSource('musteri');
  protected readonly branches = serverSelectionSource('sube');
  protected readonly currencyOptions = CURRENCY_OPTIONS;
  protected readonly action = moneySubmission<CustomerTransferRequest>({
    scope: () => 'cari-virman',
  });

  protected readonly history = new TemelStore(
    (p: QueryParameters) =>
      this.api.get<readonly CustomerTransferRow[]>(financePath('/cari-virmanlar'), {
        parametreler: p,
      }),
    { oncekiVeriyiKoru: true },
  );
  private readonly params = signal<QueryParameters>({});
  protected readonly filtered = computed(() => Object.keys(this.params()).length > 0);

  protected readonly form = new FormGroup({
    kaynakCari: new FormControl<SecimSecenegi | null>(null, Validators.required),
    hedefCari: new FormControl<SecimSecenegi | null>(null, Validators.required),
    tutar: new FormControl<string | null>(null, Validators.required),
    doviz: new FormControl<string | null>('TRY', Validators.required),
    kur: new FormControl<number | null>(null),
    tarih: new FormControl<string | null>(bugun()),
    vade: new FormControl<string | null>(null),
    makbuzNo: new FormControl<string | null>(null, Validators.maxLength(32)),
    sube: new FormControl<SecimSecenegi | null>(null),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
  });
  protected readonly filter = new FormGroup({
    cari: new FormControl<SecimSecenegi | null>(null),
    ara: new FormControl<string | null>(null, Validators.maxLength(100)),
    bas: new FormControl<string | null>(null),
    bit: new FormControl<string | null>(null),
  });
  protected readonly currency = toSignal(this.form.controls.doviz.valueChanges, {
    initialValue: this.form.controls.doviz.value,
  });
  protected readonly isForeign = computed(() => currencyCode(this.currency()) !== 'TRY');

  constructor() {
    pageLeaveGuard(() => this.hasUnsavedChanges());
    // Sonucu bilinmeyen işlem (sayfa kapanıp açıldıysa) aynı gövde + anahtarla KİLİTLİ geri gelir.
    this.action.restore(this.form);
    clearRateOnCurrencyChange(this.form.controls.doviz, this.form.controls.kur);
    inject(FetchPolicy).connect({ parametre: this.params, yukle: (p) => this.history.yukle(p) });
  }

  hasUnsavedChanges(): boolean {
    return this.action.pending() || this.form.dirty;
  }

  unsavedChangesMessage(): string | null {
    return this.action.pending() ? this.t('finans.islem.terkMesaji') : null;
  }

  protected applyFilter(): void {
    const v = this.filter.getRawValue();
    this.params.set(queryParams({ cariId: v.cari?.id, ara: v.ara, bas: v.bas, bit: v.bit }));
  }

  protected clearFilter(): void {
    this.filter.reset();
    this.params.set({});
  }

  protected submit(): void {
    void this.action.run<{ id: string }>({
      form: this.form,
      fieldMap: () => ({ kaynakCariId: 'kaynakCari', hedefCariId: 'hedefCari' }),
      build: () => {
        const v = this.form.getRawValue();
        const body = customerTransferBody({
          ...v,
          kaynakCariId: v.kaynakCari?.id ?? null,
          hedefCariId: v.hedefCari?.id ?? null,
          sube: v.sube?.etiket ?? null,
        });
        return {
          path: financePath('/cari-virman'),
          body,
          content: { tutar: body.tutar, doviz: body.doviz ?? 'TRY' },
        };
      },
      success: () => {
        this.toast.basari(this.t('finans.cariVirman.kaydedildi'));
        this.resetForm();
      },
      afterDuplicate: () => this.resetForm(),
      settled: () => this.history.yenile(),
    });
  }

  private resetForm(): void {
    this.form.reset({ doviz: 'TRY', tarih: bugun() });
  }
}
