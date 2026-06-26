namespace RentACar.Domain.Common;

/// <summary>
/// Çok-dövizli para değer nesnesi: tutar + döviz + kur birlikte saklanır.
/// Tüm parasal işlemler bu üçlüyle taşınır (TL/USD/EUR). decimal kullanılır;
/// Postgres tarafında numeric'e map edilir.
/// PR #1'de yalnız iskelet amaçlı (AccountLedgerEntry); para mantığı henüz yok.
/// </summary>
public readonly record struct Money(decimal Amount, string Currency, decimal Rate)
{
    /// <summary>Yerel para birimine (kur uygulanmış) karşılığı.</summary>
    public decimal AmountInBase => Amount * Rate;

    public static Money Zero(string currency) => new(0m, currency, 1m);

    public override string ToString() => $"{Amount:0.00} {Currency} @ {Rate:0.######}";
}
