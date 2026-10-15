using System.Net;
using static RentACar.IntegrationTests.SystemApiTestKit;

namespace RentACar.IntegrationTests;

public sealed partial class UiSystemSecurityTests
{
    // Halka açık site vurgu rengi: /ayarlar tam PUT'unda taşınır; biçim sunucuda doğrulanır (alan eşlemeli hata),
    // büyük harf küçüğe normalize edilir, başka firmaya sızmaz.
    [Fact]
    public async Task Site_accent_color_round_trips_validates_and_stays_in_tenant()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var version = (await Json(await admin.C.GetAsync(Settings))).GetProperty("surum").GetString();

        var saved = await Json(await Send(admin, HttpMethod.Put, Settings, new { siteVurguRengi = "#C0392B", surum = version }));
        Assert.Equal("#c0392b", saved.GetProperty("siteVurguRengi").GetString());
        version = saved.GetProperty("surum").GetString();

        // Biçimsiz değer (CSS'e serbest metin sızdırma denemesi dahil) alan eşlemeli 400; kayıt değişmez.
        foreach (var bad in new[] { "kirmizi", "#12345", "red;}" })
            await Problem(await Send(admin, HttpMethod.Put, Settings, new { siteVurguRengi = bad, surum = version }),
                HttpStatusCode.BadRequest, "dogrulama", "siteVurguRengi");
        Assert.Equal("#c0392b", (await Json(await admin.C.GetAsync(Settings))).GetProperty("siteVurguRengi").GetString());

        // Başka firma bu rengi görmez.
        var other = await _kit.SetupAsync();
        var otherAdmin = await _kit.LoginAsync(other, Who.Admin);
        var otherValue = (await Json(await otherAdmin.C.GetAsync(Settings))).GetProperty("siteVurguRengi");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, otherValue.ValueKind);

        // Boş gönderim = varsayılana dönüş (tam değiştirme).
        saved = await Json(await Send(admin, HttpMethod.Put, Settings, new { siteVurguRengi = "", surum = version }));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, saved.GetProperty("siteVurguRengi").ValueKind);
    }

    // Site teması: yalnız bilinen anahtar (büyük harf normalize), bilinmeyen → 400 siteTemasi, boş → null (pazar),
    // başka firmaya sızmaz.
    [Fact]
    public async Task Site_theme_round_trips_validates_and_stays_in_tenant()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var version = (await Json(await admin.C.GetAsync(Settings))).GetProperty("surum").GetString();

        var saved = await Json(await Send(admin, HttpMethod.Put, Settings, new { siteTemasi = "Vitrin", surum = version }));
        Assert.Equal("vitrin", saved.GetProperty("siteTemasi").GetString());
        version = saved.GetProperty("surum").GetString();

        foreach (var bad in new[] { "mavi", "tarife;}", "vitrin2" })
            await Problem(await Send(admin, HttpMethod.Put, Settings, new { siteTemasi = bad, surum = version }),
                HttpStatusCode.BadRequest, "dogrulama", "siteTemasi");
        Assert.Equal("vitrin", (await Json(await admin.C.GetAsync(Settings))).GetProperty("siteTemasi").GetString());

        var other = await _kit.SetupAsync();
        var otherAdmin = await _kit.LoginAsync(other, Who.Admin);
        Assert.Equal(System.Text.Json.JsonValueKind.Null,
            (await Json(await otherAdmin.C.GetAsync(Settings))).GetProperty("siteTemasi").ValueKind);

        saved = await Json(await Send(admin, HttpMethod.Put, Settings, new { siteTemasi = "", surum = version }));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, saved.GetProperty("siteTemasi").ValueKind);
    }

    // Geçersiz tema anahtarı genel (alansız) hata DEĞİL: 400 + errors.siteTemasi, başka alana düşmez; mesaj geçerli
    // değerleri sayar. Boşluklu/büyük harfli geçersiz değer ve kolon sınırını aşan değer de aynı alana bağlanır.
    [Fact]
    public async Task Invalid_site_theme_is_bound_to_siteTemasi_field()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var version = (await Json(await admin.C.GetAsync(Settings))).GetProperty("surum").GetString();

        foreach (var bad in new[] { " KONTUAR2 ", "Mavi", new string('v', 21) })
        {
            var p = await Problem(await Send(admin, HttpMethod.Put, Settings, new { siteTemasi = bad, surum = version }),
                HttpStatusCode.BadRequest, "dogrulama", "siteTemasi");
            var fields = p.GetProperty("errors").EnumerateObject().Select(x => x.Name).ToList();
            Assert.Equal(["siteTemasi"], fields);
        }

        var last = await Problem(await Send(admin, HttpMethod.Put, Settings, new { siteTemasi = "mavi", surum = version }),
            HttpStatusCode.BadRequest, "dogrulama", "siteTemasi");
        var text = last.GetProperty("errors").GetProperty("siteTemasi").ToString();
        Assert.Contains("pazar", text);
        Assert.Contains("vitrin", text);
        Assert.Contains("rota", text);
        // Eski anahtarlar kabul edilir ama geçerli değer olarak İLAN EDİLMEZ.
        Assert.DoesNotContain("tarife", text);
        Assert.DoesNotContain("kontuar", text);

        // Hiçbiri yazılmadı: ayar varsayılanda (null).
        Assert.Equal(System.Text.Json.JsonValueKind.Null,
            (await Json(await admin.C.GetAsync(Settings))).GetProperty("siteTemasi").ValueKind);
    }

    // Anahtar göçü (tarife → pazar, kontuar → rota): eski anahtarla gelen PUT REDDEDİLMEZ, yeni anahtara normalize
    // edilip saklanır. Gerekçe: eski SPA sekmesi / önbellekteki istemci kaydederken kullanıcının seçimi kaybolmasın
    // ve 400 almasın; sitenin ?tema= önizlemesiyle aynı kural (SiteThemeKeys.Normalize tek kaynak). Eski anahtar
    // DB'ye hiç yazılmaz, okunurken yeni anahtar döner.
    [Fact]
    public async Task Legacy_site_theme_key_is_accepted_and_stored_as_new_key()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var version = (await Json(await admin.C.GetAsync(Settings))).GetProperty("surum").GetString();

        foreach (var (legacy, current) in new[] { ("tarife", "pazar"), (" Kontuar ", "rota"), ("TARIFE", "pazar") })
        {
            var saved = await Json(await Send(admin, HttpMethod.Put, Settings, new { siteTemasi = legacy, surum = version }));
            Assert.Equal(current, saved.GetProperty("siteTemasi").GetString());
            version = saved.GetProperty("surum").GetString();
            Assert.Equal(current, (await Json(await admin.C.GetAsync(Settings))).GetProperty("siteTemasi").GetString());
        }

        var rota = await Json(await Send(admin, HttpMethod.Put, Settings, new { siteTemasi = "rota", surum = version }));
        Assert.Equal("rota", rota.GetProperty("siteTemasi").GetString());
    }
}
