using System.Security.Cryptography;

namespace RentACar.Application.Common;

/// <summary>
/// Tahmin edilemez URL token'ı: 32-byte CSPRNG → Base64Url (256 bit entropi; adres satırında,
/// QR'da ve WhatsApp mesajında güvenle taşınır — `+`, `/`, `=` yok).
///
/// <para>Kural TEK yerde: iCal feed token'ı (<c>CalendarTokenUtil</c>) ve sözleşme paylaşım linki
/// (PR-C) aynı üreticiyi kullanır. Kopyalanan kripto, birinde düzeltilip diğerinde kalan zafiyet
/// üretir (O12c dersi: el-klonu kaldırıldı, kural tek yerde).</para>
/// </summary>
public static class GuvenliToken
{
    public static string Uret()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
