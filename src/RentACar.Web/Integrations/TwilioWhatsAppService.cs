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
        => (await GonderAsync(phone, templateName, parameters, ct)).Ok;

    /// <summary>
    /// Gönderim + oluşan mesajın SID'i. Arayüz (<see cref="IWhatsAppService"/>) yalnız bool döndürdüğü
    /// için ayrı metot: SID'e SADECE teşhis yolu (Ayarlar test butonu) ihtiyaç duyar, job'lar duymaz.
    /// Servis SINGLETON kayıtlı — "son gönderilen SID"i alan olarak tutmak eşzamanlı çağrılarda
    /// yanlış mesajı raporlardı; bu yüzden SID dönüş değeriyle taşınır.
    /// </summary>
    public async Task<(bool Ok, string? Sid)> GonderAsync(
        string phone, string templateName, IReadOnlyDictionary<string, string> parameters, CancellationToken ct = default)
    {
        var sid = config["Twilio:AccountSid"];
        var token = config["Twilio:AuthToken"];
        var from = config["Twilio:WhatsAppFrom"];
        var contentSid = config[$"Twilio:Templates:{templateName}"];
        if (string.IsNullOrWhiteSpace(sid) || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(from))
        {
            log.LogWarning("Twilio config eksik (template={T}) → WhatsApp gönderilmedi.", templateName);
            return (false, null);
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
            return (false, null);
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
            if (resp.IsSuccessStatusCode)
            {
                string? olusanSid = null;
                // 201 Created = Twilio mesajı KABUL ETTİ, TESLİM ETTİ demek DEĞİL. Teslim hatası
                // (sandbox'a katılmamış alıcı 63015, 24 saat penceresi 63016, engellenmiş numara
                // 63021) saniyeler içinde mesajın DURUMUNA düşer, HTTP yanıtına değil.
                // Canlı denemede birebir yaşandı: 201 alındı, mesaj `status=failed, error 63015`
                // oldu, ekranda "gönderildi" yazıyordu.
                //
                // SID + ilk durum loglanır ki başarısızlık sessiz kalmasın ve teşhis edilebilsin.
                // Kesin teslim doğrulaması için SonDurumAsync kullanılır (Ayarlar'daki test butonu).
                try
                {
                    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                    var kok = doc.RootElement;
                    var msid = kok.TryGetProperty("sid", out var s2) ? s2.GetString() : null;
                    olusanSid = msid;
                    var durum = kok.TryGetProperty("status", out var d2) ? d2.GetString() : null;
                    var hata = kok.TryGetProperty("error_code", out var e2) && e2.ValueKind != JsonValueKind.Null
                        ? e2.ToString() : null;

                    if (durum is "failed" or "undelivered" || hata is not null)
                    {
                        log.LogWarning("Twilio mesajı oluşturuldu ama BAŞARISIZ (sid={Sid} durum={Durum} hata={Hata}).",
                            msid, durum, hata);
                        return (false, olusanSid);
                    }
                    log.LogInformation("Twilio mesajı kuyruğa alındı (sid={Sid} durum={Durum}).", msid, durum);
                }
                catch (JsonException) { /* gövde okunamadı — kabul yanıtını geçerli say */ }
                return (true, olusanSid);
            }
            var err = await resp.Content.ReadAsStringAsync(ct);
            log.LogWarning("Twilio WhatsApp başarısız {Status}: {Err}", (int)resp.StatusCode, err);
            return (false, null);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Twilio WhatsApp gönderim hatası.");
            return (false, null);
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
    /// <summary>
    /// Bir numaraya giden SON mesajın gerçek teslim durumunu Twilio'dan okur.
    ///
    /// <para><b>Neden gerekli:</b> gönderim ucu 201 Created alır almaz "kabul edildi" der; WhatsApp
    /// teslim hatası (63015 katılmamış alıcı, 63016 pencere kapalı, 63021 engellenmiş) saniyeler
    /// sonra mesajın DURUMUNA düşer. "Yapılandırmam çalışıyor mu?" sorusunun dürüst cevabı ancak
    /// bu okumayla verilebilir — Ayarlar'daki test butonu bunu kullanır.</para>
    ///
    /// <para>Yalnız TEŞHİS amaçlıdır; arka plan job'ları çağırmaz (her mesajdan sonra ek istek
    /// atmak gereksiz yük olurdu, onlar için log yeterli).</para>
    /// </summary>
    /// <returns>(durum, hataKodu) — okunamazsa (null, null).</returns>
    public async Task<(string? Durum, string? HataKodu)> SonDurumAsync(
        string mesajSid, CancellationToken ct = default)
    {
        var sid = config["Twilio:AccountSid"];
        var token = config["Twilio:AuthToken"];
        if (string.IsNullOrWhiteSpace(sid) || string.IsNullOrWhiteSpace(token)
            || string.IsNullOrWhiteSpace(mesajSid)) return (null, null);

        try
        {
            var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(10);
            // TEKİL okuma (SID ile) — liste ucu DEĞİL. Canlı denemede yakalandı: `?To=…` filtreli
            // liste yeni oluşan mesajı 9 saniye boyunca DÖNDÜRMEDİ (Twilio liste ucu eventually
            // consistent), yoklama hep boş liste görüp "durum belli değil" diyordu. Aynı mesaj
            // SID ile sorulunca ANINDA geliyor. Ayrıca liste yolu yarış da barındırıyordu:
            // "numaraya giden son mesaj" bizim az önce gönderdiğimiz olmayabilir.
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.twilio.com/2010-04-01/Accounts/{sid}/Messages/{mesajSid}.json");
            req.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{sid}:{token}")));
            var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                log.LogWarning("Twilio durum sorgusu HTTP {Kod} döndü.", (int)resp.StatusCode);
                return (null, null);
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var m = doc.RootElement;
            var durum = m.TryGetProperty("status", out var d) ? d.GetString() : null;
            var hata = m.TryGetProperty("error_code", out var e) && e.ValueKind != JsonValueKind.Null
                ? e.ToString() : null;
            return (durum, hata);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Twilio durum sorgusu başarısız.");
            return (null, null);
        }
    }

    /// <summary>Twilio hata kodunu operatörün anlayacağı cümleye çevirir (teşhis).</summary>
    public static string HataAciklama(string? kod) => kod switch
    {
        "63015" => "Alıcı numara WhatsApp sandbox'a katılmamış. Telefondan sandbox numarasına "
                 + "'join <kod>' mesajı gönderin (kod Twilio konsolunda yazıyor).",
        "63016" => "24 saatlik müşteri penceresi kapalı. Serbest metin ancak alıcı size yazdıktan "
                 + "sonraki 24 saat içinde gönderilebilir; dışında onaylı şablon gerekir.",
        "63021" => "Alıcı numara gönderimi engellemiş.",
        "21211" or "21212" => "Numara biçimi geçersiz (E.164 olmalı, ör. +905321112233).",
        "20003" => "Twilio kimliği geçersiz (Account SID / Auth Token).",
        null => "",
        _ => $"Twilio hata kodu {kod}.",
    };

    /// <remarks>SAF ve <c>public</c>: doğrudan test edilebilsin diye (repoda <c>InternalsVisibleTo</c>
    /// deseni yok; <c>AracImza</c>/<c>GelenEFaturaKdvKirilim</c> gibi saf yardımcılar da public).</remarks>
    public static string Metin(IReadOnlyDictionary<string, string> parameters)
        => string.Join(' ', parameters
            .OrderBy(p => int.TryParse(p.Key, out var n) ? n : int.MaxValue)
            .ThenBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => p.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v)));
}
