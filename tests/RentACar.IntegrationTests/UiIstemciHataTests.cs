using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// F3.3 — <c>POST /api/ui/v1/istemci-hata</c> GERÇEK Web boru hattında: yalnız oturum, CSRF, pilot muafiyeti,
/// 4 KB gövde sınırı, alan doğrulaması, IP başına hız sınırı ve WARNING log (firma/kullanıcıyla, kontrol
/// karakteri temizlenmiş). BAĞIMSIZ ORACLE: beklenen durum/kod değerleri elle yazılmış sabitlerdir.
/// </summary>
[Collection("web")]
public sealed class UiIstemciHataTests(WebFixture fx)
{
    private const string Endpoint = "/api/ui/v1/istemci-hata";

    private static string? CookieValue(HttpResponseMessage r, string name)
    {
        if (!r.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var d in values)
            if (d.StartsWith(name + "=", StringComparison.Ordinal))
                return Uri.UnescapeDataString(d[(name.Length + 1)..].Split(';')[0]);
        return null;
    }

    /// <summary>Girişli istemci + girişten SONRAKİ XSRF belirteci. Kimlik fixture'da çalışma anında üretilir.</summary>
    private static async Task<(HttpClient C, string Xsrf)> Login(WebFactory f, TestKimlik k)
    {
        var c = f.Client();
        var once = CookieValue(await c.GetAsync("/api/ui/v1/oturum/xsrf"), "XSRF-TOKEN")!;
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/ui/v1/oturum/giris")
        {
            Content = JsonContent.Create(new { firma = k.Firma, kullanici = k.Kullanici, sifre = k.Sifre }),
        };
        req.Headers.Add("X-XSRF-TOKEN", once);
        var r = await c.SendAsync(req);
        Assert.True(r.StatusCode == HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (c, CookieValue(r, "XSRF-TOKEN")!);
    }

    private static HttpRequestMessage Report(string? xsrf, HttpContent body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = body };
        if (xsrf is not null) req.Headers.Add("X-XSRF-TOKEN", xsrf);
        return req;
    }

    private static HttpContent Json(object o) => JsonContent.Create(o);

    private static async Task Status(HttpResponseMessage r, HttpStatusCode expected, string? code)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(expected == r.StatusCode, $"Beklenen {(int)expected}, gelen {(int)r.StatusCode}: {text}");
        Assert.Null(r.Headers.Location);
        if (expected == HttpStatusCode.NoContent) return;
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        var j = JsonDocument.Parse(text).RootElement;
        if (code is null) Assert.False(j.TryGetProperty("kod", out _));
        else Assert.Equal(code, j.GetProperty("kod").GetString());
    }

    /// <summary>Log dosyalarında işaretçiyi içeren olayı bekler (Serilog her olayda diske yazar).</summary>
    private async Task<JsonElement?> LogEvent(string sign, TimeSpan? duration = null)
    {
        var end = DateTime.UtcNow + (duration ?? TimeSpan.FromSeconds(3));
        do
        {
            foreach (var file in Directory.GetFiles(fx.LogDirectory, "log-web-*.log"))
            {
                await using var flow = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(flow, Encoding.UTF8);
                while (await reader.ReadLineAsync() is { } row)
                    if (row.Contains(sign, StringComparison.Ordinal))
                        return JsonDocument.Parse(row).RootElement.Clone();
            }
            await Task.Delay(100);
        } while (DateTime.UtcNow < end);
        return null;
    }

    [Fact]
    public async Task Oturumsuz_401_oturum_yok()
    {
        var r = await fx.Web.Client().SendAsync(Report(null, Json(new { mesaj = "x", url = "/app/", surum = "t" })));
        await Status(r, HttpStatusCode.Unauthorized, "oturum_yok");
    }

    [Fact]
    public async Task Oturumlu_rapor_204_ve_firma_kullaniciyla_WARNING_loglanir()
    {
        var (c, xsrf) = await Login(fx.Web, fx.PilotAdmin);
        var sign = "RAPOR-" + Guid.NewGuid().ToString("N");
        var r = await c.SendAsync(Report(xsrf, Json(new
        {
            mesaj = sign + " TypeError: x\r\nSAHTE [ERR] satiri",
            yigin = "TypeError: x\n  at a (chunk-1.js:1:1)",
            url = "/app/kiralar/5",
            surum = "main-ABCD1234.js",
        })));
        await Status(r, HttpStatusCode.NoContent, null);

        var evt = await LogEvent(sign);
        Assert.True(evt is not null, "Log olayı bulunamadı");
        var o = evt.Value;
        Assert.Equal("Warning", o.GetProperty("@l").GetString());
        Assert.Equal(fx.PilotCompanyId.ToString(), o.GetProperty("TenantId").GetString());
        Assert.False(string.IsNullOrEmpty(o.GetProperty("UserId").GetString()));
        Assert.Equal(fx.PilotAdmin.Kullanici, o.GetProperty("KullaniciAdi").GetString());
        Assert.Equal("/app/kiralar/5", o.GetProperty("Url").GetString());
        Assert.Equal("main-ABCD1234.js", o.GetProperty("Surum").GetString());
        var message = o.GetProperty("Mesaj").GetString()!;
        Assert.DoesNotContain('\r', message); // sahte log satırı üretilemez
        Assert.DoesNotContain('\n', message);
        Assert.Contains("at a (chunk-1.js:1:1)", o.GetProperty("Yigin").GetString());
    }

    [Fact]
    public async Task Pilot_olmayan_firmanin_kullanicisi_da_raporlayabilir()
    {
        var (c, xsrf) = await Login(fx.Web, fx.OtherAdmin);
        var r = await c.SendAsync(Report(xsrf, Json(new { mesaj = "pilot dışı hata", url = "/app/", surum = "t" })));
        await Status(r, HttpStatusCode.NoContent, null);
    }

    [Fact]
    public async Task Csrf_basligi_yoksa_400_xsrf_gecersiz()
    {
        var (c, _) = await Login(fx.Web, fx.PilotAdmin);
        var r = await c.SendAsync(Report(null, Json(new { mesaj = "x", url = "/app/", surum = "t" })));
        await Status(r, HttpStatusCode.BadRequest, "xsrf_gecersiz");
    }

    [Fact]
    public async Task Dort_KB_ustu_413_ve_loglanmaz()
    {
        var (c, xsrf) = await Login(fx.Web, fx.PilotAdmin);
        var sign = "BUYUK-" + Guid.NewGuid().ToString("N");
        var body = new StringContent(
            JsonSerializer.Serialize(new { mesaj = sign, yigin = new string('y', 5000), url = "/app/", surum = "t" }),
            Encoding.UTF8, "application/json");
        var r = await c.SendAsync(Report(xsrf, body));
        await Status(r, HttpStatusCode.RequestEntityTooLarge, null);
        Assert.Null(await LogEvent(sign, TimeSpan.FromMilliseconds(500)));
    }

    [Fact]
    public async Task Bos_mesaj_ve_bozuk_json_400_dogrulama()
    {
        var (c, xsrf) = await Login(fx.Web, fx.PilotAdmin);
        var empty = await c.SendAsync(Report(xsrf, Json(new { mesaj = " ", url = "/app/", surum = "t" })));
        await Status(empty, HttpStatusCode.BadRequest, "dogrulama");
        var j = JsonDocument.Parse(await empty.Content.ReadAsStringAsync()).RootElement;
        Assert.True(j.GetProperty("errors").TryGetProperty("mesaj", out _));

        var corrupt = await c.SendAsync(Report(xsrf, new StringContent("{bozuk", Encoding.UTF8, "application/json")));
        await Status(corrupt, HttpStatusCode.BadRequest, "dogrulama");
    }

    [Fact]
    public async Task Hiz_siniri_429_cok_istek_json()
    {
        var c = fx.NarrowLimited.Client(); // istemci-hata limiti 2 (limiter kimlikten ÖNCE: anonim de sayılır)
        var body = new { mesaj = "x", url = "/app/", surum = "t" };
        await c.SendAsync(Report(null, Json(body)));
        await c.SendAsync(Report(null, Json(body)));
        await Status(await c.SendAsync(Report(null, Json(body))), HttpStatusCode.TooManyRequests, "cok_istek");
    }
}
