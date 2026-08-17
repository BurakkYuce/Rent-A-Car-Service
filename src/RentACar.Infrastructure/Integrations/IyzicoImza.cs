using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RentACar.Infrastructure.Integrations;

/// <summary>
/// iyzico IYZWSv2 (HMAC-SHA256) kimlik doğrulama başlığını üretir.
///
/// <para>SAF ve <c>public</c>: ağa çıkmadan doğrudan test edilir. Bu sınıfın doğruluğu iyzico'nun
/// kendi dokümanındaki örnek başlıkla BİREBİR karşılaştırılarak kilitlenmiştir
/// (<c>IyzicoImzaTests</c>) — imza hatası "Geçersiz imza / errorCode 1000" olarak döner ve
/// teşhisi zordur, bu yüzden kurgu koddan değil dokümandan sabitlenir.</para>
///
/// <para><b>İmza yükü SIRALIDIR:</b> <c>randomKey + uriPath + requestBody</c>. uriPath yalnız yoldur
/// (host YOK, varsa sorgu dizesi dâhil); requestBody gönderilen gövdenin BİREBİR kendisidir —
/// yeniden serileştirilirse (boşluk/sıra farkı) imza tutmaz.</para>
/// </summary>
public static class IyzicoImza
{
    /// <summary>
    /// <c>Authorization</c> başlığının değeri (<c>IYZWSv2 &lt;base64&gt;</c>) ve ona eşlik etmesi
    /// gereken <c>x-iyzi-rnd</c> değerini üretir.
    /// </summary>
    /// <param name="apiKey">Firma API anahtarı.</param>
    /// <param name="secretKey">Firma güvenlik anahtarı (HMAC anahtarı).</param>
    /// <param name="uriPath">İstek yolu, ör. <c>/payment/bin/check</c>.</param>
    /// <param name="requestBody">Gönderilecek gövdenin birebir metni.</param>
    /// <param name="randomKey">Rastgele anahtar. Test için sabitlenebilir; üretimde <c>null</c> geçilir.</param>
    public static (string Authorization, string RandomKey) Uret(
        string apiKey, string secretKey, string uriPath, string requestBody, string? randomKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(uriPath);

        var rnd = string.IsNullOrWhiteSpace(randomKey) ? RastgeleAnahtar() : randomKey;
        var imza = Imzala(secretKey, rnd + uriPath + requestBody);
        var ham = $"apiKey:{apiKey}&randomKey:{rnd}&signature:{imza}";
        return ("IYZWSv2 " + Convert.ToBase64String(Encoding.UTF8.GetBytes(ham)), rnd);
    }

    /// <summary>HMAC-SHA256 → KÜÇÜK harfli hex. (Base64 DEĞİL — iyzico hex bekler.)</summary>
    public static string Imzala(string secretKey, string yuk)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        var ozet = hmac.ComputeHash(Encoding.UTF8.GetBytes(yuk));
        return Convert.ToHexStringLower(ozet);
    }

    /// <summary>
    /// Rastgele anahtar: milisaniye damgası + 9 haneli rastgele son ek.
    ///
    /// <para>Yalnız zaman damgası KULLANILMAZ: aynı milisaniyede iki istek (paralel çağrı) aynı
    /// anahtarı üretir ve tekrar-saldırısı korumasını zayıflatır. Rastgele son ek kriptografik
    /// üreteçten gelir.</para>
    /// </summary>
    public static string RastgeleAnahtar()
        => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)
           + RandomNumberGenerator.GetInt32(100_000_000, 999_999_999).ToString(CultureInfo.InvariantCulture);
}
