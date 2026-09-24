import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, Validators } from '@angular/forms';

import { ApiIstemcisi, type SorguParametreleri } from '@core/api/api-istemcisi';
import {
  type KaydedilmemisDegisiklikSahibi,
  sayfaTerkKorumasi,
} from '@core/form/kaydedilmemis-degisiklik';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { TemelStore } from '@core/veri/temel-store';
import { dovizKodu } from '@features/kira-formu/finans-paneli/finans-modeli';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';

import {
  type AccountKind,
  type CashTransactionList,
  type CashTransferRequest,
  type CashTransferRow,
  type CashboxSummary,
  TRANSACTION_TYPES,
  type TransferFormValue,
  cashTransferBody,
  financePath,
  queryParams,
} from '../finance-model';
import {
  AccountList,
  CHANNEL_OPTIONS,
  CURRENCY_OPTIONS,
  FIN_COMMON,
  clearAccountOnKindChange,
  clearRateOnCurrencyChange,
  kindOptions,
} from '../finance-shared';
import { moneyAction } from '../money-action';

const PAGE_SIZE = 50;

/**
 * Kasa / Banka (`/app/kasa`, Blazor `KasaHub.razor`): özet kartlar (sunucu, ₺), virman (kasa↔banka / hesaplar arası;
 * `POST finans/kasa/virman`, E06), son virmanlar (künye) ve süzgeçli nakit işlem listesi (önce sunucuda süz, sonra kes).
 * "İşlemi yapan" formda yok — oturumdan. Negatif bakiye mümkündür (bilinçli, guard yok).
 */
@Component({
  selector: 'rc-cash-hub',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...FIN_COMMON],
  providers: [FetchPolicy, AccountList],
  templateUrl: './cash-hub.html',
  styleUrl: '../finance.scss',
})
export class CashHub implements KaydedilmemisDegisiklikSahibi {
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastServisi);
  private readonly t = ceviriFonksiyonu();
  protected readonly accounts = inject(AccountList);

  protected readonly summary = new TemelStore(() =>
    this.api.get<CashboxSummary>(financePath('/kasa/ozet')),
  );
  protected readonly transfers = new TemelStore(() =>
    this.api.get<readonly CashTransferRow[]>(financePath('/kasa/virmanlar'), {
      parametreler: { limit: 50 },
    }),
  );
  protected readonly transactions = new TemelStore(
    (p: SorguParametreleri) =>
      this.api.get<CashTransactionList>(financePath('/kasa/islemler'), { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );

  protected readonly filter = new FormGroup({
    q: new FormControl<string | null>(null, Validators.maxLength(100)),
    tip: new FormControl<string | null>(null),
    hesap: new FormControl<string | null>(null),
    kanal: new FormControl<string | null>(null),
    bas: new FormControl<string | null>(null),
    bit: new FormControl<string | null>(null),
  });
  private readonly params = signal<SorguParametreleri>({ sayfa: 1, boyut: PAGE_SIZE });
  protected readonly page = computed(() => Number(this.params()['sayfa'] ?? 1));
  protected readonly pageCount = computed(() => {
    const l = this.transactions.veri()?.liste;
    return l ? Math.max(1, Math.ceil(Number(l.toplam) / PAGE_SIZE)) : 1;
  });
  protected readonly filtered = computed(() => Object.keys(this.params()).length > 2);

  protected readonly typeOptions: readonly SecenekOgesi<string>[] = TRANSACTION_TYPES.map((x) => ({
    deger: x,
    etiket: this.t(`finans.islemTipi.${x}`),
  }));
  protected readonly kindOptions = kindOptions(this.t);
  protected readonly channelOptions = CHANNEL_OPTIONS;
  protected readonly currencyOptions = CURRENCY_OPTIONS;

  protected readonly action = moneyAction<CashTransferRequest>();
  protected readonly form = new FormGroup({
    kaynak: new FormControl<AccountKind | null>('Kasa', Validators.required),
    kaynakHesapId: new FormControl<string | null>(null),
    hedef: new FormControl<AccountKind | null>('Banka', Validators.required),
    hedefHesapId: new FormControl<string | null>(null),
    tutar: new FormControl<string | null>(null, Validators.required),
    doviz: new FormControl<string | null>('TRY', Validators.required),
    kur: new FormControl<number | null>(null),
    makbuzNo: new FormControl<string | null>(null, Validators.maxLength(32)),
    sube: new FormControl<string | null>(null, Validators.maxLength(128)),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
  });
  private readonly sourceKind = toSignal(this.form.controls.kaynak.valueChanges, {
    initialValue: this.form.controls.kaynak.value,
  });
  private readonly targetKind = toSignal(this.form.controls.hedef.valueChanges, {
    initialValue: this.form.controls.hedef.value,
  });
  protected readonly currency = toSignal(this.form.controls.doviz.valueChanges, {
    initialValue: this.form.controls.doviz.value,
  });
  protected readonly sourceAccounts = computed(() => this.accounts.options(this.sourceKind()));
  protected readonly targetAccounts = computed(() => this.accounts.options(this.targetKind()));
  protected readonly isForeign = computed(() => dovizKodu(this.currency()) !== 'TRY');

  constructor() {
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
    this.accounts.load();
    clearRateOnCurrencyChange(this.form.controls.doviz, this.form.controls.kur);
    clearAccountOnKindChange(
      this.accounts,
      this.form.controls.kaynak,
      this.form.controls.kaynakHesapId,
    );
    clearAccountOnKindChange(
      this.accounts,
      this.form.controls.hedef,
      this.form.controls.hedefHesapId,
    );
    const policy = inject(FetchPolicy);
    policy.baglan({ parametre: signal(0).asReadonly(), yukle: () => this.reloadSummary() });
    policy.baglan({ parametre: this.params, yukle: (p) => this.transactions.yukle(p) });
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.action.pending() || this.form.dirty;
  }

  kaydedilmemisDegisiklikMesaji(): string | null {
    return this.action.pending() ? this.t('finans.islem.terkMesaji') : null;
  }

  protected applyFilter(): void {
    const v = this.filter.getRawValue();
    this.params.set({ ...queryParams(v), sayfa: 1, boyut: PAGE_SIZE });
  }

  protected clearFilter(): void {
    this.filter.reset();
    this.params.set({ sayfa: 1, boyut: PAGE_SIZE });
  }

  protected goTo(page: number): void {
    this.params.update((p) => ({ ...p, sayfa: page }));
  }

  protected transfer(): void {
    void this.action.run<{ id: string }>({
      form: this.form,
      build: () => {
        const body = cashTransferBody(this.form.getRawValue() as TransferFormValue);
        return {
          path: financePath('/kasa/virman'),
          body,
          content: { tutar: body.tutar, doviz: body.doviz ?? 'TRY', hesap: body.kaynak ?? null },
        };
      },
      success: () => {
        this.toast.basari(this.t('finans.kasa.virmanYapildi'));
        this.resetForm();
      },
      afterDuplicate: () => this.resetForm(),
      settled: () => this.reloadAll(),
    });
  }

  protected abandon(): void {
    void this.action.abandon(() => this.reloadAll());
  }

  protected makbuzUrl(id: string): string {
    return `/kasa/makbuz/${encodeURIComponent(id)}/pdf`;
  }

  private reloadSummary(): void {
    this.summary.yukle();
    this.transfers.yukle();
  }

  private reloadAll(): void {
    this.reloadSummary();
    this.transactions.yenile();
  }

  private resetForm(): void {
    const v = this.form.getRawValue();
    this.form.reset({ kaynak: v.kaynak, hedef: v.hedef, doviz: 'TRY' });
  }
}
