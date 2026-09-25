import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';

import {
  type KaydedilmemisDegisiklikSahibi,
  sayfaTerkKorumasi,
} from '@core/form/kaydedilmemis-degisiklik';
import { moneySubmission } from '@core/form/money-submission';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { dovizKodu } from '@features/kira-formu/finans-paneli/finans-modeli';
import { type SecimSecenegi, sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';

import {
  type AdjustmentDirection,
  type AdjustmentFormValue,
  type BalanceAdjustmentRequest,
  adjustmentBody,
  financePath,
} from '../finance-model';
import {
  CURRENCY_OPTIONS,
  CustomerBalanceSource,
  FIN_COMMON,
  balanceSide,
  clearRateOnCurrencyChange,
  followCustomerQuery,
  labelFromData,
} from '../finance-shared';

/**
 * Bakiye Düzeltme (`/app/finans/bakiye-duzeltme`, Blazor `BakiyeDuzeltme.razor`): Kasa/Banka'ya DOKUNMADAN cari
 * bakiyesi (karşı bacak Muhasebe Düzeltmesi; gelir-gider raporlarına girmez). Silinemez — ters yönde ikinci düzeltme.
 * `POST finans/bakiye-duzeltme` (E13: aynı içerik 200 aynı id; farklı 409). Onaylı.
 */
@Component({
  selector: 'rc-balance-adjustment-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...FIN_COMMON],
  providers: [FetchPolicy, CustomerBalanceSource],
  templateUrl: './balance-adjustment-page.html',
  styleUrl: '../finance.scss',
})
export class BalanceAdjustmentPage implements KaydedilmemisDegisiklikSahibi {
  protected readonly balance = inject(CustomerBalanceSource).store;
  private readonly toast = inject(ToastServisi);
  private readonly confirm = inject(OnayServisi);
  private readonly t = ceviriFonksiyonu();
  protected readonly customers = sunucuSecimKaynagi('musteri');
  protected readonly customer = new FormControl<SecimSecenegi | null>(null);
  protected readonly cariId = signal<string | null>(
    inject(ActivatedRoute).snapshot.queryParamMap.get('cariId'),
  );
  protected readonly side = computed(() => balanceSide(this.balance.veri()?.bakiye));
  protected readonly action = moneySubmission<BalanceAdjustmentRequest>({
    scope: () => `bakiye-duzeltme:${this.cariId() ?? ''}`,
  });

  protected readonly form = new FormGroup({
    yon: new FormControl<AdjustmentDirection | null>('Alacaklandir', Validators.required),
    tutar: new FormControl<string | null>(null, Validators.required),
    doviz: new FormControl<string | null>('TRY', Validators.required),
    kur: new FormControl<number | null>(null),
    tarih: new FormControl<string | null>(null),
    vade: new FormControl<string | null>(null),
    makbuzNo: new FormControl<string | null>(null, Validators.maxLength(32)),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(470)),
  });
  protected readonly directionOptions: readonly SecenekOgesi<AdjustmentDirection>[] = [
    { deger: 'Alacaklandir', etiket: this.t('finans.duzeltme.alacaklandir') },
    { deger: 'Borclandir', etiket: this.t('finans.duzeltme.borclandir') },
  ];
  protected readonly currencyOptions = CURRENCY_OPTIONS;
  protected readonly currency = toSignal(this.form.controls.doviz.valueChanges, {
    initialValue: this.form.controls.doviz.value,
  });
  protected readonly isForeign = computed(() => dovizKodu(this.currency()) !== 'TRY');

  constructor() {
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
    // Sonucu bilinmeyen işlem (sayfa kapanıp açıldıysa) aynı gövde + anahtarla KİLİTLİ geri gelir.
    this.action.restore(this.form);
    clearRateOnCurrencyChange(this.form.controls.doviz, this.form.controls.kur);
    this.customer.valueChanges.pipe(takeUntilDestroyed()).subscribe((c) => {
      if (c && c.id !== this.cariId()) this.cariId.set(c.id);
    });
    effect(() => {
      const locked = this.action.pending();
      untracked(() => (locked ? this.customer.disable() : this.customer.enable()));
    });
    followCustomerQuery(this.cariId, this.customer, () => this.action.pending(), {
      dirty: () => this.form.dirty,
      discard: () => this.resetForm(),
    });
    effect(() => {
      const b = this.balance.veri();
      const id = this.cariId();
      untracked(() => labelFromData(this.customer, id, b));
    });
    inject(FetchPolicy).baglan({
      parametre: this.cariId.asReadonly(),
      yukle: (id) => (id ? this.balance.yukle(id) : this.balance.sifirla()),
      sifirla: () => this.balance.sifirla(),
    });
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.action.pending() || this.form.dirty;
  }

  kaydedilmemisDegisiklikMesaji(): string | null {
    return this.action.pending() ? this.t('finans.islem.terkMesaji') : null;
  }

  protected submit(): void {
    const id = this.cariId();
    if (!id) return;
    void this.action.run<{ id: string }>({
      form: this.form,
      build: () => {
        const body = adjustmentBody(id, this.form.getRawValue() as AdjustmentFormValue);
        return {
          path: financePath('/bakiye-duzeltme'),
          body,
          content: { tutar: body.tutar, doviz: body.doviz ?? 'TRY' },
        };
      },
      confirm: () =>
        this.confirm.sor({
          baslik: this.t('finans.duzeltme.onayBaslik'),
          // #299 L-new-1: onay metni HANGİ carinin düzeltileceğini söyler (sorgu ile cari değişmiş olabilir).
          mesaj: this.t('finans.duzeltme.onayMesaj', { ad: this.customerName(id) }),
        }),
      success: () => {
        this.toast.basari(this.t('finans.duzeltme.kaydedildi'));
        this.resetForm();
      },
      afterDuplicate: () => this.resetForm(),
      settled: () => this.balance.yenile(),
    });
  }

  /** Gövdedeki carinin adı: bakiye yanıtı o cariye aitse ondan (KVKK kurallı), değilse seçici etiketinden. */
  private customerName(id: string): string {
    const b = this.balance.veri();
    return b?.cariId === id ? b.cariAd : (this.customer.value?.etiket ?? '');
  }

  private resetForm(): void {
    this.form.reset({ yon: 'Alacaklandir', doviz: 'TRY' });
  }
}
