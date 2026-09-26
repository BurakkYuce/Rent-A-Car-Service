import { type Signal, computed, signal } from '@angular/core';
import { formatMoney } from '@core/bicim/bicim';
import { invariantDecimal } from '@core/form/ondalik';
import { toNumber } from '../kira-formu-modeli';
import type { ServerNumber } from '../kira-tipleri';
import type {
  TakeDepositRequest,
  DepositForfeitRequest,
  OutsourcedServiceRequest,
  IssueInvoiceRequest,
  AccountType,
  PaymentRequest,
  CollectionInfo,
  CollectionRequest,
} from './finans-tipleri';

/**
 * Sabit panel finans işlemlerinin SAF kuralları (F4.4): istek gövdeleri, kur alanı, tahsilat anahtarı
 * kopyası. Tutar formülü YOK — tutarlar kullanıcının yazdığı (ya da sunucunun verdiği) değerlerdir; para
 * değerleri invariant ondalık METİN taşınır (`rc-para-girdisi`: "1.500,50" → "1500.50"; sunucu `decimal`).
 */

/** Temel para birimi; kur alanı yalnız bunun dışında gönderilir. */
export const BASE_CURRENCY = 'TRY';
/** Blazor sabit panelinin döviz seçenekleri (kiranın dövizi farklıysa listeye eklenir). */
export const CURRENCIES = ['TRY', 'USD', 'EUR'] as const;
/** FAZ-84 kanal (saf bilgi; defteri etkilemez). */
export const CHANNELS = ['Masaüstü', 'Mobil', 'Tablet'] as const;

type MoneyValue = string | number | null | undefined;

/** Para kontrolü değeri → invariant metin (`"1500.50"`); boş/biçimsiz → `null`. */
export function moneyText(value: MoneyValue, fraction = 4): string | null {
  return invariantDecimal(value, { kesir: fraction });
}

/** Sunucunun `varsayilanTutar`'ı → ön-doldurma (yalnız POZİTİFse; bakiye ≤ 0 ise alan boş kalır). */
export function prefillAmount(value: MoneyValue): string | null {
  const m = moneyText(value, 2);
  return m !== null && !m.startsWith('-') && !/^0+(\.0+)?$/.test(m) ? m : null;
}

/** ISO kod (boş → TRY). Kira formunun eski adları ("TL", "EURO") ISO'ya çevrilir; kalanı sunucu doğrular. */
export function currencyCode(currency: string | null | undefined): string {
  const d = (currency ?? '').trim();
  if (d === '' || d === 'TL') return BASE_CURRENCY;
  if (d === 'EURO') return 'EUR';
  return d;
}

/** Gösterim: sunucu tutarı + döviz → `1.234,56 ₺`; değer yoksa "—". HESAP YAPILMAZ. */
export function displayMoney(v: ServerNumber, currency: string | null | undefined): string {
  return formatMoney(toNumber(v), currencyCode(currency)) || '—';
}

/** Döviz seçenekleri: sabit liste + (varsa) kiranın dövizi. */
export function currencyOptions(extra: string | null | undefined): readonly string[] {
  const code = extra ? currencyCode(extra) : null;
  return code && !(CURRENCIES as readonly string[]).includes(code)
    ? [...CURRENCIES, code]
    : CURRENCIES;
}

/**
 * Kur alanı kuralı: temel parada (TRY) kur GÖNDERİLMEZ (sunucu 1 kullanır; ≠1 reddedilir). Dövizde yazılan
 * kur gönderilir; boşsa hiç gönderilmez → sunucu çözer (firma sabit kuru → TCMB; bulunamazsa red).
 */
export function exchangeRateField(
  currency: string | null | undefined,
  exchangeRate: MoneyValue,
): { kur?: string } {
  if (currencyCode(currency) === BASE_CURRENCY) return {};
  const m = moneyText(exchangeRate, 6);
  return m === null ? {} : { kur: m };
}

function metin(value: string | null | undefined): string | null {
  const d = value?.trim() ?? '';
  return d === '' ? null : d;
}

export interface TahsilatDegeri {
  readonly tutar: MoneyValue;
  readonly doviz: string | null;
  readonly kur: MoneyValue;
  readonly hesapId: string | null;
  readonly kanal: string | null;
  readonly aciklama: string | null;
}

/**
 * Kira tahsilatı gövdesi. Cari, kira ve DETERMİNİSTİK anahtar SUNUCUNUN verdiği satır kopyasından
 * (`TahsilatBilgisi`) gelir — formdan değil. Anahtar gövdede taşınır; `Idempotency-Key` başlığı
 * GÖNDERİLMEZ (idempotency envanteri "SPA sözleşmesi": deterministik anahtar varken başlık tüketilmez;
 * yeniden deneme AYNI kopyayla yapılır, anahtarsız asla).
 */
export function collectionBody(
  copy: CollectionInfo,
  account: AccountType,
  d: TahsilatDegeri,
): CollectionRequest {
  return {
    cariId: copy.cariId,
    kiraId: copy.rentalId,
    tahsilatAnahtar: copy.anahtar,
    tutar: moneyText(d.tutar, 2) ?? '',
    hesap: account,
    hesapId: d.hesapId,
    doviz: currencyCode(d.doviz),
    ...exchangeRateField(d.doviz, d.kur),
    kanal: d.kanal,
    aciklama: metin(d.aciklama),
  };
}

export interface OdemeDegeri {
  readonly tutar: MoneyValue;
  readonly hesapId: string | null;
  readonly kanal: string | null;
  readonly aciklama: string | null;
}

/**
 * Giden havale (Banka ödemesi). Blazor paritesi: kiraya BAĞLANMAZ (kira Tahsilat/Bakiye'si değişmez),
 * cari = kiranın müşterisi, TRY. Anahtar `Idempotency-Key` başlığında (ZORUNLU; işlem başına).
 */
export function paymentBody(customerId: string, d: OdemeDegeri): PaymentRequest {
  return {
    cariId: customerId,
    tutar: moneyText(d.tutar, 2) ?? '',
    hesap: 'Banka',
    hesapId: d.hesapId,
    kanal: d.kanal,
    aciklama: metin(d.aciklama),
  };
}

export interface DepozitoAlDegeri {
  readonly tutar: MoneyValue;
  readonly hesap: AccountType | null;
  readonly hesapId: string | null;
}

/** Depozito al (TRY, cari = kiranın müşterisi): Borç Kasa/Banka / Alacak Depozito. */
export function takeDepositBody(customerId: string, d: DepozitoAlDegeri): TakeDepositRequest {
  return {
    cariId: customerId,
    tutar: moneyText(d.tutar, 2) ?? '',
    hesap: d.hesap,
    hesapId: d.hesapId,
  };
}

export interface IratDegeri {
  readonly tutar: MoneyValue;
  readonly aciklama: string | null;
}

/** Depozito irat: gelir bu kiranın aracına atfedilir (kira bu carinin olmalı — sunucu çiti). */
export function forfeitBody(
  customerId: string,
  rentalId: string,
  d: IratDegeri,
): DepositForfeitRequest {
  return {
    cariId: customerId,
    kiraId: rentalId,
    tutar: moneyText(d.tutar, 2) ?? '',
    aciklama: metin(d.aciklama),
  };
}

export interface FaturaDegeri {
  readonly otv: MoneyValue;
  readonly tevkifatOran: MoneyValue;
  readonly tevkifatTutar: MoneyValue;
  readonly damgaVergisi: MoneyValue;
  readonly iadeMi: boolean | null;
  readonly manuelMi: boolean | null;
}

/** Kiradan fatura (fark faturası otomatiği serviste). Vergi alanları bilgi amaçlı, boş = gönderilmez. */
export function invoiceBody(rentalId: string, d: FaturaDegeri): IssueInvoiceRequest {
  return {
    kiraId: rentalId,
    otv: moneyText(d.otv, 2),
    tevkifatOran: moneyText(d.tevkifatOran, 2),
    tevkifatTutar: moneyText(d.tevkifatTutar, 2),
    damgaVergisi: moneyText(d.damgaVergisi, 2),
    iadeMi: d.iadeMi ?? false,
    manuelMi: d.manuelMi ?? false,
  };
}

export interface DisHizmetDegeri {
  readonly cariId: string | null;
  readonly alinanHizmet: string | null;
  readonly hizmetAlinanFirma: string | null;
  readonly hizmetBedeli: MoneyValue;
  readonly komisyonOran: MoneyValue;
  readonly doviz: string | null;
  readonly kur: MoneyValue;
  readonly komisyonFaturaNo: string | null;
  readonly aciklama: string | null;
}

/** B2B dış hizmet alımı (tam defterli). Anahtar başlıkta (ZORUNLU; işlem başına). */
export function outsourcedServiceBody(
  rentalId: string,
  d: DisHizmetDegeri,
): OutsourcedServiceRequest {
  return {
    kiraId: rentalId,
    cariId: d.cariId ?? '',
    alinanHizmet: metin(d.alinanHizmet),
    hizmetAlinanFirma: metin(d.hizmetAlinanFirma),
    hizmetBedeli: moneyText(d.hizmetBedeli, 2) ?? '',
    komisyonOran: moneyText(d.komisyonOran, 2),
    doviz: currencyCode(d.doviz),
    ...exchangeRateField(d.doviz, d.kur),
    komisyonFaturaNo: metin(d.komisyonFaturaNo),
    aciklama: metin(d.aciklama),
  };
}

/** `detayGeldi` sonucu: form ne yapmalı? */
export type CopyResult =
  /** Yeni kopya alındı, form boşta → değerler sunucudan ön-doldurulur. */
  | 'ondoldur'
  /** Yeni kopya alındı ama değerlere dokunulmaz (kullanıcı yazdı ya da 409 sonrası ön-doldurma kapalı). */
  | 'anahtar'
  /** Açık form: ESKİ kopya korunur (bayatsa sunucu 409 verir → yeniden yüklenir). */
  | 'korundu';

/**
 * Tahsilat formunun SATIR KOPYASI (deterministik `tahsilatAnahtar` + cari + kira + döviz). Kural:
 *
 * - Form boştaysa (dokunulmamış, sonuçlanmamış gönderim yok) her yeni detay kopyayı tazeler.
 * - Kullanıcı formu doldurduysa ya da bir gönderim SONUÇLANMADIYSA (ağ/5xx/oturum/doğrulama) kopya
 *   DONAR: yeniden deneme aynı anahtarla gider. İlk istek sunucuya ulaşıp yazıldıysa ikincisi anahtar
 *   üzerinden 409 alır — anahtar hiçbir zaman sessizce yenisiyle (ya da anahtarsızla) değiştirilmez.
 * - Gönderim sonuçlanınca (2xx ya da 409 `mukerrer`) kopya "tazeleme bekliyor" olur; gönderim düğmesi
 *   YENİ detay gelene dek kapalıdır, gelen detayın anahtarı alınır (ikinci meşru tahsilat yeni anahtarla).
 * - 409 sonrası (`sonuclandi(false)`) ön-doldurma KAPANIR, bir sonraki 2xx'e kadar: kullanıcı güncel bakiyeye
 *   bakıp tutarı bilinçli girer (F4.4 adversarial HIGH-1 — kaybolan yanıttan sonra ön-dolu tutar ikinci
 *   tahsilata davetti).
 */
export class CollectionCopy {
  private readonly _copy = signal<CollectionInfo | null>(null);
  private readonly _refreshPending = signal(false);
  private attempted = false;
  private prefillAllowed = true;

  readonly kopya: Signal<CollectionInfo | null> = this._copy.asReadonly();
  readonly refreshPending: Signal<boolean> = this._refreshPending.asReadonly();
  /** Gönderilebilir: kopya var ve sonuçlanan işlemden sonra tazeleme beklenmiyor. */
  readonly canSubmit = computed(() => this._copy() !== null && !this._refreshPending());

  /**
   * `anahtarBayat`: bu tazeleme, AYNI anahtarı taşıyan kardeş formun (Nakit ↔ Kart/Havale) SONUÇLANAN işleminden
   * geliyor — eski anahtar kesin kullanıldı. Kirli form yine de yeni anahtarı alır (değerlere dokunulmaz → 'anahtar');
   * aksi halde ilk basış kesin bir 409 turu yaşardı (#318 L2). Donmuş (sonucu bilinmeyen) deneme varsa anahtar
   * DEĞİŞMEZ: o deneme yazılmış olabilir, tekrar aynı anahtarla gitmeli.
   */
  detailLoaded(info: CollectionInfo | null, formDirty: boolean, keyStale = false): CopyResult {
    if (this._refreshPending()) {
      this._refreshPending.set(false);
      this.attempted = false;
    } else if (this._copy() !== null && (this.attempted || (formDirty && !keyStale))) {
      return 'korundu';
    }
    this._copy.set(info);
    return formDirty || !this.prefillAllowed ? 'anahtar' : 'ondoldur';
  }

  /** Gönderim denendi ama sonuçlanmadı (ağ/5xx/oturum/doğrulama): donmuş anahtar sayfa terkinde kaybolmasın. */
  get unsettled(): boolean {
    return this.attempted && !this._refreshPending();
  }

  /** Gönderim başlıyor: kopya donar ve döner (yoksa `null` — gönderim yapılmaz). */
  submitting(): CollectionInfo | null {
    const k = this._copy();
    if (k === null || this._refreshPending()) return null;
    this.attempted = true;
    return k;
  }

  /**
   * 2xx (`ondoldur` true) ya da 409 `mukerrer` (`false`): işlem sonuçlandı, sonraki detayın anahtarı alınır.
   * 409'dan sonra ön-doldurma bir sonraki başarılı işleme kadar kapalı kalır.
   */
  settled(prefill = true): void {
    this.prefillAllowed = prefill;
    this._refreshPending.set(true);
  }
}
