using System.Text.Json;
using System.Text.Json.Nodes;

namespace RentACar.PublicSite;

/// <summary>
/// JSON-LD serileştirici. İki kural TEK yerde:
/// <list type="bullet">
/// <item>Değeri null olan alan HİÇ yazılmaz (T2: veri yoksa alan yok). <c>JsonIgnoreCondition.WhenWritingNull</c>
/// sözlük girdilerine UYGULANMAZ — bileşenler sözlük kurduğu için eskiden `"url":null` gibi alanlar basılıyordu
/// (test yakaladı). Budama iç içe nesne ve dizilerde de yapılır.</item>
/// <item>Varsayılan kodlayıcı korunur: <c>&lt;</c> <c>&gt;</c> <c>&amp;</c> kaçırılır → tenant verisi
/// <c>&lt;/script&gt;</c> üretemez (MarkupString ile basmanın güvenlik gerekçesi).</item>
/// </list>
/// </summary>
public static class JsonLd
{
    public static string Serialize(object value)
    {
        var node = JsonSerializer.SerializeToNode(value);
        Prune(node);
        return node?.ToJsonString() ?? "null";
    }

    private static void Prune(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Where(kv => kv.Value is null).Select(kv => kv.Key).ToList()) obj.Remove(key);
                foreach (var kv in obj) Prune(kv.Value);
                break;
            case JsonArray arr:
                foreach (var item in arr) Prune(item);
                break;
        }
    }
}
