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

    // Site teması: yalnız bilinen anahtar (büyük harf normalize), bilinmeyen → 400 siteTemasi, boş → null (anahtar),
    // başka firmaya sızmaz.
    [Fact]
    public async Task Site_theme_round_trips_validates_and_stays_in_tenant()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var version = (await Json(await admin.C.GetAsync(Settings))).GetProperty("surum").GetString();

        var saved = await Json(await Send(admin, HttpMethod.Put, Settings, new { siteTemasi = "Kiyas", surum = version }));
        Assert.Equal("kiyas", saved.GetProperty("siteTemasi").GetString());
        version = saved.GetProperty("surum").GetString();

        foreach (var bad in new[] { "mavi", "tarife;}", "kiyas2" })
            await Problem(await Send(admin, HttpMethod.Put, Settings, new { siteTemasi = bad, surum = version }),
                HttpStatusCode.BadRequest, "dogrulama", "siteTemasi");
        Assert.Equal("kiyas", (await Json(await admin.C.GetAsync(Settings))).GetProperty("siteTemasi").GetString());

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
        Assert.Contains("anahtar", text);
        Assert.Contains("kiyas", text);
        Assert.Contains("paket", text);
        // Eski anahtarlar kabul edilir ama geçerli değer olarak İLAN EDİLMEZ.
        foreach (var legacy in new[] { "tarife", "kontuar", "pazar", "vitrin", "rota" })
            Assert.DoesNotContain(legacy, text);

        // Hiçbiri yazılmadı: ayar varsayılanda (null).
        Assert.Equal(System.Text.Json.JsonValueKind.Null,
            (await Json(await admin.C.GetAsync(Settings))).GetProperty("siteTemasi").ValueKind);
    }

    // Site palet seti: aynı istekteki temanın setlerinden biri (büyük harf/boşluk normalize), katalog GET'te gelir,
    // başka firmaya sızmaz, boş → null (temanın varsayılanı). Beklenen anahtarlar ve renkler elle yazıldı.
    [Fact]
    public async Task Site_palette_round_trips_with_catalog_and_stays_in_tenant()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var first = await Json(await admin.C.GetAsync(Settings));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, first.GetProperty("sitePaleti").ValueKind);
        var catalog = first.GetProperty("sitePaletSecenekleri").EnumerateArray()
            .ToDictionary(t => t.GetProperty("tema").GetString()!,
                t => t.GetProperty("paletler").EnumerateArray().Select(p => p.GetProperty("anahtar").GetString()).ToList());
        Assert.Equal(["anahtar", "kiyas", "paket"], catalog.Keys);
        Assert.Equal(["zumrut", "lacivert", "visne", "grafit"], catalog["anahtar"]);
        Assert.Equal(["mavi", "turkuaz", "kiremit", "gece"], catalog["kiyas"]);
        Assert.Equal(["kiremit", "lacivert", "zeytin", "grafit"], catalog["paket"]);
        var zeytin = first.GetProperty("sitePaletSecenekleri").EnumerateArray().Single(t => t.GetProperty("tema").GetString() == "paket")
            .GetProperty("paletler").EnumerateArray().Single(p => p.GetProperty("anahtar").GetString() == "zeytin");
        Assert.Equal("#4a5a23", zeytin.GetProperty("ana").GetString());
        Assert.Equal("#f2f2ec", zeytin.GetProperty("zemin").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, zeytin.GetProperty("vurgu").ValueKind); // paket: vurgu ana rengi izler

        var version = first.GetProperty("surum").GetString();
        var saved = await Json(await Send(admin, HttpMethod.Put, Settings,
            new { siteTemasi = "kiyas", sitePaleti = " Turkuaz ", surum = version }));
        Assert.Equal("turkuaz", saved.GetProperty("sitePaleti").GetString());
        Assert.Equal("turkuaz", (await Json(await admin.C.GetAsync(Settings))).GetProperty("sitePaleti").GetString());
        version = saved.GetProperty("surum").GetString();

        var other = await _kit.SetupAsync();
        var otherAdmin = await _kit.LoginAsync(other, Who.Admin);
        Assert.Equal(System.Text.Json.JsonValueKind.Null,
            (await Json(await otherAdmin.C.GetAsync(Settings))).GetProperty("sitePaleti").ValueKind);

        saved = await Json(await Send(admin, HttpMethod.Put, Settings, new { siteTemasi = "kiyas", sitePaleti = "", surum = version }));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, saved.GetProperty("sitePaleti").ValueKind);
    }

    // Temaya ait olmayan (başka temanın seti; tema boşsa varsayılan tema anahtar), bilinmeyen, biçimsiz ya da kolon
    // sınırını aşan palet: 400 + YALNIZ errors.sitePaleti; mesaj temanın geçerli setlerini sayar; kayıt değişmez.
    // Eski tema anahtarı (vitrin → paket) aitlikte de yeni karşılığıyla değerlendirilir.
    [Fact]
    public async Task Invalid_or_foreign_site_palette_is_bound_to_sitePaleti_field()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var version = (await Json(await admin.C.GetAsync(Settings))).GetProperty("surum").GetString();

        foreach (var (theme, bad) in new (string?, string)[]
                 {
                     ("paket", "zumrut"), (null, "mavi"), ("kiyas", "olmayan"), ("vitrin", "turkuaz"), ("anahtar", "zumrut;}"),
                     ("anahtar", "domates"), ("anahtar", "zum rut"), ("anahtar", new string('a', 31)), ("anahtar", "zumrut</style>"),
                 })
        {
            var p = await Problem(await Send(admin, HttpMethod.Put, Settings, new { siteTemasi = theme, sitePaleti = bad, surum = version }),
                HttpStatusCode.BadRequest, "dogrulama", "sitePaleti");
            Assert.Equal(["sitePaleti"], p.GetProperty("errors").EnumerateObject().Select(x => x.Name).ToList());
        }

        var last = await Problem(await Send(admin, HttpMethod.Put, Settings, new { siteTemasi = "kiyas", sitePaleti = "zumrut", surum = version }),
            HttpStatusCode.BadRequest, "dogrulama", "sitePaleti");
        var text = last.GetProperty("errors").GetProperty("sitePaleti").ToString();
        foreach (var key in new[] { "mavi", "turkuaz", "kiremit", "gece" })
            Assert.Contains(key, text);

        var now = await Json(await admin.C.GetAsync(Settings));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, now.GetProperty("sitePaleti").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, now.GetProperty("siteTemasi").ValueKind);

        // Eski tema anahtarı + yeni karşılığının seti kabul edilir (tema yeni anahtarla saklanır).
        var saved = await Json(await Send(admin, HttpMethod.Put, Settings, new { siteTemasi = "vitrin", sitePaleti = "zeytin", surum = version }));
        Assert.Equal(("paket", "zeytin"), (saved.GetProperty("siteTemasi").GetString(), saved.GetProperty("sitePaleti").GetString()));
    }

    // Anahtar göçü (tarife/pazar → anahtar, kontuar/rota → kiyas, vitrin → paket): eski anahtarla gelen PUT REDDEDİLMEZ, yeni anahtara normalize
    // edilip saklanır. Gerekçe: eski SPA sekmesi / önbellekteki istemci kaydederken kullanıcının seçimi kaybolmasın
    // ve 400 almasın; sitenin ?tema= önizlemesiyle aynı kural (SiteThemeKeys.Normalize tek kaynak). Eski anahtar
    // DB'ye hiç yazılmaz, okunurken yeni anahtar döner.
    [Fact]
    public async Task Legacy_site_theme_key_is_accepted_and_stored_as_new_key()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var version = (await Json(await admin.C.GetAsync(Settings))).GetProperty("surum").GetString();

        foreach (var (legacy, current) in new[]
                 {
                     ("tarife", "anahtar"), (" Kontuar ", "kiyas"), ("PAZAR", "anahtar"), ("rota", "kiyas"), ("Vitrin", "paket"),
                 })
        {
            var saved = await Json(await Send(admin, HttpMethod.Put, Settings, new { siteTemasi = legacy, surum = version }));
            Assert.Equal(current, saved.GetProperty("siteTemasi").GetString());
            version = saved.GetProperty("surum").GetString();
            Assert.Equal(current, (await Json(await admin.C.GetAsync(Settings))).GetProperty("siteTemasi").GetString());
        }

        var paket = await Json(await Send(admin, HttpMethod.Put, Settings, new { siteTemasi = "paket", surum = version }));
        Assert.Equal("paket", paket.GetProperty("siteTemasi").GetString());
    }
}
