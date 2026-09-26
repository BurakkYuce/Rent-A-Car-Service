import type { ApiPath, QueryParameters } from '@core/api/api-istemcisi';
import type { Schema } from '@core/api/ui-tipleri';
import { type DayText, mergeMoment } from '@core/form/tarih-girdisi';
import {
  currencyCode,
  exchangeRateField,
  moneyText,
} from '@features/kira-formu/finans-paneli/finans-modeli';

/**
 * F8.2a finans ekranlarının SAF kuralları: tipler (üretilen şemaların takma adları), uç yolları ve istek gövdeleri.
 * Tutar formülü YOK — bakiye, yürüyen bakiye, mizan, toplamlar sunucudan gelir. Para değerleri invariant ondalık
 * METİN taşınır (`rc-para-girdisi` → `"1500.50"`); kur alanı kira formunun kuralıyla (`kurAlani`): TRY'de gönderilmez,
 * dövizde boşsa gönderilmez (sunucu firma sabit kuru → TCMB ile çözer).
 */

export type CashboxSummary = Schema<'CashboxSummary'>;
export type CashTransactionList = Schema<'CashTransactionList'>;
export type CashTransactionRow = Schema<'CashTransactionRow'>;
export type CashTransferRow = Schema<'CashTransferRow'>;
export type CashTransferRequest = Schema<'CashTransferRequest'>;
export type CashOperationResult = Schema<'CashOperationResult'>;
export type CustomerBalance = Schema<'CustomerBalance'>;
export type BalanceAdjustmentRequest = Schema<'BalanceAdjustmentRequest'>;
export type CustomerTransferRequest = Schema<'CustomerTransferRequest'>;
export type CustomerTransferRow = Schema<'CustomerTransferRow'>;
export type CustomerStatement = Schema<'CustomerStatement'>;
export type CustomerStatementLine = Schema<'CustomerStatementLine'>;
export type CustomerOpenItems = Schema<'CustomerOpenItems'>;
export type CustomerOpenItem = Schema<'CustomerOpenItem'>;
export type CloseItemsRequest = Schema<'CloseItemsRequest'>;
export type CloseItemsResult = Schema<'CloseItemsResult'>;
export type BulkCollectionRequest = Schema<'BulkCollectionRequest'>;
export type BulkPostingResult = Schema<'BulkPostingResult'>;
export type BulkExpenseRequest = Schema<'BulkExpenseRequest'>;
export type DepositBalanceRow = Schema<'DepositBalanceRow'>;
export type PeriodCloseState = Schema<'PeriodCloseState'>;
export type AutoCollectionList = Schema<'AutoCollectionList'>;
export type AutoCollectionCandidate = Schema<'AutoCollectionCandidate'>;
export type AutoCollectionRequest = Schema<'AutoCollectionRequest'>;
export type AutoCollectionResult = Schema<'AutoCollectionResult'>;
export type RatesScreen = Schema<'RatesScreen'>;
export type FixedRate = Schema<'FixedRate'>;
export type FixedRateCreateRequest = Schema<'FixedRateCreateRequest'>;
export type FixedRateUpdateRequest = Schema<'FixedRateUpdateRequest'>;
export type RatesRefreshResult = Schema<'RatesRefreshResult'>;
export type ConversionResult = Schema<'ConversionResult'>;
export type CollectionRequest = Schema<'TahsilatIstegi'>;
export type PaymentRequest = Schema<'OdemeIstegi'>;
export type DepositTakeRequest = Schema<'DepozitoAlIstegi'>;
export type DepositIncomeRequest = Schema<'DepozitoIratIstegi'>;
export type DepositRefundRequest = Schema<'DepositRefundRequest'>;
export type DepositOffsetRequest = Schema<'DepositOffsetRequest'>;
export type AccountOption = Schema<'FinansHesapOgesi'>;

export const FINANCE = '/api/ui/v1/finans';

/** Kimlikli alt yol (kimlik kaçışlanır). */
export function financePath(suffix: string): ApiPath {
  return `${FINANCE}${suffix}` as ApiPath;
}

export function customerPath(customerId: string, suffix: string): ApiPath {
  return financePath(`/cariler/${encodeURIComponent(customerId)}${suffix}`);
}

/** Kasa/banka hesap türü (`FinansApi.Hesap`: yalnız bu iki değer). */
export type AccountKind = 'Kasa' | 'Banka';
export const ACCOUNT_KINDS: readonly AccountKind[] = ['Kasa', 'Banka'];
export type AdjustmentDirection = 'Alacaklandir' | 'Borclandir';
/** Gider ödeme yöntemi (`OdemeYontemi` enum adları). */
export const PAYMENT_METHODS = ['Nakit', 'Banka', 'AcikHesap'] as const;
export type PaymentMethod = (typeof PAYMENT_METHODS)[number];
/** Gider tipi (`ExpenseType` enum adları; Blazor seçeneği enum sırasıyla). */
export const EXPENSE_TYPES = [
  'Genel',
  'Arac',
  'Personel',
  'Sigorta',
  'Mtv',
  'Muayene',
  'Diger',
  'Finansman',
] as const;
export const RENTAL_STATUSES = ['Kirada', 'Tamamlandi', 'Iptal'] as const;
export const TRANSACTION_TYPES = ['Tahsilat', 'Odeme'] as const;

type MoneyValue = string | number | null | undefined;

/** Boş/boşluk → `null`, aksi kırpılmış metin. */
export function textOrNull(v: string | null | undefined): string | null {
  const s = v?.trim() ?? '';
  return s === '' ? null : s;
}

/** Takvim günü → o günün İstanbul gece yarısı (UTC anı). Boş → `null` (sunucu bugünü kullanır). */
export function dayToInstant(day: DayText | null | undefined): string | null {
  return day ? mergeMoment(day, '00:00') : null;
}

/** Tutar (2 ondalık, invariant metin); boş → `''` (sunucu alan hatası verir, sessiz sıfır yok). */
export function amountText(v: MoneyValue): string {
  return moneyText(v, 2) ?? '';
}

/** Döviz + kur alanları birlikte (kur yalnız dövizde ve doluysa). */
export function currencyFields(currency: string | null | undefined, exchangeRate: MoneyValue) {
  return { doviz: currencyCode(currency), ...exchangeRateField(currency, exchangeRate) };
}

/** Deneme kaydı içeriği (`TahsilatDenemeKaydi`; tutar + döviz + hesap türü — kişisel veri yok). */
export interface AttemptContent {
  readonly tutar: string | number | null;
  readonly doviz: string;
  readonly hesap: string | null;
}

export interface CashFormValue {
  readonly tutar: MoneyValue;
  readonly doviz: string | null;
  readonly kur: MoneyValue;
  readonly hesap: AccountKind | null;
  readonly hesapId: string | null;
  readonly kanal: string | null;
  readonly tarih: DayText | null;
  readonly aciklama: string | null;
}

/** Nakit işlem / ekstre tahsilatı: kiraya bağlanmaz, deterministik anahtar YOK → `Idempotency-Key` başlığı. */
export function collectionBody(customerId: string, v: CashFormValue): CollectionRequest {
  return {
    cariId: customerId,
    tutar: amountText(v.tutar),
    hesap: v.hesap,
    hesapId: v.hesapId,
    ...currencyFields(v.doviz, v.kur),
    kanal: textOrNull(v.kanal),
    aciklama: textOrNull(v.aciklama),
    tarih: dayToInstant(v.tarih),
  };
}

/** Ödeme (tediye) gövdesi — tahsilatla aynı alanlar. */
export function paymentBody(customerId: string, v: CashFormValue): PaymentRequest {
  return collectionBody(customerId, v);
}

export interface TransferFormValue {
  readonly kaynak: AccountKind | null;
  readonly kaynakHesapId: string | null;
  readonly hedef: AccountKind | null;
  readonly hedefHesapId: string | null;
  readonly tutar: MoneyValue;
  readonly doviz: string | null;
  readonly kur: MoneyValue;
  readonly makbuzNo: string | null;
  readonly sube: string | null;
  readonly aciklama: string | null;
}

/** Kasa↔banka / hesaplar arası virman. "İşlemi yapan" formda YOK (oturumdan). */
export function cashTransferBody(v: TransferFormValue): CashTransferRequest {
  return {
    kaynak: v.kaynak,
    hedef: v.hedef,
    tutar: amountText(v.tutar),
    kaynakHesapId: v.kaynakHesapId,
    hedefHesapId: v.hedefHesapId,
    ...currencyFields(v.doviz, v.kur),
    makbuzNo: textOrNull(v.makbuzNo),
    sube: textOrNull(v.sube),
    aciklama: textOrNull(v.aciklama),
  };
}

export interface AdjustmentFormValue {
  readonly yon: AdjustmentDirection | null;
  readonly tutar: MoneyValue;
  readonly doviz: string | null;
  readonly kur: MoneyValue;
  readonly tarih: DayText | null;
  readonly vade: DayText | null;
  readonly makbuzNo: string | null;
  readonly aciklama: string | null;
}

/** Bakiye düzeltme: Kasa/Banka'ya DOKUNMAZ (karşı bacak Muhasebe Düzeltmesi). */
export function adjustmentBody(
  customerId: string,
  v: AdjustmentFormValue,
): BalanceAdjustmentRequest {
  return {
    cariId: customerId,
    yon: v.yon,
    tutar: amountText(v.tutar),
    ...currencyFields(v.doviz, v.kur),
    tarih: dayToInstant(v.tarih),
    vade: dayToInstant(v.vade),
    makbuzNo: textOrNull(v.makbuzNo),
    aciklama: textOrNull(v.aciklama),
  };
}

export interface CustomerTransferFormValue {
  readonly kaynakCariId: string | null;
  readonly hedefCariId: string | null;
  readonly tutar: MoneyValue;
  readonly doviz: string | null;
  readonly kur: MoneyValue;
  readonly tarih: DayText | null;
  readonly vade: DayText | null;
  readonly makbuzNo: string | null;
  readonly sube: string | null;
  readonly aciklama: string | null;
}

/** Cari↔cari virman: kaynak alacaklanır (bakiye ↓), hedef borçlanır (bakiye ↑). */
export function customerTransferBody(v: CustomerTransferFormValue): CustomerTransferRequest {
  return {
    kaynakCariId: v.kaynakCariId ?? '',
    hedefCariId: v.hedefCariId ?? '',
    tutar: amountText(v.tutar),
    ...currencyFields(v.doviz, v.kur),
    tarih: dayToInstant(v.tarih),
    vade: dayToInstant(v.vade),
    makbuzNo: textOrNull(v.makbuzNo),
    sube: textOrNull(v.sube),
    aciklama: textOrNull(v.aciklama),
  };
}

/** Tek cari toplu kapatma seçimi: `tutar` boş → kalemin KALANI (sunucu kuruşa aşağı yuvarlar). */
export interface CloseSelection {
  readonly kalemId: string;
  readonly tutar: MoneyValue;
}

export function closeItemsBody(
  selection: readonly CloseSelection[],
  v: {
    hesap: AccountKind | null;
    kanal: string | null;
    tarih: DayText | null;
    aciklama: string | null;
  },
): CloseItemsRequest {
  return {
    secim: selection.map((s) => ({ kalemId: s.kalemId, tutar: moneyText(s.tutar, 2) })),
    hesap: v.hesap,
    kanal: textOrNull(v.kanal),
    tarih: dayToInstant(v.tarih),
    aciklama: textOrNull(v.aciklama),
  };
}

export interface BulkCollectionRow {
  readonly cariId: string | null;
  readonly tutar: MoneyValue;
  readonly aciklama: string | null;
}

/** Çok cari toplu tahsilat (TRY, ATOMİK). Satır sırası korunur (sunucu satır anahtarı `RowKey(parti, i)`). */
export function bulkCollectionBody(
  rows: readonly BulkCollectionRow[],
  v: { hesap: AccountKind | null; hesapId: string | null; kanal: string | null },
): BulkCollectionRequest {
  return {
    satirlar: rows.map((r) => ({
      cariId: r.cariId ?? '',
      tutar: amountText(r.tutar),
      aciklama: textOrNull(r.aciklama),
    })),
    hesap: v.hesap,
    hesapId: v.hesapId,
    kanal: textOrNull(v.kanal),
  };
}

export interface BulkExpenseRow {
  readonly netTutar: MoneyValue;
  readonly aciklama: string | null;
  readonly aracId: string | null;
}

export interface BulkExpenseFormValue {
  readonly tip: string | null;
  readonly odemeYontemi: PaymentMethod | null;
  /** Kesir (0,20 = %20) — `rc-sayi-girdisi` sayısı. */
  readonly kdvOrani: number | null;
  readonly cariId: string | null;
  readonly vade: DayText | null;
  readonly finansalHesapId: string | null;
}

/** Toplu gider (ATOMİK). Plaka satır bazlı: her satır ayrı gider, tek gider araçlara bölünmez. */
export function bulkExpenseBody(
  rows: readonly BulkExpenseRow[],
  v: BulkExpenseFormValue,
): BulkExpenseRequest {
  return {
    satirlar: rows.map((r) => ({
      netTutar: amountText(r.netTutar),
      aciklama: textOrNull(r.aciklama),
      aracId: r.aracId,
    })),
    tip: v.tip,
    odemeYontemi: v.odemeYontemi,
    kdvOrani: v.kdvOrani ?? '',
    cariId: v.cariId,
    vade: dayToInstant(v.vade),
    finansalHesapId: v.finansalHesapId,
  };
}

/** Depozito işlemleri: al/irat F4.4 uçları, iade/mahsup F8.1a (hepsi `finans/depozito/*`). */
export type DepositOperation = 'al' | 'iade' | 'mahsup' | 'irat';

/**
 * Depozito gövdesi (TRY — Blazor formunda döviz yok). Al/İade kasa-banka hesabı taşır; Mahsup (cari borcuna) ve İrat
 * (gelire) nakit hesabı KULLANMAZ — gövdede de yok. İrat bu ekrandan kiraya atfedilmez (`kiraId` yok).
 */
export function depositRequest(
  op: DepositOperation,
  customerId: string,
  v: { tutar: MoneyValue; hesap: AccountKind | null; hesapId: string | null },
): { cariId: string; tutar: string; hesap?: AccountKind | null; hesapId?: string | null } {
  const base = { cariId: customerId, tutar: amountText(v.tutar) };
  return op === 'al' || op === 'iade' ? { ...base, hesap: v.hesap, hesapId: v.hesapId } : base;
}

/**
 * Toplu işlem satır hatası eşlemesi (r299 MEDIUM-1): sunucunun `satirlar[i].alan` hatası GÖNDERİLEN kopyanın i. satırına
 * aittir. O satırın kimliği ekranda hangi sıradaysa (`satirlar.<j>.<formAlanı>`) oraya yazılır; ekranda artık yoksa
 * eşlenmez (form üstü genel hataya düşer) — kayan satıra ASLA yazılmaz.
 */
export function rowErrorMap(
  sentIds: readonly string[],
  currentIds: readonly string[],
  fields: Readonly<Record<string, string>>,
): Record<string, string> {
  const map: Record<string, string> = {};
  sentIds.forEach((id, i) => {
    const j = currentIds.indexOf(id);
    if (j < 0) return;
    for (const [server, form] of Object.entries(fields))
      map[`satirlar[${i}].${server}`] = `satirlar.${j}.${form}`;
  });
  return map;
}

/** Otomatik tahsilat satır kimliği (kira + dönem sırası) — `@for` izleme anahtarı ve seçim. */
export function candidateKey(c: {
  readonly kiraId: string;
  readonly donemSira: number | string;
}): string {
  return `${c.kiraId}:${c.donemSira}`;
}

/** Sorgu parametreleri: boş değerler gönderilmez. */
export function queryParams(
  v: Readonly<Record<string, string | boolean | null | undefined>>,
): QueryParameters {
  return Object.fromEntries(
    Object.entries(v).filter(([, d]) => d !== null && d !== undefined && d !== '' && d !== false),
  ) as QueryParameters;
}
