import {
  ChangeDetectionStrategy,
  Component,
  Injectable,
  Pipe,
  type PipeTransform,
  booleanAttribute,
  computed,
  inject,
  input,
  output,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { type FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { paraBicimle, sayiBicimle } from '@core/bicim/bicim';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { TemelStore } from '@core/veri/temel-store';
import { DOVIZLER, KANALLAR } from '@features/kira-formu/finans-paneli/finans-modeli';
import { BICIM_PIPELARI } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import { AramaSecim } from '@shared/form/arama-secim/arama-secim';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { OnayKutusu } from '@shared/form/kontroller/onay-kutusu';
import { ParaGirdisi } from '@shared/form/kontroller/para-girdisi';
import { SayiGirdisi } from '@shared/form/kontroller/sayi-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';
import { Ikon } from '@shared/ikon/ikon';
import type { IkonAdi } from '@shared/ikon/ikon-kaydi';

import {
  ACCOUNT_KINDS,
  type AccountKind,
  type AccountOption,
  type CustomerBalance,
  customerPath,
  financePath,
} from './finance-model';
import type { MoneyActionState } from './money-action';

type Translate = ReturnType<typeof ceviriFonksiyonu>;

/** Aktif kasa/banka hesapları (seçim isteğe bağlı; hata sessiz — seçici boş kalır, tür yine seçilir). */
@Injectable()
export class AccountList {
  private readonly api = inject(ApiIstemcisi);
  readonly store = new TemelStore(() =>
    this.api.get<readonly AccountOption[]>(financePath('/hesaplar'), {
      context: istekBaglami({ sessiz: true }),
    }),
  );
  private readonly all = computed(() => this.store.veri() ?? []);

  load(): void {
    if (this.store.tur() === 'bos') this.store.yukle();
  }

  /** Türe göre hesap seçenekleri (tür yoksa tümü). */
  options(kind: AccountKind | null | undefined): readonly SecenekOgesi<string>[] {
    return this.all()
      .filter((h) => !kind || h.tur === kind)
      .map((h) => ({ deger: h.id, etiket: h.etiket }));
  }

  /** Seçili hesap türle uyuşmuyorsa temizlenmeli mi. */
  fits(id: string | null, kind: AccountKind | null | undefined): boolean {
    return id === null || this.options(kind).some((o) => o.deger === id);
  }
}

/**
 * Döviz değişince kur TEMİZLENİR (#291 M2): eski dövizin kuru (ör. TRY'nin 1'i) yeni dövize gitmesin; boş kur =
 * sunucu çözer. Enjeksiyon bağlamında çağrılır.
 */
export function clearRateOnCurrencyChange(
  currency: FormControl<string | null>,
  rate: FormControl<number | null>,
): void {
  let last = currency.value;
  currency.valueChanges.pipe(takeUntilDestroyed()).subscribe((v) => {
    if (v !== last && rate.value !== null) rate.setValue(null);
    last = v;
  });
}

/** Hesap türü değişince türe uymayan spesifik hesap seçimi düşer. Enjeksiyon bağlamında çağrılır. */
export function clearAccountOnKindChange(
  accounts: AccountList,
  kind: FormControl<AccountKind | null>,
  account: FormControl<string | null>,
): void {
  kind.valueChanges.pipe(takeUntilDestroyed()).subscribe((k) => {
    if (!accounts.fits(account.value, k)) account.setValue(null);
  });
}

/** Seçili carinin bakiyesi (`GET finans/cariler/{id}/bakiye`; sunucu Σ SignedBase — SPA toplamaz). */
@Injectable()
export class CustomerBalanceSource {
  private readonly api = inject(ApiIstemcisi);
  readonly store = new TemelStore(
    (cariId: string) => this.api.get<CustomerBalance>(customerPath(cariId, '/bakiye')),
    { oncekiVeriyiKoru: true },
  );
}

/** Bakiye yönü etiketi anahtarı: pozitif = müşteri borçlu, negatif = alacaklı. */
export function balanceSide(v: number | string | null | undefined): 'borclu' | 'alacakli' | null {
  const n = typeof v === 'string' ? Number(v) : v;
  if (n === null || n === undefined || !Number.isFinite(n) || n === 0) return null;
  return n > 0 ? 'borclu' : 'alacakli';
}

export function kindOptions(t: Translate): readonly SecenekOgesi<AccountKind>[] {
  return ACCOUNT_KINDS.map((k) => ({ deger: k, etiket: t(`finans.hesapTuru.${k}`) }));
}

export const CURRENCY_OPTIONS: readonly SecenekOgesi<string>[] = DOVIZLER.map((d) => ({
  deger: d,
  etiket: d,
}));

export const CHANNEL_OPTIONS: readonly SecenekOgesi<string>[] = KANALLAR.map((k) => ({
  deger: k,
  etiket: k,
}));

/**
 * Para formunun gönder düğmesi + "sonucu bilinmiyor" bandı + vazgeç + alansız hatalar (tüm finans ekranlarında aynı).
 * Donmuş kopya varken düğme "Aynı işlemi tekrar gönder" olur ve formu değil kopyayı gönderir.
 */
@Component({
  selector: 'rc-fin-submit',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, FormHatalari, Ikon],
  template: `
    @if (action().frozen()) {
      <div class="rc-form-mesaji rc-form-mesaji--uyari" role="alert">
        {{ 'finans.islem.sonucBilinmiyor' | transloco }}
      </div>
    }
    <rc-form-hatalari [hatalar]="action().errors()" />
    <div class="form__eylemler">
      <button
        type="button"
        class="rc-dugme rc-dugme--kucuk"
        [class.rc-dugme--birincil]="!ikincil()"
        [disabled]="action().sending() || disabled()"
        (click)="send.emit()"
      >
        <rc-ikon [ad]="ikon()" [boyut]="14" />
        {{
          action().sending()
            ? ('form.gonderiliyor' | transloco)
            : action().frozen()
              ? ('finans.islem.tekrarGonder' | transloco)
              : label()
        }}
      </button>
      @if (action().frozen() && !action().sending()) {
        <button
          type="button"
          class="rc-dugme rc-dugme--hayalet rc-dugme--kucuk"
          (click)="giveUp.emit()"
        >
          {{ 'finans.islem.vazgec' | transloco }}
        </button>
      }
      <ng-content />
    </div>
  `,
})
export class FinanceSubmit {
  readonly action = input.required<MoneyActionState>();
  readonly label = input.required<string>();
  readonly ikon = input<IkonAdi>('cash');
  readonly ikincil = input(false, { transform: booleanAttribute });
  readonly disabled = input(false, { transform: booleanAttribute });
  readonly send = output<void>();
  readonly giveUp = output<void>();
}

/** Sunucu sayısı (`number | string`) → sayı; boş/biçimsiz `null`. HESAP YAPILMAZ, yalnız gösterim. */
export function toAmount(v: number | string | null | undefined): number | null {
  if (v === null || v === undefined || v === '') return null;
  const n = typeof v === 'number' ? v : Number(v);
  return Number.isFinite(n) ? n : null;
}

/** `{{ tutar | fpara }}` / `{{ tutar | fpara: 'USD' }}` — sunucu sayısı (`number | string`) için `para`. */
@Pipe({ name: 'fpara' })
export class FinanceMoneyPipe implements PipeTransform {
  transform(
    v: number | string | null | undefined,
    currency: string | null | undefined = 'TRY',
  ): string {
    return paraBicimle(toAmount(v), currency || 'TRY');
  }
}

/** `{{ tutar | fsayi }}` — düz sayı (döviz ayrı sütunda); varsayılan iki ondalık. */
@Pipe({ name: 'fsayi' })
export class FinanceNumberPipe implements PipeTransform {
  transform(v: number | string | null | undefined, digits = '1.2-2'): string {
    return sayiBicimle(toAmount(v), digits);
  }
}

/** `{{ tutar | negatif }}` → sıfırdan küçükse `true` (renk sınıfı için). */
@Pipe({ name: 'negatif' })
export class NegativePipe implements PipeTransform {
  transform(v: number | string | null | undefined): boolean {
    const n = toAmount(v);
    return n !== null && n < 0;
  }
}

/** Finans ekranlarının ortak içe aktarımları (form seti + biçim pipe'ları + gönder düğmesi). */
export const FIN_COMMON = [
  ReactiveFormsModule,
  RouterLink,
  TranslocoPipe,
  ...BICIM_PIPELARI,
  Alan,
  AramaSecim,
  FormHatalari,
  Ikon,
  MetinGirdisi,
  OnayKutusu,
  ParaGirdisi,
  SayiGirdisi,
  Secim,
  TarihSecici,
  FinanceSubmit,
  FinanceMoneyPipe,
  FinanceNumberPipe,
  NegativePipe,
] as const;
