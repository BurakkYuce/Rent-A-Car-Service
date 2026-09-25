import {
  ChangeDetectionStrategy,
  Component,
  type OnInit,
  booleanAttribute,
  computed,
  effect,
  inject,
  input,
  output,
  untracked,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, Validators } from '@angular/forms';

import { moneySubmission } from '@core/form/money-submission';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { dovizKodu, onDoldurmaTutari } from '@features/kira-formu/finans-paneli/finans-modeli';

import {
  type AccountKind,
  type CashFormValue,
  type CollectionRequest,
  collectionBody,
  financePath,
  paymentBody,
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

export type CashOperationKind = 'tahsilat' | 'odeme';

/**
 * Bir cariye bağımsız tahsilat / ödeme (tediye) formu — `POST finans/tahsilat|odeme` (F4.4 uçları; kiraya bağlanmaz,
 * `Idempotency-Key` başlığı işlem başına). Nakit İşlem ve Cari Ekstre ekranları AYNI bileşeni kullanır (Blazor iki
 * yüzeyde aynı alanlar). Tahsilatta borç varsa tutar bakiye kadar ÖNERİLİR (yalnız dokunulmamış alana; sunucu hiçbir
 * şeyi bu değerden türetmez); 409 `mukerrer` sonrası öneri bir sonraki başarılı işleme kadar KAPALI (F4.4 HIGH-1).
 */
@Component({
  selector: 'rc-fin-cash-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...FIN_COMMON],
  templateUrl: './cash-operation-form.html',
  styleUrl: '../finance.scss',
})
export class CashOperationForm implements OnInit {
  readonly cariId = input.required<string>();
  readonly kind = input.required<CashOperationKind>();
  /** Güncel cari bakiyesi (sunucu; pozitif = müşteri borçlu) — tahsilat önerisi için. */
  readonly balance = input<number | string | null>(null);
  /** Tarih alanı (Nakit İşlem'de var, ekstrede yok — Blazor paritesi). */
  readonly withDate = input(false, { transform: booleanAttribute });
  /** Kanal varsayılanı: Nakit İşlem boş ("—"), ekstre "Masaüstü". */
  readonly defaultChannel = input<string | null>(null);
  readonly refreshing = input(false, { transform: booleanAttribute });
  readonly settled = output<void>();

  private readonly toast = inject(ToastServisi);
  private readonly t = ceviriFonksiyonu();
  protected readonly accounts = inject(AccountList);
  protected readonly action = moneySubmission<CollectionRequest>({
    scope: () => `nakit-${this.kind()}:${this.cariId()}`,
  });
  private prefillAllowed = true;

  protected readonly form = new FormGroup({
    tutar: new FormControl<string | null>(null, Validators.required),
    tarih: new FormControl<string | null>(null),
    hesap: new FormControl<AccountKind | null>('Kasa', Validators.required),
    hesapId: new FormControl<string | null>(null),
    kanal: new FormControl<string | null>(null),
    doviz: new FormControl<string | null>('TRY', Validators.required),
    kur: new FormControl<number | null>(null),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
  });

  protected readonly kindOptions = kindOptions(this.t);
  protected readonly currencyOptions = CURRENCY_OPTIONS;
  protected readonly channelOptions = CHANNEL_OPTIONS;
  private readonly accountKind = toSignal(this.form.controls.hesap.valueChanges, {
    initialValue: this.form.controls.hesap.value,
  });
  protected readonly currency = toSignal(this.form.controls.doviz.valueChanges, {
    initialValue: this.form.controls.doviz.value,
  });
  protected readonly accountOptions = computed(() => this.accounts.options(this.accountKind()));
  protected readonly isForeign = computed(() => dovizKodu(this.currency()) !== 'TRY');
  protected readonly titleKey = computed(() =>
    this.kind() === 'tahsilat' ? 'finans.nakit.tahsilat' : 'finans.nakit.odeme',
  );

  constructor() {
    this.accounts.load();
    clearRateOnCurrencyChange(this.form.controls.doviz, this.form.controls.kur);
    clearAccountOnKindChange(this.accounts, this.form.controls.hesap, this.form.controls.hesapId);
    effect(() => {
      const channel = this.defaultChannel();
      untracked(() => {
        if (this.form.controls.kanal.pristine) this.form.controls.kanal.setValue(channel);
      });
    });
    // Öneri: yalnız tahsilatta ve YALNIZ TRY'de (bakiye baz paradır, ₺ — r299 MEDIUM-2: TRY bakiyesi USD tutarı
    // olarak gidiyordu), dokunulmamış tutara, donmuş/uçan işlem yokken ve 409 sonrası değilken. Döviz değişince
    // dokunulmamış tutar yeniden hesaplanır: TRY dışında boşalır.
    effect(() => {
      const tryCurrency = dovizKodu(this.currency()) === 'TRY';
      const suggested =
        this.kind() === 'tahsilat' && tryCurrency ? onDoldurmaTutari(this.balance()) : null;
      untracked(() => {
        const c = this.form.controls.tutar;
        if (!c.pristine || this.action.pending()) return;
        c.setValue(this.prefillAllowed ? suggested : null);
      });
    });
  }

  ngOnInit(): void {
    // Sonucu bilinmeyen işlem (sayfa kapanıp açıldıysa) aynı gövde + anahtarla KİLİTLİ geri gelir.
    this.action.restore(this.form);
  }

  /** Sonucu bilinmeyen işlem var mı (sayfa terk koruması). */
  pending(): boolean {
    return this.action.pending();
  }

  dirty(): boolean {
    return this.form.dirty;
  }

  protected submit(): void {
    const tahsilat = this.kind() === 'tahsilat';
    void this.action.run<{ id: string }>({
      form: this.form,
      build: () => {
        const v = this.form.getRawValue() as CashFormValue;
        const body = tahsilat ? collectionBody(this.cariId(), v) : paymentBody(this.cariId(), v);
        return {
          path: financePath(tahsilat ? '/tahsilat' : '/odeme'),
          body,
          content: { tutar: body.tutar, doviz: body.doviz ?? 'TRY' },
        };
      },
      success: () => {
        this.prefillAllowed = true;
        this.toast.basari(
          this.t(tahsilat ? 'finans.nakit.tahsilatYapildi' : 'finans.nakit.odemeYapildi'),
        );
        this.resetForm();
      },
      afterDuplicate: () => this.resetForm(),
      settled: (reason) => {
        // 409 sonrası öneri bir sonraki başarılı işleme kadar KAPALI (F4.4 HIGH-1; `mevcut`suz 409 dahil).
        if (reason === 'duplicate') this.prefillAllowed = false;
        this.settled.emit();
      },
    });
  }

  /** Cari değişimi (onaylı) ya da işlem sonrası: form boş hâline döner. */
  resetForm(): void {
    const keep = this.form.getRawValue();
    this.form.reset({
      tutar: null,
      tarih: null,
      hesap: keep.hesap,
      hesapId: keep.hesapId,
      kanal: this.defaultChannel(),
      doviz: 'TRY',
      kur: null,
      aciklama: null,
    });
  }
}
