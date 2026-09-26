using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RentACar.Web.Observability;

/// <summary>
/// Grafana Alerting → uygulama köprüsü (/internal/alert). Alarmı KENDİ loglarımıza (Warning) yazar →
/// merkezî kayıt (Loki/dosya/console) + istersen best-effort WhatsApp forward. GİZLİ-ANAHTAR kapılı
/// (makine POST'u, cookie yok); anahtar config'te yoksa uç DEVRE DIŞI (açık uç bırakma). Grafana ayrıca
/// kendi contact-point'leriyle (e-posta/Slack) push yapabilir — bu köprü durable log + WhatsApp içindir.
/// </summary>
public static class AlertWebhook
{
    /// <summary>Anahtarı istekten çıkarır: <c>X-Alert-Token</c> header'ı (curl/test) VEYA
    /// <c>Authorization: Bearer &lt;token&gt;</c> (Grafana webhook contact-point'i keyfi header desteklemez,
    /// Bearer destekler). İlki dolu değilse Bearer'a bakar.</summary>
    public static string? ExtractToken(string? xAlertToken, string? authorization)
    {
        if (!string.IsNullOrEmpty(xAlertToken)) return xAlertToken;
        const string p = "Bearer ";
        if (!string.IsNullOrEmpty(authorization) && authorization.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            return authorization[p.Length..].Trim();
        return null;
    }

    /// <summary>Anahtar doğrulama: config'te anahtar tanımlı OLMALI ve gelen header birebir eşleşmeli
    /// (sabit-zamanlı karşılaştırma — timing sızıntısı yok). Config boşsa yetki YOK (uç kapalı).</summary>
    public static bool Authorized(string? provided, string? configured)
    {
        if (string.IsNullOrEmpty(configured) || string.IsNullOrEmpty(provided)) return false;
        var a = Encoding.UTF8.GetBytes(provided);
        var b = Encoding.UTF8.GetBytes(configured);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>Grafana alarm gövdesinden kısa özet çıkarır (best-effort): title/message ya da
    /// alerts[].labels.alertname + annotations.summary; ayrıştırılamazsa ham metin (kırpılmış).</summary>
    public static string Summarize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "(boş alarm gövdesi)";
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
            {
                var t = title.GetString() ?? "";
                if (root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                    return Clamp($"{t} — {msg.GetString()}");
                return Clamp(t);
            }
            if (root.TryGetProperty("alerts", out var alerts) && alerts.ValueKind == JsonValueKind.Array && alerts.GetArrayLength() > 0)
            {
                var first = alerts[0];
                var name = first.TryGetProperty("labels", out var lbl) && lbl.TryGetProperty("alertname", out var an)
                    ? an.GetString() : "alarm";
                var summary = first.TryGetProperty("annotations", out var ann) && ann.TryGetProperty("summary", out var sm)
                    ? sm.GetString() : "";
                var status = first.TryGetProperty("status", out var st) ? st.GetString() : "";
                return Clamp($"[{status}] {name}: {summary}");
            }
        }
        catch (JsonException) { /* ham'a düş */ }
        return Clamp(json);
    }

    private static string Clamp(string s) => s.Length <= 400 ? s : s[..400] + "…";
}
