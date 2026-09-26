using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RentACar.Web.Finance;

/// <summary>
/// Hızlı-tahsilat formu için DETERMİNİSTİK idempotency anahtarı (dashboard Dönüşler + kira listesi).
/// Anahtar = SHA256("rentalId:bakiye:islemSayisi") ilk 16 byte → Guid.
/// Neden üç bileşen: yalnız (rentalId, bakiye) ZAMANSAL ÇAKIŞIR — aylık kirada Bakiye ertesi ay aynı
/// değere geri döner, aynı anahtar üretilir ve MEŞRU tahsilat "mükerrer" diye bloklanır. islemSayisi
/// (kiranın kasa/banka işlem SAYISI, ters kayıtlar dahil) monoton artar → her tahsilat/ters-kayıt
/// sonrası anahtar değişir; double-click ve stale-tab (aynı render → aynı snapshot) yine bloklanır.
/// Bakiye InvariantCulture ile yazılır: tr-TR "1250,50" basar, anahtar ortamlar arası ayrışırdı.
/// Dedupe DB'de: CashTransaction (TenantId, IslemAnahtari) kısmi unique index (FinanceConfigs).
/// </summary>
public static class CollectionKey
{
    public static Guid Generate(Guid rentalId, decimal balance, int transactionCount)
    {
        var input = $"{rentalId}:{balance.ToString(CultureInfo.InvariantCulture)}:{transactionCount}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return new Guid(hash.AsSpan(0, 16));
    }
}
