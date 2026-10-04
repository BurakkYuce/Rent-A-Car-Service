using System.Security.Cryptography;
using System.Text;

namespace RentACar.IntegrationTests.Infrastructure;

/// <summary>
/// Testlerdeki okunur ETTN etiketinden ("ETTN-A1") GİB biçiminde (UUID, büyük harf) DETERMİNİSTİK ETTN üretir. Gelen
/// e-Fatura ETTN'i artık UUID olmak zorunda (<c>IncomingEInvoiceService.CreateManualAsync</c>); aynı etiket her çağrıda
/// aynı ETTN'i verir (tekillik ve tenant-izolasyon testleri buna dayanır). Servis de kanonik büyük harf saklar.
/// </summary>
public static class TestEttn
{
    /// <summary>Etiket zaten bir UUID ise kanonik hâli (büyük harf), değilse etiketten türetilen UUID.</summary>
    public static string Of(string label)
        => (Guid.TryParseExact(label, "D", out var g) ? g : new Guid(MD5.HashData(Encoding.UTF8.GetBytes(label))))
            .ToString("D").ToUpperInvariant();
}
