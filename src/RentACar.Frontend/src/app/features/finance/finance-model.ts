import type { ApiYolu, SorguParametreleri } from '@core/api/api-istemcisi';
import type { Sema } from '@core/api/ui-tipleri';
import { type GunMetni, anBirlestir } from '@core/form/tarih-girdisi';
import { dovizKodu, kurAlani, paraMetni } from '@features/kira-formu/finans-paneli/finans-modeli';

/**
 * F8.2a finans ekranlarının SAF kuralları: tipler (üretilen şemaların takma adları), uç yolları ve istek gövdeleri.
 * Tutar formülü YOK — bakiye, yürüyen bakiye, mizan, toplamlar sunucudan gelir. Para değerleri invariant ondalık
 * METİN taşınır (`rc-para-girdisi` → `"1500.50"`); kur alanı kira formunun kuralıyla (`kurAlani`): TRY'de gönderilmez,
 * dövizde boşsa gönderilmez (sunucu firma sabit kuru → TCMB ile çözer).
 */

export type CashboxSummary = Sema<'CashboxSummary'>;
export type CashTransactionList = Sema<'CashTransactionList'>;
export type CashTransactionRow = Sema<'CashTransactionRow'>;
export type CashTransferRow = Sema<'CashTransferRow'>;
export type CashTransferRequest = Sema<'CashTransferRequest'>;
export type CashOperationResult = Sema<'CashOperationResult'>;
export type CustomerBalance = Sema<'CustomerBalance'>;
export type BalanceAdjustmentRequest = Sema<'BalanceAdjustmentRequest'>;
export type CustomerTransferRequest = Sema<'CustomerTransferRequest'>;
export type CustomerTransferRow = Sema<'CustomerTransferRow'>;
export type CustomerStatement = Sema<'CustomerStatement'>;
export type CustomerStatementLine = Sema<'CustomerStatementLine'>;
export type CustomerOpenItems = Sema<'CustomerOpenItems'>;
export type CustomerOpenItem = Sema<'CustomerOpenItem'>;
export type CloseItemsRequest = Sema<'CloseItemsRequest'>;
export type CloseItemsResult = Sema<'CloseItemsResult'>;
export type BulkCollectionRequest = Sema<'BulkCollectionRequest'>;
export type BulkPostingResult = Sema<'BulkPostingResult'>;
export type BulkExpenseRequest = Sema<'BulkExpenseRequest'>;
export type DepositBalanceRow = Sema<'DepositBalanceRow'>;
export type PeriodCloseState = Sema<'PeriodCloseState'>;
export type AutoCollectionList = Sema<'AutoCollectionList'>;
export type AutoCollectionCandidate = Sema<'AutoCollectionCandidate'>;
export type AutoCollectionRequest = Sema<'AutoCollectionRequest'>;
export type AutoCollectionResult = Sema<'AutoCollectionResult'>;
export type RatesScreen = Sema<'RatesScreen'>;
export type FixedRate = Sema<'FixedRate'>;
export type FixedRateCreateRequest = Sema<'FixedRateCreateRequest'>;
export type FixedRateUpdateRequest = Sema<'FixedRateUpdateRequest'>;
export type RatesRefreshResult = Sema<'RatesRefreshResult'>;
export type ConversionResult = Sema<'ConversionResult'>;
export type CollectionRequest = Sema<'TahsilatIstegi'>;
export type PaymentRequest = Sema<'OdemeIstegi'>;
export type DepositTakeRequest = Sema<'DepozitoAlIstegi'>;
export type DepositIncomeRequest = Sema<'DepozitoIratIstegi'>;
export type DepositRefundRequest = Sema<'DepositRefundRequest'>;
export type DepositOffsetRequest = Sema<'DepositOffsetRequest'>;
export type AccountOption = Sema<'FinansHesapOgesi'>;

export const FINANCE = '/api/ui/v1/finans';

/** Kimlikli alt yol (kimlik kaçışlanır). */
export function financePath(suffix: string): ApiYolu {
  return `${FINANCE}${suffix}` as ApiYolu;
}

export function customerPath(cariId: string, suffix: string): ApiYolu {
  return financePath(`/cariler/${encodeURIComponent(cariId)}${suffix}`);
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
export function dayToInstant(day: GunMetni | null | undefined): string | null {
  return day ? anBirlestir(day, '00:00') : null;
}

/** Tutar (2 ondalık, invariant metin); boş → `''` (sunucu alan hatası verir, sessiz sıfır yok). */
export function amountText(v: MoneyValue): string {
  return paraMetni(v, 2) ?? '';
}

/** Döviz + kur alanları birlikte (kur yalnız dövizde ve doluysa). */
export function currencyFields(doviz: string | null | undefined, kur: MoneyValue) {
  return { doviz: dovizKodu(doviz), ...kurAlani(doviz, kur) };
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
  readonly tarih: GunMetni | null;
  readonly aciklama: string | null;
}

/** Nakit işlem / ekstre tahsilatı: kiraya bağlanmaz, deterministik anahtar YOK → `Idempotency-Key` başlığı. */
export function collectionBody(cariId: string, v: CashFormValue): CollectionRequest {
  return {
    cariId,
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
export function paymentBody(cariId: string, v: CashFormValue): PaymentRequest {
  return collectionBody(cariId, v);
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
  readonly tarih: GunMetni | null;
  readonly vade: GunMetni | null;
  readonly makbuzNo: string | null;
  readonly aciklama: string | null;
}

/** Bakiye düzeltme: Kasa/Banka'ya DOKUNMAZ (karşı bacak Muhasebe Düzeltmesi). */
export function adjustmentBody(cariId: string, v: AdjustmentFormValue): BalanceAdjustmentRequest {
  return {
    cariId,
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
  readonly tarih: GunMetni | null;
  readonly vade: GunMetni | null;
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
    tarih: GunMetni | null;
    aciklama: string | null;
  },
): CloseItemsRequest {
  return {
    secim: selection.map((s) => ({ kalemId: s.kalemId, tutar: paraMetni(s.tutar, 2) })),
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
  readonly vade: GunMetni | null;
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
  cariId: string,
  v: { tutar: MoneyValue; hesap: AccountKind | null; hesapId: string | null },
): { cariId: string; tutar: string; hesap?: AccountKind | null; hesapId?: string | null } {
  const base = { cariId, tutar: amountText(v.tutar) };
  return op === 'al' || op === 'iade' ? { ...base, hesap: v.hesap, hesapId: v.hesapId } : base;
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
): SorguParametreleri {
  return Object.fromEntries(
    Object.entries(v).filter(([, d]) => d !== null && d !== undefined && d !== '' && d !== false),
  ) as SorguParametreleri;
}
