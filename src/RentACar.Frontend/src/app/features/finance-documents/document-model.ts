import type { Sema } from '@core/api/ui-tipleri';
import { listeTanimi } from '@core/veri/liste-sorgusu';

// ---- Fatura (`/api/ui/v1/faturalar`)
export type InvoiceRow = Sema<'InvoiceListRow'>;
export type InvoiceDetail = Sema<'InvoiceDetail'>;
export type InvoiceLineRow = Sema<'InvoiceLineListRow'>;
export type ManualInvoiceRequest = Sema<'ManualInvoiceRequest'>;
export type BatchInvoiceRequest = Sema<'BatchInvoiceRequest'>;
export type BatchInvoiceResult = Sema<'BatchInvoiceResult'>;
export type DocumentResult = Sema<'DocumentResult'>;
export type RentalRow = Sema<'KiraListeSatiri'>;

// ---- Ceza (`/api/ui/v1/cezalar`)
export type PenaltyRow = Sema<'PenaltyListRow'>;
export type PenaltyDetail = Sema<'PenaltyDetail'>;
export type PenaltyLine = Sema<'PenaltyLineDto'>;
export type PenaltyCreateRequest = Sema<'PenaltyCreateRequest'>;
export type PenaltyPaymentRequest = Sema<'PenaltyPaymentRequest'>;
export type PenaltyPaymentResult = Sema<'PenaltyPaymentResult'>;
export type PenaltyType = Sema<'PenaltyTypeDto'>;

// ---- Gider (`/api/ui/v1/giderler`)
export type ExpenseRow = Sema<'ExpenseListRow'>;
export type ExpenseCreateRequest = Sema<'ExpenseCreateRequest'>;
export type ExpensePaymentRequest = Sema<'ExpensePaymentRequest'>;
export type ExpensePayment = Sema<'ExpensePaymentDto'>;
export type ExpenseCategory = Sema<'ExpenseCategoryDto'>;

// ---- Gelen e-fatura (`/api/ui/v1/gelen-efatura`)
export type IncomingInvoiceRow = Sema<'IncomingInvoiceRow'>;
export type IncomingInvoiceDetail = Sema<'IncomingInvoiceDetail'>;
export type IncomingInvoiceCreateRequest = Sema<'IncomingInvoiceCreateRequest'>;
export type IncomingInvoiceLinkRequest = Sema<'IncomingInvoiceLinkRequest'>;
export type IncomingInvoiceLinkResult = Sema<'IncomingInvoiceLinkResult'>;
export type IncomingInvoiceExpenseRequest = Sema<'IncomingInvoiceExpenseRequest'>;
export type IncomingInvoiceExpenseResult = Sema<'IncomingInvoiceExpenseResult'>;
export type IncomingInvoiceSyncResult = Sema<'IncomingInvoiceSyncResult'>;

// ---- Araç satışı (`/api/ui/v1/satislar`)
export type VehicleSaleRow = Sema<'VehicleSaleRow'>;
export type VehicleSaleRequest = Sema<'VehicleSaleCreateRequest'>;

/** Sunucu enum ADLARI (tanımsız ad 400). */
export const PENALTY_STATUSES = ['Yeni', 'Yansitildi', 'Odendi', 'Iptal', 'Kismi'] as const;
export const PENALTY_PAYMENT_STATUSES = ['Odenmemis', 'Kismi', 'Odendi'] as const;
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
export type ExpenseType = (typeof EXPENSE_TYPES)[number];
export const PAYMENT_METHODS = ['Nakit', 'Banka', 'AcikHesap'] as const;
export type PaymentMethod = (typeof PAYMENT_METHODS)[number];
export const INCOMING_STATUSES = ['Beklemede', 'Onaylandi', 'Reddedildi', 'Islendi'] as const;
export const SALE_STATUSES = ['Tamamlandi', 'Iptal'] as const;
export const ACCOUNT_KINDS = ['Kasa', 'Banka'] as const;
export type AccountKind = (typeof ACCOUNT_KINDS)[number];
export const CURRENCIES = ['TRY', 'USD', 'EUR'] as const;
/** Üç durumlu evet/hayır süzgeci (URL'de `true`/`false`; yoksa tümü). */
export const YES_NO = ['true', 'false'] as const;

/**
 * KDV oranları KESİR olarak (sunucu `kdvOrani` 0,20 = %20). Seçim listesi: istemci yüzdeyi kesre ÇEVİRMEZ (formül yok);
 * gönderilen değer sunucunun beklediği metnin kendisidir.
 */
export const VAT_RATES = ['0.20', '0.10', '0.01', '0'] as const;
export type VatRate = (typeof VAT_RATES)[number];
export const VAT_LABELS: Readonly<Record<VatRate, string>> = {
  '0.20': '%20',
  '0.10': '%10',
  '0.01': '%1',
  '0': '%0',
};

/** Blazor "KDV Sıfır Sebep" seçenekleri (aynı metinler; sunucu serbest metin saklar). */
export const ZERO_VAT_REASONS = [
  'İstisna Olmayan Diğer',
  '15/b Uluslararası Kuruluşlara',
  '11/1-A Hizmet İhracatı',
  '350-Diğerleri',
] as const;
export const INVOICE_PAYMENT_TYPES = ['Kart', 'Havale', 'Nakit'] as const;
export const INVOICE_DELIVERY_TYPES = ['Mail', 'Kargo', 'Posta'] as const;
export const SALE_CHANNELS = ['Galeri', 'Açık Artırma', 'Bireysel', 'Kurumsal'] as const;
/** Blazor gider "Hazır Açıklama" önerileri (seç veya yaz). */
export const EXPENSE_PRESETS = [
  'Yakıt',
  'Otopark',
  'Köprü/Otoyol',
  'Temizlik',
  'Lastik',
  'Periyodik bakım',
  'Sigorta',
  'Vergi/Harç',
  'Kira',
  'Personel',
  'Kırtasiye',
  'Diğer',
] as const;

// ------------------------------------------------------------------ liste tanımları (URL = API adları)

export const INVOICE_LIST = listeTanimi({
  filtreler: {
    q: { tur: 'metin', enFazla: 128 },
    cariId: { tur: 'kimlik' },
    iptal: { tur: 'secim', degerler: YES_NO },
    doviz: { tur: 'metin', enFazla: 8 },
    ofis: { tur: 'metin', enFazla: 128 },
    bas: { tur: 'tarih' },
    bit: { tur: 'tarih' },
  },
  siralanabilir: ['no', 'tarih', 'vadeTarihi', 'cariAd', 'genelToplam', 'durum', 'doviz'],
  varsayilanSirala: null,
  varsayilanBoyut: 50,
});

export const INVOICE_LINE_LIST = listeTanimi({
  filtreler: {
    q: { tur: 'metin', enFazla: 128 },
    cariId: { tur: 'kimlik' },
    plaka: { tur: 'metin', enFazla: 16 },
    ofis: { tur: 'metin', enFazla: 128 },
    bas: { tur: 'tarih' },
    bit: { tur: 'tarih' },
    iptalleriGizle: { tur: 'bayrak' },
  },
  siralanabilir: ['faturaNo', 'tarih', 'cariAd', 'satirToplam', 'kdvOrani'],
  varsayilanSirala: null,
  varsayilanBoyut: 100,
});

export const PENALTY_LIST = listeTanimi({
  filtreler: {
    musteri: { tur: 'metin', enFazla: 128 },
    makbuzNo: { tur: 'metin', enFazla: 64 },
    plaka: { tur: 'metin', enFazla: 16 },
    bas: { tur: 'tarih' },
    bit: { tur: 'tarih' },
    durum: { tur: 'secim', degerler: PENALTY_STATUSES },
    odemeDurumu: { tur: 'secim', degerler: PENALTY_PAYMENT_STATUSES },
    islemSube: { tur: 'metin', enFazla: 128 },
  },
  siralanabilir: ['no', 'tebligTarihi', 'vadeTarihi', 'tutar', 'kalan', 'durum', 'plaka', 'cariAd'],
  varsayilanSirala: null,
  varsayilanBoyut: 50,
});

export const EXPENSE_LIST = listeTanimi({
  filtreler: {
    q: { tur: 'metin', enFazla: 128 },
    cariId: { tur: 'kimlik' },
    plaka: { tur: 'metin', enFazla: 16 },
    tip: { tur: 'secim', degerler: EXPENSE_TYPES },
    sube: { tur: 'metin', enFazla: 64 },
    bas: { tur: 'tarih' },
    bit: { tur: 'tarih' },
  },
  siralanabilir: ['no', 'tarih', 'tip', 'plaka', 'cariAd', 'genelToplam', 'kalan', 'sube'],
  varsayilanSirala: null,
  varsayilanBoyut: 50,
});

export const INCOMING_LIST = listeTanimi({
  filtreler: {
    firma: { tur: 'metin', enFazla: 256 },
    ettnBas: { tur: 'metin', enFazla: 64 },
    ettnBit: { tur: 'metin', enFazla: 64 },
    plaka: { tur: 'metin', enFazla: 16 },
    durum: { tur: 'secim', degerler: INCOMING_STATUSES },
    bas: { tur: 'tarih' },
    bit: { tur: 'tarih' },
    giderlestirildi: { tur: 'secim', degerler: YES_NO },
  },
  siralanabilir: ['tarih', 'ettn', 'gonderenUnvan', 'genelToplam', 'durum'],
  varsayilanSirala: null,
  varsayilanBoyut: 50,
});

export const SALE_LIST = listeTanimi({
  filtreler: {
    plaka: { tur: 'metin', enFazla: 16 },
    aliciCariId: { tur: 'kimlik' },
    durum: { tur: 'secim', degerler: SALE_STATUSES },
    satisiVerildi: { tur: 'secim', degerler: YES_NO },
    ofis: { tur: 'metin', enFazla: 128 },
    bas: { tur: 'tarih' },
    bit: { tur: 'tarih' },
  },
  siralanabilir: ['no', 'tarih', 'plaka', 'aliciAd', 'genelToplam'],
  varsayilanSirala: null,
  varsayilanBoyut: 50,
});

/** Dışa aktarma: Blazor uçları kendi sorgu adlarını okur (gider ve fatura detay listesinde `ara`). */
export const EXPENSE_EXPORT_NAMES = {
  q: 'ara',
  plaka: 'plaka',
  cariId: 'cariId',
  tip: 'tip',
  sube: 'sube',
  bas: 'bas',
  bit: 'bit',
} as const;

/** Fatura detay listesi dışa aktarması (`iptal=gizle` Blazor adı). */
export function invoiceLineExportParameters(
  f: Readonly<Record<string, string | number | boolean | undefined>>,
): Record<string, string> {
  const out: Record<string, string> = {};
  const copy: readonly (readonly [string, string])[] = [
    ['q', 'ara'],
    ['cariId', 'cariId'],
    ['plaka', 'plaka'],
    ['ofis', 'ofis'],
    ['bas', 'bas'],
    ['bit', 'bit'],
  ];
  for (const [api, legacy] of copy) {
    const v = f[api];
    if (v !== undefined && v !== '') out[legacy] = String(v);
  }
  if (f['iptalleriGizle'] === true) out['iptal'] = 'gizle';
  return out;
}
