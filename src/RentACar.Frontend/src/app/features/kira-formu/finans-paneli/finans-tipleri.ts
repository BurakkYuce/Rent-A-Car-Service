import type { Schema } from '@core/api/ui-tipleri';

/**
 * Sabit panel finans işlemleri (F4.4) sözleşme tipleri — `docs/api/ui-v1.json`'dan üretilen şemaların
 * takma adları. Uçlar: `Web/Api/Finans/FinansApi.cs` (yazma) + `Web/Api/Kira/KiraApi.cs` (alt kayıt okuma).
 */
export type CollectionInfo = Schema<'TahsilatBilgisi'>;
export type CollectionRequest = Schema<'TahsilatIstegi'>;
export type PaymentRequest = Schema<'OdemeIstegi'>;
export type IssueInvoiceRequest = Schema<'FaturaKesIstegi'>;
export type PeriodInvoiceRequest = Schema<'DonemFaturaIstegi'>;
export type PeriodInvoiceResponse = Schema<'DonemFaturaYaniti'>;
export type OutsourcedServiceRequest = Schema<'DisHizmetIstegi'>;
export type TakeDepositRequest = Schema<'DepozitoAlIstegi'>;
export type DepositForfeitRequest = Schema<'DepozitoIratIstegi'>;
export type FinanceTransactionResponse = Schema<'FinansIslemYaniti'>;
export type FinanceAccountItem = Schema<'FinansHesapOgesi'>;
export type ExchangeRateSelectItem = Schema<'KurSecimOgesi'>;
export type RentalInvoice = Schema<'KiraFaturaDto'>;
export type RentalPenaltyHgs = Schema<'KiraCezaHgsYaniti'>;
export type RentalPeriod = Schema<'KiraDonemDto'>;
export type RentalOutsourcedService = Schema<'KiraDisHizmetDto'>;

/** Kasa/banka hesap türü — `FinansApi.Hesap`: yalnız bu iki değer (sessizce Kasa'ya düşmez). */
export type AccountType = 'Kasa' | 'Banka';
