import type { Sayfa } from '@core/api/sayfa';
import type { Sema } from '@core/api/ui-tipleri';
import { listeTanimi } from '@core/veri/liste-sorgusu';

// ---- Servis / bakım (`/api/ui/v1/servisler`)
export type ServiceRecordRow = Sema<'ServiceRecordRow'>;
export type ServiceRecordDetail = Sema<'ServiceRecordDetail'>;
export type ServiceLine = Sema<'ServiceLineDto'>;
export type ServiceInfo = Sema<'ServiceInfoDto'>;
export type ServiceRecordRequest = Sema<'ServiceRecordRequest'>;
export type ServiceInfoRequest = Sema<'ServiceInfoRequest'>;
export type ServiceLineRequest = Sema<'ServiceLineRequest'>;
export type ServiceReflectRequest = Sema<'ServiceReflectRequest'>;

// ---- Sigorta / MTV / muayene (`/api/ui/v1/regulasyon`) + vade panosu
export type PolicyRow = Sema<'InsurancePolicyRow'>;
export type PolicyDetail = Sema<'InsurancePolicyDetail'>;
export type PolicyRequest = Sema<'InsurancePolicyRequest'>;
export type PolicyPaymentRequest = Sema<'InsurancePaymentRequest'>;
export type Endorsement = Sema<'EndorsementDto'>;
export type EndorsementRequest = Sema<'EndorsementRequest'>;
export type MtvRow = Sema<'MtvRow'>;
export type MtvDetail = Sema<'MtvDetail'>;
export type MtvRequest = Sema<'MtvRequest'>;
export type InspectionRow = Sema<'InspectionRow'>;
export type InspectionDetail = Sema<'InspectionDetail'>;
export type InspectionRequest = Sema<'InspectionRequest'>;
export type InstallmentPayment = Sema<'InstallmentPaymentDto'>;
export type InstallmentPaymentRequest = Sema<'InstallmentPaymentRequest'>;
export type InstallmentPaymentResult = Sema<'InstallmentPaymentResult'>;
export type RegulationOptions = Sema<'RegulationOptions'>;

/**
 * `GET /vade` satırı — üretilen şemadan. Eskiden bildirim merkezinin aynı adlı DTO'su şemayı eziyordu (tip elle
 * yazılmıştı); o kayıt `MessageDueItemDto` oldu ve yapısal test (`UiApiOpenApiSchemaNameTests`) çakışmayı kilitler.
 */
export type DueItem = Sema<'DueItemDto'>;

/** Sayfa zarfı çekirdeğin `Sayfa<T>`'si (üretilen `SayfaOf…` sayıları `number | string` yazar). */
export interface DueBoard {
  readonly ozet: Sema<'DueSummary'>;
  readonly kalemler: Sayfa<DueItem>;
}

export const SERVICES = '/api/ui/v1/servisler';
export const REGULATION = '/api/ui/v1/regulasyon';
export const DUE_BOARD = '/api/ui/v1/vade';

/** Kimlikli alt yol (kimlik kaçışlanır). */
export function recordPath(
  base: `/api/ui/v1/${string}`,
  id: string,
  suffix = '',
): `/api/ui/v1/${string}` {
  return `${base}/${encodeURIComponent(id)}${suffix}` as `/api/ui/v1/${string}`;
}

/** Sunucu enum ADLARI (tanımsız ad 400). Sıra Blazor sekme/seçim sırası. */
export const SERVICE_STATUSES = ['Rezerve', 'Acik', 'Serviste', 'Tamamlandi', 'Iptal'] as const;
export type ServiceStatus = (typeof SERVICE_STATUSES)[number];
export const SERVICE_TYPES = ['Periyodik', 'Ariza', 'Hasar', 'Lastik', 'Diger'] as const;
export type ServiceType = (typeof SERVICE_TYPES)[number];
export const DAMAGE_PARTIES = ['Yok', 'Sirket', 'Musteri', 'Sigorta'] as const;
export type DamageParty = (typeof DAMAGE_PARTIES)[number];
export const PAYMENT_METHODS = ['Nakit', 'Banka', 'AcikHesap'] as const;
export type PaymentMethod = (typeof PAYMENT_METHODS)[number];
export const INSURANCE_TYPES = ['Trafik', 'Kasko'] as const;
export type InsuranceType = (typeof INSURANCE_TYPES)[number];
export const DUE_BUCKETS = ['Gecmis', 'YediGun', 'OtuzGun', 'Ileri'] as const;
export type DueBucket = (typeof DUE_BUCKETS)[number];
/** Ödeme hesabı türü (sunucu `hesap`). */
export const ACCOUNT_KINDS = ['Kasa', 'Banka'] as const;
export type AccountKind = (typeof ACCOUNT_KINDS)[number];
/** Poliçe dövizleri (sunucu `secenekler.dovizler` gelene kadar varsayılan). */
export const POLICY_CURRENCIES = ['TRY', 'EUR', 'USD', 'GBP'] as const;
/** Beyan türü önerileri (Blazor datalist). */
export const DECLARATION_TYPES = ['Kaza Tespit Tutanağı', 'Anlaşmalı Beyan', 'Tek Taraflı'];

const PAID = ['true', 'false'] as const;

export const SERVICE_LIST = listeTanimi({
  filtreler: {
    durum: { tur: 'secim', degerler: SERVICE_STATUSES },
    tip: { tur: 'secim', degerler: SERVICE_TYPES },
    plaka: { tur: 'metin', enFazla: 32 },
    bas: { tur: 'tarih' },
    bit: { tur: 'tarih' },
  },
  siralanabilir: ['no', 'plaka', 'durum', 'tip', 'girisTarihi', 'toplamIscilik'],
  varsayilanSirala: '-girisTarihi',
});

export const POLICY_LIST = listeTanimi({
  filtreler: {
    plaka: { tur: 'metin', enFazla: 32 },
    odendi: { tur: 'secim', degerler: PAID },
    tip: { tur: 'secim', degerler: INSURANCE_TYPES },
    bitisBas: { tur: 'tarih' },
    bitisBit: { tur: 'tarih' },
  },
  siralanabilir: ['plaka', 'tip', 'bitis', 'prim', 'kalan', 'odendi'],
  varsayilanSirala: 'bitis',
});

export const MTV_LIST = listeTanimi({
  filtreler: {
    plaka: { tur: 'metin', enFazla: 32 },
    odendi: { tur: 'secim', degerler: PAID },
    vadeBas: { tur: 'tarih' },
    vadeBit: { tur: 'tarih' },
  },
  siralanabilir: ['plaka', 'donem', 'vade', 'tutar', 'kalan', 'odendi'],
  varsayilanSirala: 'vade',
});

export const INSPECTION_LIST = listeTanimi({
  filtreler: {
    plaka: { tur: 'metin', enFazla: 32 },
    odendi: { tur: 'secim', degerler: PAID },
    bitisBas: { tur: 'tarih' },
    bitisBit: { tur: 'tarih' },
  },
  siralanabilir: ['plaka', 'muayeneTarihi', 'bitis', 'ucret', 'kalan', 'odendi'],
  varsayilanSirala: 'bitis',
});

export const DUE_LIST = listeTanimi({
  filtreler: {
    kova: { tur: 'secim', degerler: DUE_BUCKETS },
    tur: { tur: 'metin', enFazla: 32 },
    plaka: { tur: 'metin', enFazla: 32 },
  },
  siralanabilir: ['plaka', 'tur', 'bitis', 'kalanGun'],
  varsayilanSirala: 'kalanGun',
});

/** API sayısı (`number | string`) → sayı; boş/geçersiz → `null`. */
export function num(v: number | string | null | undefined): number | null {
  if (v === null || v === undefined || v === '') return null;
  const n = typeof v === 'number' ? v : Number(v);
  return Number.isFinite(n) ? n : null;
}
