using System.Text.Json;
using System.Text.Json.Nodes;

namespace RentACar.Application.Auditing;

/// <summary>
/// Denetim izindeki sır ve KVKK anahtarları için TEK kural. Hem yazma yolu (<c>AuditSaveChangesInterceptor</c>: değer
/// AuditLogs'a hiç girmez) hem okuma yüzeyleri (<c>/api/ui/v1/denetim</c>, Blazor <c>AuditList.razor</c>: eski kayıtlarda
/// kalan değerler gösterilmez) bu sınıfı kullanır. Önceden interceptor yalnız sabit bir PII listesini maskeliyordu;
/// <c>SmtpSifreEnc</c>, <c>PasswordHash</c>, <c>CalendarToken</c> gibi cipher/hash/token değerleri AuditLogs'a düz
/// yazılıyordu (2026-09-25 incelemesi, #304 L2).
/// </summary>
public static class AuditSecretMask
{
    public const string Masked = "***";

    private static readonly string[] SecretKeySuffixes = ["Enc", "Hash", "Token"];
    private static readonly string[] SecretKeyParts = ["Sifre", "Password", "ApiKey", "Secret", "Parola"];

    // "Vkn": GelenEFatura.GonderenVkn şahıs firmasında TCKN taşır. "Tc" tek başına BİLİNÇLİ yok (çok geniş eşleşir).
    private static readonly string[] PiiKeyParts =
        ["TcKimlik", "TcNo", "Tckn", "KimlikNo", "VergiNo", "Vkn", "EhliyetNo", "PasaportNo", "Maas", "Iban",
         "SurucuBelgeNo"];

    // Nüfus cüzdanı alanları (Customer, Personel). Kısa ve genel adlar olduğu için parça değil TAM anahtar eşleşir;
    // "SiraNo" parça olsaydı taksit/sıra alanlarını da maskelerdi (2026-09-25 #319 incelemesi L1).
    private static readonly string[] PiiExactKeys = ["SeriNo", "CiltNo", "AileSira", "AileSiraNo", "SiraNo"];

    /// <summary>Sır ya da KVKK kapsamındaki kişisel veri anahtarı mı (büyük/küçük harf duyarsız).</summary>
    public static bool IsSecretKey(string key)
        => SecretKeySuffixes.Any(s => key.EndsWith(s, StringComparison.OrdinalIgnoreCase))
           || SecretKeyParts.Any(p => key.Contains(p, StringComparison.OrdinalIgnoreCase))
           || PiiKeyParts.Any(p => key.Contains(p, StringComparison.OrdinalIgnoreCase))
           || PiiExactKeys.Any(k => key.Equals(k, StringComparison.OrdinalIgnoreCase));

    /// <summary>Denetim değer JSON'u → sır/PII anahtarları (iç içe nesne ve dizilerde de) maskeli JSON. Nesne değilse ya da
    /// ayrıştırılamıyorsa <c>null</c>.</summary>
    public static string? MaskJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject obj) return null;
            MaskNode(obj);
            return obj.ToJsonString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void MaskNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var key in o.Select(kv => kv.Key).ToList())
                {
                    if (IsSecretKey(key) && o[key] is not null) o[key] = Masked;
                    else if (MaskEmbeddedJson(o[key]) is { } masked) o[key] = masked;
                    else MaskNode(o[key]);
                }
                break;
            case JsonArray a:
                for (var i = 0; i < a.Count; i++)
                {
                    if (MaskEmbeddedJson(a[i]) is { } masked) a[i] = masked;
                    else MaskNode(a[i]);
                }
                break;
        }
    }

    /// <summary>Metin değeri içine gömülü JSON nesnesi/dizisi — ayrıştırılıp maskelenir ve yine metin olarak döner;
    /// JSON değilse <c>null</c>.</summary>
    private static string? MaskEmbeddedJson(JsonNode? node)
    {
        if (node is not JsonValue v || !v.TryGetValue<string>(out var s)) return null;
        var t = s.TrimStart();
        if (t.Length == 0 || (t[0] != '{' && t[0] != '[')) return null;
        try
        {
            var inner = JsonNode.Parse(s);
            if (inner is null) return null;
            MaskNode(inner);
            return inner.ToJsonString();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
