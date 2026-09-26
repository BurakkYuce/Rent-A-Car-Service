namespace RentACar.Application.Kur;

/// <summary>Çevirimde kullanılacak TCMB kur türü. Varsayılan Satış (müşteriye satış).</summary>
public enum ExchangeRateType
{
    Satis,        // Döviz Satış (ForexSelling) — varsayılan
    Alis,         // Döviz Alış (ForexBuying)
    EfektifSatis, // Efektif Satış (BanknoteSelling)
    EfektifAlis   // Efektif Alış (BanknoteBuying)
}
