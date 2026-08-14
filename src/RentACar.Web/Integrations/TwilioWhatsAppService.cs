using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using RentACar.Application.Integrations;

namespace RentACar.Web.Integrations;

/// <summary>
/// Gerçek Twilio WhatsApp gönderici (Content/Messages API). Config-gated: Twilio:AccountSid varsa Program.cs DI'da
/// StubWhatsAppService'i override eder. Proaktif mesaj → onaylı template (ContentSid) zorunlu; templateName→ContentSid
/// config map (`Twilio:Templates:{name}`), parameters→ContentVariables JSON. Basic auth. Hata/timeout(10s) → false
/// (job ÇÖKMESİN). Web'de IHttpClientFactory (Infra'ya Http bağımlılığı eklemeden — TcmbKurService deseni).
/// </summary>
public sealed class TwilioWhatsAppService(
    IHttpClientFactory httpFactory, IConfiguration config, ILogger<TwilioWhatsAppService> log) : IWhatsAppService
{
    public async Task<bool> SendTemplateAsync(
        string phone, string templateName, IReadOnlyDictionary<string, string> parameters, CancellationToken ct = default)
    {
        var sid = config["Twilio:AccountSid"];
        var token = config["Twilio:AuthToken"];
        var from = config["Twilio:WhatsAppFrom"];
        var contentSid = config[$"Twilio:Templates:{templateName}"];
        if (string.IsNullOrWhiteSpace(sid) || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(from))
        {
            log.LogWarning("Twilio config eksik (template={T}) → WhatsApp gönderilmedi.", templateName);
            return false;
        }

        // SERBEST METİN YOLU — yalnız `Twilio:AllowFreeform=true` ise ve şablon SID'i YOKSA.
        //
        // Neden gerekli: WhatsApp SANDBOX'ı özel şablon kabul etmiyor (Twilio: "You can't use custom
        // message templates with the Sandbox"). Sandbox yalnız (a) numaranın katılmasından sonraki
        // 24 saatlik pencerede serbest metni, (b) Twilio'nun 3 sabit hazır şablonunu destekler.
        // Bizim şablonlarımız (operasyon_ozet / ops_alert) ikisi de değil → bu yol olmadan entegrasyon
        // WhatsApp Business onayı çıkana kadar HİÇ test edilemezdi.
        //
        // Neden AÇIK BAYRAK (sessiz fallback DEĞİL): şablon SID'i üretimde unutulursa sessizce serbest
        // metne düşmek, iş-başlatımlı mesajın 24 saatlik pencere dışında WhatsApp tarafından
        // reddedilmesi demektir — yani net bir config uyarısı yerine sessiz bir gönderim hatası.
        // Bayrak kapalıyken davranış ESKİSİYLE BİREBİR aynıdır (şablon yoksa uyar ve gönderme).
        var freeformIzinli = config.GetValue("Twilio:AllowFreeform", false);
        if (string.IsNullOrWhiteSpace(contentSid) && !freeformIzinli)
        {
            log.LogWarning("Twilio config eksik (template={T}) → WhatsApp gönderilmedi.", templateName);
            return false;
        }

        var body = new Dictionary<string, string>
        {
            ["From"] = $"whatsapp:{from}",
            ["To"] = $"whatsapp:{phone}",
        };
        if (!string.IsNullOrWhiteSpace(contentSid))
        {
            body["ContentSid"] = contentSid;
            body["ContentVariables"] = JsonSerializer.Serialize(parameters);
        }
        else
        {
            body["Body"] = Metin(parameters);
            log.LogInformation("Twilio serbest metin yolu (template={T}) — şablon SID'i yok, AllowFreeform açık.", templateName);
        }

        try
        {
            var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(10); // O2: açık-bağlantı penceresini sınırla
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"https://api.twilio.com/2010-04-01/Accounts/{sid}/Messages.json")
            {
                Content = new FormUrlEncodedContent(body),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{sid}:{token}")));
            var resp = await http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode) return true; // 201 Created
            var err = await resp.Content.ReadAsStringAsync(ct);
            log.LogWarning("Twilio WhatsApp başarısız {Status}: {Err}", (int)resp.StatusCode, err);
            return false;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Twilio WhatsApp gönderim hatası.");
            return false;
        }
    }

    /// <summary>
    /// Şablon değişkenlerini serbest-metin gövdesine çevirir. Şablon yolunda değişkenler
    /// <c>ContentVariables</c> JSON'u olarak ("1", "2", …) gider; serbest metinde şablon metni
    /// OLMADIĞI için elimizde yalnız bu değerler vardır, sırayla birleştirilir.
    ///
    /// <para>Sıralama SAYISAL: <c>{"10":…,"2":…}</c> sözlük sırasında "10" &lt; "2" olurdu ve
    /// mesaj karışık çıkardı. Sayıya çevrilemeyen anahtarlar (olmamalı) sona, kendi aralarında
    /// ordinal sırayla eklenir — belirsiz sıra bırakmamak için.</para>
    /// </summary>
    /// <remarks>SAF ve <c>public</c>: doğrudan test edilebilsin diye (repoda <c>InternalsVisibleTo</c>
    /// deseni yok; <c>AracImza</c>/<c>GelenEFaturaKdvKirilim</c> gibi saf yardımcılar da public).</remarks>
    public static string Metin(IReadOnlyDictionary<string, string> parameters)
        => string.Join(' ', parameters
            .OrderBy(p => int.TryParse(p.Key, out var n) ? n : int.MaxValue)
            .ThenBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => p.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v)));
}
