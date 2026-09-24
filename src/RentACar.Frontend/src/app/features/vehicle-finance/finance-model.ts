import type { Sema } from '@core/api/ui-tipleri';
import { trBuyukHarf } from '@core/metin/tr-normalize';
import { listeTanimi } from '@core/veri/liste-sorgusu';

const currencyKey = (c: string | null | undefined) => trBuyukHarf((c ?? '').trim());

/**
 * Gönderilecek kur (inceleme M2): kaydın dövizi değiştiyse ve kullanıcı kuru AÇIKÇA değiştirmediyse (formdaki kur hâlâ
 * kaydın eski kuru) kur gönderilmez → sunucu yeni döviz için çözer (TRY = 1; dövizde firma kuru → TCMB). Aksi halde
 * TRY kaydı EUR'ya çevrilince eski kur 1 "açık kur" sayılıp baz tutarı yanlış yazardı. Yeni kayıtta ve döviz aynıysa
 * formdaki değer aynen gider.
 */
export function rateToSend(
  currency: string | null,
  rate: number | null,
  base: { readonly doviz: string; readonly kur: number | string } | null,
): number | null {
  if (base === null || currencyKey(currency || 'TRY') === currencyKey(base.doviz)) return rate;
  const baseRate = typeof base.kur === 'number' ? base.kur : Number(base.kur);
  return rate === null || rate === baseRate ? null : rate;
}

// ---- Araç kredisi (`/api/ui/v1/arac-kredileri`)
export type LoanRow = Sema<'AracKrediListeSatiri'>;
export type LoanBoard = Sema<'AracKrediPano'>;
export type LoanDetail = Sema<'AracKrediDetayYaniti'>;
export type LoanInstallment = Sema<'AracKrediTaksit'>;
export type LoanRequest = Sema<'AracKrediIstegi'>;
export type LoanCreated = Sema<'AracKrediOlusturYaniti'>;
export type InstallmentPayRequest = Sema<'TaksitOdeIstegi'>;
export type InstallmentPayResponse = Sema<'TaksitOdeYaniti'>;
export type BulkCancelResponse = Sema<'KrediTopluIptalYaniti'>;

// ---- Müşteri taksiti (`/api/ui/v1/musteri-taksitleri`)
export type CustomerInstallment = Sema<'MusteriTaksitSatiri'>;
export type CustomerInstallmentRequest = Sema<'MusteriTaksitIstegi'>;
export type CustomerInstallmentSummary = Sema<'TaksitOzet'>;
export type InstallmentPlanRequest = Sema<'TaksitPlanIstegi'>;

// ---- Araç siparişi (`/api/ui/v1/arac-siparisleri`)
export type OrderRow = Sema<'AracSiparisSatiri'>;
export type OrderDetail = Sema<'AracSiparisDto'>;
export type OrderRequest = Sema<'AracSiparisIstegi'>;

// ---- BAF, hasar, filo plan
export type Allocation = Sema<'BafDto'>;
export type AllocationRequest = Sema<'BafIstegi'>;
export type AllocationReturnRequest = Sema<'BafTeslimIstegi'>;
export type DamageFile = Sema<'HasarDto'>;
export type DamageFileRequest = Sema<'HasarIstegi'>;
export type FleetPlan = Sema<'FiloPlanDto'>;
export type FleetPlanRequest = Sema<'FiloPlanIstegi'>;

/** Sunucu enum ADLARI (tanımsız ad 400). */
export const LOAN_STATUSES = ['Aktif', 'Kapandi', 'Iptal'] as const;
export const CUSTOMER_INSTALLMENT_STATUSES = ['Bekliyor', 'Odendi'] as const;
export type CustomerInstallmentStatus = (typeof CUSTOMER_INSTALLMENT_STATUSES)[number];
export const ORDER_STATUSES = ['Bekliyor', 'Onaylandi', 'TeslimAlindi', 'Iptal'] as const;
export const ALLOCATION_STATUSES = ['Acik', 'Kapandi', 'Iptal'] as const;
export const ALLOCATION_PURPOSES = [
  'AracAyirma',
  'AracDonusu',
  'AracTeslimati',
  'Yikama',
  'ServisBakim',
  'Muayene',
  'LastikDegisimi',
  'YakitIkmali',
  'SubelerArasiTransfer',
  'PersonelKullanimi',
  'Diger',
] as const;
export type AllocationPurpose = (typeof ALLOCATION_PURPOSES)[number];
export const ALLOCATION_LOCATIONS = ['AyniOfis', 'FarkliOfis'] as const;
export const DAMAGE_STATUSES = ['Acik', 'Onayda', 'Onaylandi', 'Reddedildi', 'Kapali'] as const;
/** Kasa/Banka (taksit ödemesinin hesap türü; sunucu `hesap`). */
export const ACCOUNT_KINDS = ['Kasa', 'Banka'] as const;
export type AccountKind = (typeof ACCOUNT_KINDS)[number];
/** Blazor sipariş formunun döviz seçenekleri. */
export const ORDER_CURRENCIES = ['TRY', 'USD', 'EUR'] as const;

/** Blazor `/arac-kredi` süzgeçleri (cari, plaka, dosya no, durum, başlangıç aralığı). */
export const LOAN_LIST = listeTanimi({
  filtreler: {
    cariId: { tur: 'kimlik' },
    plaka: { tur: 'metin', enFazla: 20 },
    dosyaNo: { tur: 'metin', enFazla: 64 },
    durum: { tur: 'secim', degerler: LOAN_STATUSES },
    bas: { tur: 'tarih' },
    bit: { tur: 'tarih' },
  },
  siralanabilir: [
    'no',
    'bankaAdi',
    'plaka',
    'cari',
    'krediTutari',
    'baslangicTarihi',
    'kalanBakiye',
    'durum',
  ],
  varsayilanSirala: null,
  varsayilanBoyut: 50,
});

/** Blazor `/musteri-taksit` süzgeçleri (müşteri, araç, durum, vade aralığı, yalnız gecikenler). */
export const CUSTOMER_INSTALLMENT_LIST = listeTanimi({
  filtreler: {
    cariId: { tur: 'kimlik' },
    vehicleId: { tur: 'kimlik' },
    durum: { tur: 'secim', degerler: CUSTOMER_INSTALLMENT_STATUSES },
    vadeMin: { tur: 'tarih' },
    vadeMax: { tur: 'tarih' },
    gecikmis: { tur: 'bayrak' },
  },
  siralanabilir: ['vade', 'sira', 'cari', 'plaka', 'taksitTutari', 'tutarBaz', 'durum'],
  varsayilanSirala: null,
  varsayilanBoyut: 50,
});

/** Blazor `/arac-siparis` süzgeçleri. */
export const ORDER_LIST = listeTanimi({
  filtreler: {
    cariId: { tur: 'kimlik' },
    ara: { tur: 'metin', enFazla: 100 },
    arac: { tur: 'metin', enFazla: 100 },
    dosyaNo: { tur: 'metin', enFazla: 64 },
    durum: { tur: 'secim', degerler: ORDER_STATUSES },
    bas: { tur: 'tarih' },
    bit: { tur: 'tarih' },
  },
  siralanabilir: ['no', 'tedarikci', 'siparisTarihi', 'beklenenTeslim', 'toplam', 'durum'],
  varsayilanSirala: null,
  varsayilanBoyut: 50,
});

/** Blazor `/baf` süzgeçleri (FAZ-18 arama paneli). */
export const ALLOCATION_LIST = listeTanimi({
  filtreler: {
    personelId: { tur: 'kimlik' },
    plaka: { tur: 'metin', enFazla: 20 },
    durum: { tur: 'secim', degerler: ALLOCATION_STATUSES },
    kullanimAmaci: { tur: 'secim', degerler: ALLOCATION_PURPOSES },
    lokasyon: { tur: 'secim', degerler: ALLOCATION_LOCATIONS },
    ofis: { tur: 'metin', enFazla: 100 },
    bas: { tur: 'tarih' },
    bit: { tur: 'tarih' },
  },
  siralanabilir: ['no', 'plaka', 'personel', 'cikisTarihi', 'donusTarihi', 'durum'],
  varsayilanSirala: null,
  varsayilanBoyut: 50,
});

/** `/hasar` (Blazor süzgeçsiz; API durum + araç süzgeci verir). */
export const DAMAGE_LIST = listeTanimi({
  filtreler: {
    durum: { tur: 'secim', degerler: DAMAGE_STATUSES },
  },
  siralanabilir: ['no', 'plaka', 'acilisTarihi', 'tahminiTutar', 'durum'],
  varsayilanSirala: null,
  varsayilanBoyut: 50,
});

/** `/filo-plan`: süzgeçsiz; toplam satırı TÜM hedeflerden (Blazor gibi) → tek sayfada en çok 200. */
export const FLEET_PLAN_LIST = listeTanimi({
  filtreler: {},
  siralanabilir: ['aracGrupAdi', 'sipp', 'donem', 'hedefAdet', 'gerceklesen', 'fark'],
  varsayilanSirala: null,
  varsayilanBoyut: 200,
});

/**
 * Dışa aktarma parametreleri: Blazor uçları kendi (eski) sorgu adlarını okur (`cariF`, `plakaF`…). Ekrandaki süzgeç
 * AYNEN taşınır ("gördüğün = indirdiğin").
 */
export function exportParameters(
  filters: Readonly<Record<string, string | number | boolean | undefined>>,
  names: Readonly<Record<string, string>>,
): Record<string, string> {
  const out: Record<string, string> = {};
  for (const [api, legacy] of Object.entries(names)) {
    const v = filters[api];
    if (v !== undefined && v !== null && v !== '') out[legacy] = String(v);
  }
  return out;
}

export const LOAN_EXPORT_NAMES = {
  cariId: 'cariF',
  plaka: 'plakaF',
  dosyaNo: 'dosyaF',
  durum: 'durumF',
  bas: 'bas',
  bit: 'bit',
} as const;

export const ORDER_EXPORT_NAMES = {
  cariId: 'cariF',
  ara: 'araF',
  arac: 'aracF',
  dosyaNo: 'dosyaF',
  durum: 'durumF',
  bas: 'bas',
  bit: 'bit',
} as const;
