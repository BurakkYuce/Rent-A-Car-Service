import {
  Injectable,
  Pipe,
  type PipeTransform,
  computed,
  effect,
  inject,
  untracked,
  type WritableSignal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { type FormControl, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { formatMoney, sayiBicimle } from '@core/bicim/bicim';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { translationFunction } from '@core/i18n/ceviri';
import { requestContext } from '@core/oturum/request-context';
import { TemelStore } from '@core/veri/temel-store';
import { CURRENCIES, CHANNELS } from '@features/kira-formu/finans-paneli/finans-modeli';
import { FORMAT_PIPES } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import { SearchSelection } from '@shared/form/arama-secim/search-selection';
import type { SecimSecenegi } from '@shared/form/arama-secim/selection-source';
import { FormErrors } from '@shared/form/form-errors';
import { TextInput } from '@shared/form/kontroller/text-input';
import { Checkbox } from '@shared/form/kontroller/checkbox';
import { MoneyInput } from '@shared/form/kontroller/money-input';
import { NumberInput } from '@shared/form/kontroller/number-input';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Selection } from '@shared/form/kontroller/selection';
import { MoneyNoticeView } from '@shared/form/money-submit/money-notice';
import { MoneySubmitBar } from '@shared/form/money-submit/money-submit-bar';
import { DatePicker } from '@shared/form/tarih/date-picker';
import { Icon } from '@shared/ikon/icon';

import {
  ACCOUNT_KINDS,
  type AccountKind,
  type AccountOption,
  type CustomerBalance,
  customerPath,
  financePath,
} from './finance-model';
import { PageBand } from '../../kabuk/sayfa-bandi/page-band';

type Translate = ReturnType<typeof translationFunction>;

/** Aktif kasa/banka hesapları (seçim isteğe bağlı; hata sessiz — seçici boş kalır, tür yine seçilir). */
@Injectable()
export class AccountList {
  private readonly api = inject(ApiIstemcisi);
  readonly store = new TemelStore(() =>
    this.api.get<readonly AccountOption[]>(financePath('/hesaplar'), {
      context: requestContext({ sessiz: true }),
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
    (customerId: string) => this.api.get<CustomerBalance>(customerPath(customerId, '/bakiye')),
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

export const CURRENCY_OPTIONS: readonly SecenekOgesi<string>[] = CURRENCIES.map((d) => ({
  deger: d,
  etiket: d,
}));

export const CHANNEL_OPTIONS: readonly SecenekOgesi<string>[] = CHANNELS.map((k) => ({
  deger: k,
  etiket: k,
}));

/**
 * `?cariId=` sorgusunu İZLER (r299 LOW-3): kalıcı sekmede aynı rota başka cariyle açılınca (ekstreden, kasadan) sayfa
 * yeni cariye geçer; yalnız ilk açılışta okumak eski cariyi gösteriyordu. Sonucu bilinmeyen/uçan işlem varken
 * (`busy`) geçiş YAPILMAZ — donmuş kopya o cariye aittir. Seçici etiketi yeni carinin verisiyle yeniden çözülür.
 *
 * #299 L-new-1: form KİRLİYSE (`unsaved`) geçiş sessiz yapılmaz — yazılan tutar yeni cariye gitmesin. Kullanıcıya
 * sorulur; onaylarsa `discard` formu temizler ve geçilir, vazgeçerse ekrandaki cari kalır.
 */
export function followCustomerQuery(
  customerId: WritableSignal<string | null>,
  customer: FormControl<SecimSecenegi | null>,
  busy: () => boolean,
  unsaved: { readonly dirty: () => boolean; readonly discard: () => void },
): void {
  const confirm = inject(ConfirmService);
  const t = translationFunction();
  /** URL'deki son `cariId` (meşgulken ya da onay açıkken gelen değişiklik kaybolmaz — r316 L2). */
  let latest: string | null = null;
  /** Kullanıcının bu sorgu için "Vazgeç" dediği cari: aynı değer için yeniden sorulmaz. */
  let declined: string | null = null;
  let asking = false;
  /** Sorgu değişikliği meşgulken/onay açıkken geldi ve henüz değerlendirilmedi. */
  let deferred = false;
  const change = (id: string) => {
    customer.setValue(null, { emitEvent: false });
    customerId.set(id);
  };
  const evaluate = (): void => {
    if (busy() || asking) {
      deferred = true;
      return;
    }
    deferred = false;
    const id = latest;
    if (!id || id === customerId() || id === declined) return;
    if (!unsaved.dirty()) {
      change(id);
      return;
    }
    asking = true;
    void confirm
      .ask({
        baslik: t('finans.cariDegisimi.baslik'),
        mesaj: t('finans.cariDegisimi.mesaj', { ad: customer.value?.etiket ?? '' }),
        onayEtiketi: t('finans.cariDegisimi.gec'),
        tehlikeli: true,
      })
      .then((yes) => {
        asking = false;
        if (!yes) declined = id;
        else if (busy()) deferred = true;
        else if (id === latest && id !== customerId()) {
          unsaved.discard();
          change(id);
        }
        // Onay açıkken URL yeniden değiştiyse son değer değerlendirilir.
        if (deferred) evaluate();
      });
  };
  inject(ActivatedRoute)
    .queryParamMap.pipe(takeUntilDestroyed())
    .subscribe((p) => {
      latest = p.get('cariId');
      if (latest !== declined) declined = null;
      evaluate();
    });
  // Gönderim bitince (uçuş/donma kalkınca) ertelenen sorgu değişikliği yeniden değerlendirilir.
  effect(() => {
    if (!busy() && deferred) untracked(evaluate);
  });
}

/** Seçici etiketi, yalnız GÜNCEL carinin verisi geldiğinde (önceki veri korunurken eski ad yazılmasın). */
export function labelFromData(
  customer: FormControl<SecimSecenegi | null>,
  currentId: string | null,
  data: { readonly cariId: string; readonly cariAd: string } | undefined,
): void {
  if (!data || data.cariId !== currentId || customer.value?.id === data.cariId) return;
  customer.setValue({ id: data.cariId, etiket: data.cariAd }, { emitEvent: false });
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
    return formatMoney(toAmount(v), currency || 'TRY');
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
  ...FORMAT_PIPES,
  Alan,
  SearchSelection,
  FormErrors,
  Icon,
  TextInput,
  Checkbox,
  MoneyInput,
  NumberInput,
  Selection,
  DatePicker,
  MoneyNoticeView,
  MoneySubmitBar,
  PageBand,
  FinanceMoneyPipe,
  FinanceNumberPipe,
  NegativePipe,
] as const;
