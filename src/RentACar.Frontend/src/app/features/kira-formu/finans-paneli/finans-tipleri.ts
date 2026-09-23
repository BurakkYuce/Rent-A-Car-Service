import type { Sema } from '@core/api/ui-tipleri';

/**
 * Sabit panel finans işlemleri (F4.4) sözleşme tipleri — `docs/api/ui-v1.json`'dan üretilen şemaların
 * takma adları. Uçlar: `Web/Api/Finans/FinansApi.cs` (yazma) + `Web/Api/Kira/KiraApi.cs` (alt kayıt okuma).
 */
export type TahsilatBilgisi = Sema<'TahsilatBilgisi'>;
export type TahsilatIstegi = Sema<'TahsilatIstegi'>;
export type OdemeIstegi = Sema<'OdemeIstegi'>;
export type FaturaKesIstegi = Sema<'FaturaKesIstegi'>;
export type DonemFaturaIstegi = Sema<'DonemFaturaIstegi'>;
export type DonemFaturaYaniti = Sema<'DonemFaturaYaniti'>;
export type DisHizmetIstegi = Sema<'DisHizmetIstegi'>;
export type DepozitoAlIstegi = Sema<'DepozitoAlIstegi'>;
export type DepozitoIratIstegi = Sema<'DepozitoIratIstegi'>;
export type FinansIslemYaniti = Sema<'FinansIslemYaniti'>;
export type FinansHesapOgesi = Sema<'FinansHesapOgesi'>;
export type KurSecimOgesi = Sema<'KurSecimOgesi'>;
export type KiraFatura = Sema<'KiraFaturaDto'>;
export type KiraCezaHgs = Sema<'KiraCezaHgsYaniti'>;
export type KiraDonem = Sema<'KiraDonemDto'>;
export type KiraDisHizmet = Sema<'KiraDisHizmetDto'>;

/** Kasa/banka hesap türü — `FinansApi.Hesap`: yalnız bu iki değer (sessizce Kasa'ya düşmez). */
export type HesapTuru = 'Kasa' | 'Banka';
