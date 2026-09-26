using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RentACar.Web.Integrations;

namespace RentACar.IntegrationTests;

/// <summary>
/// Twilio WhatsApp göndericisi — şablon yolu vs SERBEST METİN yolu. DB gerektirmez; Twilio'ya
/// gerçek istek ATILMAZ (sahte <see cref="HttpMessageHandler"/> isteği yakalar ve gövdesini sınar).
///
/// <para><b>Neden serbest metin yolu var:</b> WhatsApp SANDBOX'ı özel şablon kabul etmiyor
/// (Twilio dokümanı: "You can't use custom message templates with the Sandbox"). Uygulamanın iki
/// şablonu (<c>operasyon_ozet</c>, <c>ops_alert</c>) sandbox'ın 3 hazır şablonundan biri değil —
/// bu yol olmasa entegrasyon, WhatsApp Business onayı çıkana kadar hiç denenemezdi.</para>
///
/// <para><b>Kilitlenen asıl kural:</b> serbest metin AÇIK BAYRAKLA gelir. Bayrak kapalıyken davranış
/// eskisiyle BİREBİR aynıdır (şablon yoksa hiç istek atma) — çünkü üretimde şablon SID'i unutulup
/// sessizce serbest metne düşmek, iş-başlatımlı mesajın 24 saat penceresi dışında WhatsApp
/// tarafından reddedilmesi demektir: net bir config hatası yerine sessiz bir gönderim hatası.</para>
/// </summary>
public sealed class TwilioWhatsAppTests
{
    /// <summary>İstek gövdesini yakalayan, ağa ÇIKMAYAN handler.</summary>
    private sealed class YakalayiciHandler(HttpStatusCode kod = HttpStatusCode.Created) : HttpMessageHandler
    {
        public HttpRequestMessage? Istek { get; private set; }
        public Dictionary<string, string> Govde { get; private set; } = [];
        public int Cagri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Cagri++;
            Istek = request;
            var raw = await request.Content!.ReadAsStringAsync(ct);
            Govde = raw.Split('&')
                .Select(p => p.Split('=', 2))
                .ToDictionary(p => Uri.UnescapeDataString(p[0]),
                              p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));
            return new HttpResponseMessage(kod) { Content = new StringContent("{}") };
        }
    }

    private sealed class TekHandlerFactory(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(h, disposeHandler: false);
    }

    private static TwilioWhatsAppService Service(YakalayiciHandler h, params (string K, string V)[] settings)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(
            settings.ToDictionary(a => a.K, a => (string?)a.V)).Build();
        return new TwilioWhatsAppService(new TekHandlerFactory(h), cfg,
            NullLogger<TwilioWhatsAppService>.Instance);
    }

    private static readonly (string, string)[] Base =
    [
        ("Twilio:AccountSid", "ACtest"),
        ("Twilio:AuthToken", "token"),
        ("Twilio:WhatsAppFrom", "+14155238886"),
    ];

    // ---------------------------------------------------------------- şablon yolu (üretim)

    [Fact]
    public async Task Sablon_tanimliysa_ContentSid_ile_gonderilir()
    {
        var h = new YakalayiciHandler();
        var svc = Service(h, [.. Base, ("Twilio:Templates:operasyon_ozet", "HX123")]);

        var ok = await svc.SendTemplateAsync("+905321112233", "operasyon_ozet",
            new Dictionary<string, string> { ["1"] = "Bugün 3 çıkış" });

        Assert.True(ok);
        Assert.Equal("HX123", h.Govde["ContentSid"]);
        // ContentVariables ham metin olarak DEĞİL, ÇÖZÜLMÜŞ hâliyle sınanır: JsonSerializer
        // Türkçe karakterleri \u00FC gibi kaçırır — bu geçerli JSON'dur ve Twilio aynı dizeye
        // geri çözer. Ham dizeye bakan bir iddia, kodu değil kaçırma biçimini test ederdi.
        var variables = System.Text.Json.JsonSerializer
            .Deserialize<Dictionary<string, string>>(h.Govde["ContentVariables"]);
        Assert.Equal(new Dictionary<string, string> { ["1"] = "Bugün 3 çıkış" }, variables);
        Assert.False(h.Govde.ContainsKey("Body"));           // şablon yolunda serbest metin YOK
        Assert.Equal("whatsapp:+14155238886", h.Govde["From"]);
        Assert.Equal("whatsapp:+905321112233", h.Govde["To"]);
    }

    /// <summary>Bayrak AÇIK olsa bile şablon tanımlıysa ŞABLON kazanır — üretim yolu bayrakla bozulmaz.</summary>
    [Fact]
    public async Task Bayrak_acikken_bile_sablon_varsa_sablon_kullanilir()
    {
        var h = new YakalayiciHandler();
        var svc = Service(h, [.. Base,
            ("Twilio:Templates:operasyon_ozet", "HX123"), ("Twilio:AllowFreeform", "true")]);

        await svc.SendTemplateAsync("+905321112233", "operasyon_ozet",
            new Dictionary<string, string> { ["1"] = "x" });

        Assert.Equal("HX123", h.Govde["ContentSid"]);
        Assert.False(h.Govde.ContainsKey("Body"));
    }

    // ---------------------------------------------------------------- serbest metin yolu (sandbox)

    [Fact]
    public async Task Sablon_yok_bayrak_acik_ise_serbest_metin_gonderilir()
    {
        var h = new YakalayiciHandler();
        var svc = Service(h, [.. Base, ("Twilio:AllowFreeform", "true")]);

        var ok = await svc.SendTemplateAsync("+905321112233", "operasyon_ozet",
            new Dictionary<string, string> { ["1"] = "Bugün 3 çıkış, 2 dönüş." });

        Assert.True(ok);
        Assert.Equal("Bugün 3 çıkış, 2 dönüş.", h.Govde["Body"]);
        Assert.False(h.Govde.ContainsKey("ContentSid"));      // sandbox özel şablon KABUL ETMEZ
        Assert.False(h.Govde.ContainsKey("ContentVariables"));
    }

    /// <summary>
    /// KRİTİK REGRESYON: bayrak yokken davranış ESKİSİYLE aynı — hiç istek atılmaz.
    /// Sessiz fallback olsaydı, üretimde eksik şablon config'i net bir uyarı yerine WhatsApp'ın
    /// reddettiği bir gönderime dönerdi.
    /// </summary>
    [Fact]
    public async Task Sablon_yok_bayrak_yok_ise_HIC_istek_atilmaz()
    {
        var h = new YakalayiciHandler();
        var svc = Service(h, Base);

        var ok = await svc.SendTemplateAsync("+905321112233", "operasyon_ozet",
            new Dictionary<string, string> { ["1"] = "x" });

        Assert.False(ok);
        Assert.Equal(0, h.Cagri);
    }

    [Fact]
    public async Task Kimlik_eksikse_bayrak_acik_olsa_bile_gonderilmez()
    {
        var h = new YakalayiciHandler();
        var svc = Service(h, ("Twilio:AccountSid", "ACtest"), ("Twilio:AllowFreeform", "true"));

        Assert.False(await svc.SendTemplateAsync("+905321112233", "ops_alert",
            new Dictionary<string, string> { ["1"] = "x" }));
        Assert.Equal(0, h.Cagri);
    }

    // ---------------------------------------------------------------- metin birleştirme

    /// <summary>
    /// Değişkenler SAYISAL sırayla birleşir. Sözlük/ordinal sırada "10" &lt; "2" olurdu ve mesaj
    /// karışık çıkardı — bağımsız oracle: elle kurulan sıra "bir iki … on".
    /// </summary>
    [Fact]
    public void Metin_degiskenleri_SAYISAL_sirayla_birlestirir()
    {
        var input = new Dictionary<string, string>
        {
            ["10"] = "on", ["2"] = "iki", ["1"] = "bir",
        };
        Assert.Equal("bir iki on", TwilioWhatsAppService.Text(input));
    }

    [Fact]
    public void Metin_bos_degerleri_atlar()
        => Assert.Equal("dolu", TwilioWhatsAppService.Text(
            new Dictionary<string, string> { ["1"] = "dolu", ["2"] = "   ", ["3"] = "" }));

    [Fact]
    public async Task Twilio_hata_donerse_false_doner_job_cokmez()
    {
        var h = new YakalayiciHandler(HttpStatusCode.BadRequest);
        var svc = Service(h, [.. Base, ("Twilio:AllowFreeform", "true")]);

        Assert.False(await svc.SendTemplateAsync("+905321112233", "operasyon_ozet",
            new Dictionary<string, string> { ["1"] = "x" }));
        Assert.Equal(1, h.Cagri);
    }
}
