import { trUpperCase } from '@core/metin/tr-normalize';

const iso = (c: string | null | undefined): string => {
  const k = trUpperCase((c ?? '').trim());
  return k === '' || k === 'TL' ? 'TRY' : k;
};

/**
 * Hesabın dövizi seçilen işlem dövizinden farklı mı (hesap dövizi bilinmiyorsa `false` — sunucu karar verir).
 * "TL" eski etiketi TRY sayılır (sunucu da ISO koda indirger).
 */
export function currencyMismatch(
  accountCurrency: string | null | undefined,
  currency: string | null | undefined,
): boolean {
  if (!accountCurrency?.trim()) return false;
  return iso(accountCurrency) !== iso(currency);
}
