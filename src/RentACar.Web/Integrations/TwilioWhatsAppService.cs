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
        if (string.IsNullOrWhiteSpace(sid) || string.IsNullOrWhiteSpace(token)
            || string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(contentSid))
        {
            log.LogWarning("Twilio config eksik (template={T}) → WhatsApp gönderilmedi.", templateName);
            return false;
        }

        var body = new Dictionary<string, string>
        {
            ["From"] = $"whatsapp:{from}",
            ["To"] = $"whatsapp:{phone}",
            ["ContentSid"] = contentSid,
            ["ContentVariables"] = JsonSerializer.Serialize(parameters),
        };

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
}
