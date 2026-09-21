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
    private const string Uc = "/api/ui/v1/istemci-hata";

    private static string? CerezDegeri(HttpResponseMessage r, string ad)
    {
        if (!r.Headers.TryGetValues("Set-Cookie", out var degerler)) return null;
        foreach (var d in degerler)
            if (d.StartsWith(ad + "=", StringComparison.Ordinal))
                return Uri.UnescapeDataString(d[(ad.Length + 1)..].Split(';')[0]);
        return null;
    }

    /// <summary>Girişli istemci + girişten SONRAKİ XSRF belirteci. Kimlik fixture'da çalışma anında üretilir.</summary>
    private static async Task<(HttpClient C, string Xsrf)> GirisYap(WebFactory f, TestKimlik k)
    {
        var c = f.Istemci();
        var once = CerezDegeri(await c.GetAsync("/api/ui/v1/oturum/xsrf"), "XSRF-TOKEN")!;
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/ui/v1/oturum/giris")
        {
            Content = JsonContent.Create(new { firma = k.Firma, kullanici = k.Kullanici, sifre = k.Sifre }),
        };
        req.Headers.Add("X-XSRF-TOKEN", once);
        var r = await c.SendAsync(req);
        Assert.True(r.StatusCode == HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (c, CerezDegeri(r, "XSRF-TOKEN")!);
    }

    private static HttpRequestMessage Rapor(string? xsrf, HttpContent govde)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, Uc) { Content = govde };
        if (xsrf is not null) req.Headers.Add("X-XSRF-TOKEN", xsrf);
        return req;
    }

    private static HttpContent Json(object o) => JsonContent.Create(o);

    private static async Task Durum(HttpResponseMessage r, HttpStatusCode beklenen, string? kod)
    {
        var metin = await r.Content.ReadAsStringAsync();
        Assert.True(beklenen == r.StatusCode, $"Beklenen {(int)beklenen}, gelen {(int)r.StatusCode}: {metin}");
        Assert.Null(r.Headers.Location);
        if (beklenen == HttpStatusCode.NoContent) return;
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        var j = JsonDocument.Parse(metin).RootElement;
        if (kod is null) Assert.False(j.TryGetProperty("kod", out _));
        else Assert.Equal(kod, j.GetProperty("kod").GetString());
    }

    /// <summary>Log dosyalarında işaretçiyi içeren olayı bekler (Serilog her olayda diske yazar).</summary>
    private async Task<JsonElement?> LogOlayi(string isaret, TimeSpan? sure = null)
    {
        var bitis = DateTime.UtcNow + (sure ?? TimeSpan.FromSeconds(3));
        do
        {
            foreach (var dosya in Directory.GetFiles(fx.LogDizini, "log-web-*.log"))
            {
                await using var akis = new FileStream(dosya, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var okuyucu = new StreamReader(akis, Encoding.UTF8);
                while (await okuyucu.ReadLineAsync() is { } satir)
                    if (satir.Contains(isaret, StringComparison.Ordinal))
                        return JsonDocument.Parse(satir).RootElement.Clone();
            }
            await Task.Delay(100);
        } while (DateTime.UtcNow < bitis);
        return null;
    }

    [Fact]
    public async Task Oturumsuz_401_oturum_yok()
    {
        var r = await fx.Web.Istemci().SendAsync(Rapor(null, Json(new { mesaj = "x", url = "/app/", surum = "t" })));
        await Durum(r, HttpStatusCode.Unauthorized, "oturum_yok");
    }

    [Fact]
    public async Task Oturumlu_rapor_204_ve_firma_kullaniciyla_WARNING_loglanir()
    {
        var (c, xsrf) = await GirisYap(fx.Web, fx.PilotAdmin);
        var isaret = "RAPOR-" + Guid.NewGuid().ToString("N");
        var r = await c.SendAsync(Rapor(xsrf, Json(new
        {
            mesaj = isaret + " TypeError: x\r\nSAHTE [ERR] satiri",
            yigin = "TypeError: x\n  at a (chunk-1.js:1:1)",
            url = "/app/kiralar/5",
            surum = "main-ABCD1234.js",
        })));
        await Durum(r, HttpStatusCode.NoContent, null);

        var olay = await LogOlayi(isaret);
        Assert.True(olay is not null, "Log olayı bulunamadı");
        var o = olay.Value;
        Assert.Equal("Warning", o.GetProperty("@l").GetString());
        Assert.Equal(fx.PilotFirmaId.ToString(), o.GetProperty("TenantId").GetString());
        Assert.False(string.IsNullOrEmpty(o.GetProperty("UserId").GetString()));
        Assert.Equal(fx.PilotAdmin.Kullanici, o.GetProperty("KullaniciAdi").GetString());
        Assert.Equal("/app/kiralar/5", o.GetProperty("Url").GetString());
        Assert.Equal("main-ABCD1234.js", o.GetProperty("Surum").GetString());
        var mesaj = o.GetProperty("Mesaj").GetString()!;
        Assert.DoesNotContain('\r', mesaj); // sahte log satırı üretilemez
        Assert.DoesNotContain('\n', mesaj);
        Assert.Contains("at a (chunk-1.js:1:1)", o.GetProperty("Yigin").GetString());
    }

    [Fact]
    public async Task Pilot_olmayan_firmanin_kullanicisi_da_raporlayabilir()
    {
        var (c, xsrf) = await GirisYap(fx.Web, fx.DigerAdmin);
        var r = await c.SendAsync(Rapor(xsrf, Json(new { mesaj = "pilot dışı hata", url = "/app/", surum = "t" })));
        await Durum(r, HttpStatusCode.NoContent, null);
    }

    [Fact]
    public async Task Csrf_basligi_yoksa_400_xsrf_gecersiz()
    {
        var (c, _) = await GirisYap(fx.Web, fx.PilotAdmin);
        var r = await c.SendAsync(Rapor(null, Json(new { mesaj = "x", url = "/app/", surum = "t" })));
        await Durum(r, HttpStatusCode.BadRequest, "xsrf_gecersiz");
    }

    [Fact]
    public async Task Dort_KB_ustu_413_ve_loglanmaz()
    {
        var (c, xsrf) = await GirisYap(fx.Web, fx.PilotAdmin);
        var isaret = "BUYUK-" + Guid.NewGuid().ToString("N");
        var govde = new StringContent(
            JsonSerializer.Serialize(new { mesaj = isaret, yigin = new string('y', 5000), url = "/app/", surum = "t" }),
            Encoding.UTF8, "application/json");
        var r = await c.SendAsync(Rapor(xsrf, govde));
        await Durum(r, HttpStatusCode.RequestEntityTooLarge, null);
        Assert.Null(await LogOlayi(isaret, TimeSpan.FromMilliseconds(500)));
    }

    [Fact]
    public async Task Bos_mesaj_ve_bozuk_json_400_dogrulama()
    {
        var (c, xsrf) = await GirisYap(fx.Web, fx.PilotAdmin);
        var bos = await c.SendAsync(Rapor(xsrf, Json(new { mesaj = " ", url = "/app/", surum = "t" })));
        await Durum(bos, HttpStatusCode.BadRequest, "dogrulama");
        var j = JsonDocument.Parse(await bos.Content.ReadAsStringAsync()).RootElement;
        Assert.True(j.GetProperty("errors").TryGetProperty("mesaj", out _));

        var bozuk = await c.SendAsync(Rapor(xsrf, new StringContent("{bozuk", Encoding.UTF8, "application/json")));
        await Durum(bozuk, HttpStatusCode.BadRequest, "dogrulama");
    }

    [Fact]
    public async Task Hiz_siniri_429_cok_istek_json()
    {
        var c = fx.DarLimitli.Istemci(); // istemci-hata limiti 2 (limiter kimlikten ÖNCE: anonim de sayılır)
        var govde = new { mesaj = "x", url = "/app/", surum = "t" };
        await c.SendAsync(Rapor(null, Json(govde)));
        await c.SendAsync(Rapor(null, Json(govde)));
        await Durum(await c.SendAsync(Rapor(null, Json(govde))), HttpStatusCode.TooManyRequests, "cok_istek");
    }
}
