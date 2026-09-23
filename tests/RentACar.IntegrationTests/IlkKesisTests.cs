using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Spa;

namespace RentACar.IntegrationTests;

/// <summary>
/// F4.6 ilk kesiş — saf kararlar (<see cref="IlkKesis"/>): yönlendirme haritası, önek paylaşan yolların DIŞARIDA
/// kalması, tek giriş (<c>/login</c> → <c>/app/giris</c>) ve giriş sonrası hedef.
/// BAĞIMSIZ ORACLE: beklenen adresler elle yazılmış sabitlerdir (sınıfın sabitleri/haritası kullanılmaz); harita
/// ayrıca F4 envanterine (docs/roadmap/F4.md) ve sayfaların gerçek <c>@page</c> satırlarına bağlanır.
/// </summary>
public sealed class IlkKesisKararTests
{
    private const string G = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";

    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Repo kökü bulunamadı.");
    }

    [Theory]
    [InlineData("/", "/app/panel")]
    [InlineData("/kiralar", "/app/kiralar")]
    [InlineData("/kiralar/", "/app/kiralar")]
    [InlineData("/Kiralar", "/app/kiralar")]                    // ASP.NET yönlendirmesi gibi harf duyarsız
    [InlineData("/kiralar/yeni", "/app/kiralar/yeni")]
    [InlineData("/KIRALAR/YENI", "/app/kiralar/yeni")]
    [InlineData("/kiralar/" + G, "/app/kiralar/" + G)]
    [InlineData("/kiralar/3F2504E0-4F89-11D3-9A0C-0305E82C3301", "/app/kiralar/" + G)] // kanonik (D, küçük harf)
    [InlineData("/kiralar/" + G + "/yazdir", "/app/kiralar/" + G + "/yazdir")]
    [InlineData("/kiralar/" + G + "/yazdir/", "/app/kiralar/" + G + "/yazdir")]
    // F5.4 rezervasyon kesişi: tek @page'li altı liste sayfası
    [InlineData("/rezervasyonlar", "/app/rezervasyonlar")]
    [InlineData("/Rezervasyonlar/", "/app/rezervasyonlar")]
    [InlineData("/teklifler", "/app/teklifler")]
    [InlineData("/takvim", "/app/takvim")]
    [InlineData("/musaitlik", "/app/musaitlik")]
    [InlineData("/MUSAITLIK/", "/app/musaitlik")]
    [InlineData("/rez-sartlari", "/app/rez-sartlari")]
    [InlineData("/filo-kiralama", "/app/filo-kiralama")]
    public void Haritadaki_sablon_SPA_yoluna_esler(string yol, string beklenen)
        => Assert.Equal(beklenen, IlkKesis.SpaYolu(yol));

    /// <summary>Önek paylaşan GET uçları ve diğer her şey haritada YOK (segment segment birebir eşleşme).</summary>
    [Theory]
    [InlineData("/kiralar/" + G + "/pdf")]                // sözleşme PDF
    [InlineData("/kiralar/" + G + "/pdf?indir=1")]
    [InlineData("/kiralar/ornek-sozlesme/pdf")]
    [InlineData("/kiralar/hesapla")]                       // canlı hesap (JSON)
    [InlineData("/kiralar/donus-hesapla")]
    [InlineData("/kiralar/musait-arac")]
    [InlineData("/kiralar/" + G + "/yazdir/x")]
    [InlineData("/kiralar/5")]                             // Guid değil
    [InlineData("/kiralar/yeni/x")]
    [InlineData("/kiralarx")]
    [InlineData("/kiralar//yazdir")]
    [InlineData("//kiralar")]
    [InlineData("/listeler/export/kiralar")]
    [InlineData("/raporlar/export/kiralar")]
    [InlineData("/kasa/makbuz/" + G + "/pdf")]
    [InlineData("/faturalar/" + G + "/pdf")]
    [InlineData("/vehicles")]
    // F5: Blazor'da karşılığı olmayan SPA alt rotaları, POST uçlarının GET'i, önek/benzer adlı sayfalar, export, takvim beslemesi
    [InlineData("/rezervasyonlar/yeni")]
    [InlineData("/rezervasyonlar/" + G)]
    [InlineData("/rezervasyonlar/create")]
    [InlineData("/rezervasyonlar/cancel")]
    [InlineData("/teklifler/yeni")]
    [InlineData("/teklifler/" + G)]
    [InlineData("/teklifler/kabul")]
    [InlineData("/takvim/yenile")]
    [InlineData("/takvim-abonelik")]
    [InlineData("/rezervasyon-kaynaklari")]
    [InlineData("/rezervasyonlarx")]
    [InlineData("/musaitlik/x")]
    [InlineData("/rez-sartlari/delete")]
    [InlineData("/filo-kiralama/yeni")]
    [InlineData("/filo-kiralama/" + G)]
    [InlineData("/filo-kiralama/iptal")]
    [InlineData("/listeler/export/rezervasyonlar")]
    [InlineData("/listeler/export/filo-kiralama")]
    [InlineData("/feed/calendar/x.ics")]
    [InlineData("/app/rezervasyonlar")]
    [InlineData("/api/ui/v1/rezervasyonlar")]
    [InlineData("/api/ui/v1/musaitlik")]
    [InlineData("/login")]
    [InlineData("/app/kiralar")]
    [InlineData("/api/ui/v1/kiralar")]
    [InlineData("/platform/tenants")]
    [InlineData("kiralar")]
    [InlineData("")]
    [InlineData(null)]
    public void Harita_disi_yol_yonlenmez(string? yol)
        => Assert.Null(IlkKesis.SpaYolu(yol));

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("OPTIONS")]
    public void Yalniz_GET_HEAD_yonlenir(string yontem)
    {
        Assert.Null(IlkKesis.SpaHedefi(yontem, "/kiralar", QueryString.Empty));
        Assert.Equal("/app/kiralar", IlkKesis.SpaHedefi("GET", "/kiralar", QueryString.Empty));
        Assert.Equal("/app/kiralar", IlkKesis.SpaHedefi("HEAD", "/kiralar", QueryString.Empty));
    }

    [Fact]
    public void Sorgu_dizesi_AYNEN_tasinir()
    {
        // Müsaitlik/araç durumu "Kirala" bağlantısı (kira sorgu sözleşmesi): varac, vfrom, vto, vgrup, musteriId.
        const string sorgu = "?varac=" + G + "&vfrom=2026-10-01T10%3A00&vto=2026-10-04T10%3A00&vgrup=%C3%96zel+Grup&musteriId=" + G;
        Assert.Equal("/app/kiralar/yeni" + sorgu, IlkKesis.SpaHedefi("GET", "/kiralar/yeni", new QueryString(sorgu)));
        Assert.Equal("/app/kiralar?q=a&q=b&bilgi=x", IlkKesis.SpaHedefi("GET", "/kiralar", new QueryString("?q=a&q=b&bilgi=x")));
        // Ham (kodlanmamış) ASCII dışı sorgu Location'a yazılamaz → yönlendirme yok (Blazor sayfası açılır, 500 değil).
        Assert.Null(IlkKesis.SpaHedefi("GET", "/kiralar", new QueryString("?q=Yılmaz")));
        Assert.Null(IlkKesis.SpaHedefi("GET", "/kiralar", new QueryString("?q=a b")));
    }

    [Theory]
    [InlineData("/login", true)]
    [InlineData("/login/", true)]
    [InlineData("/LOGIN", true)]
    [InlineData("/platform/login", false)]                 // platform girişi ETKİLENMEZ
    [InlineData("/loginx", false)]
    [InlineData("/login/x", false)]
    [InlineData("/", false)]
    [InlineData("", false)]
    public void Blazor_giris_yolu_segment_esitligiyle(string yol, bool beklenen)
        => Assert.Equal(beklenen, IlkKesis.BlazorGirisMi(yol.Length == 0 ? PathString.Empty : new PathString(yol)));

    [Theory]
    [InlineData("", "/app/giris")]
    [InlineData("?ReturnUrl=%2Fkiralar%3Fvarac%3Dx", "/app/giris?returnUrl=%2Fkiralar%3Fvarac%3Dx")]
    [InlineData("?ReturnUrl=%2Fkiralar%2F" + G + "%3Fsekme%3Dodeme", "/app/giris?returnUrl=%2Fkiralar%2F" + G + "%3Fsekme%3Dodeme")]
    [InlineData("?returnurl=%2Fvehicles", "/app/giris?returnUrl=%2Fvehicles")]            // anahtar harf duyarsız
    [InlineData("?ReturnUrl=%2F", "/app/giris")]                                         // Panel = varsayılan, taşınmaz
    [InlineData("?ReturnUrl=%2F%2Fevil.com", "/app/giris")]                              // açık yönlendirme
    [InlineData("?ReturnUrl=https%3A%2F%2Fevil.com", "/app/giris")]
    [InlineData("?ReturnUrl=%2Flogin", "/app/giris")]                                    // döngü
    [InlineData("?ReturnUrl=%2Fplatform%2Ftenants", "/app/giris")]                       // alan geçişi
    [InlineData("?ReturnUrl=%2Flisteler%2Fexport%2Fcariler", "/app/giris")]              // indirme dönüş olamaz
    [InlineData("?hata=kapali", "/app/giris?neden=kiraci_kapali")]
    [InlineData("?hata=1&ReturnUrl=%2Fvehicles", "/app/giris?returnUrl=%2Fvehicles")]   // diğer hata kodları taşınmaz
    [InlineData("?bilgi=x&foo=bar", "/app/giris")]
    public void Oturumsuz_login_SPA_girisine(string sorgu, string beklenen)
        => Assert.Equal(beklenen, IlkKesis.GirisYonlendirmesi(new QueryString(sorgu.Length == 0 ? null : sorgu)));

    [Theory]
    // pilot: SPA hedefi — /app dönüşü aynen, haritadaki Blazor adresi SPA karşılığına, varsayılan Panel
    [InlineData(true, null, "/app/panel")]
    [InlineData(true, "/", "/app/panel")]
    [InlineData(true, "/kiralar?varac=x&vfrom=y", "/app/kiralar?varac=x&vfrom=y")]
    [InlineData(true, "/kiralar/yeni?varac=x", "/app/kiralar/yeni?varac=x")]
    [InlineData(true, "/kiralar/" + G + "#sekme=odeme", "/app/kiralar/" + G + "#sekme=odeme")]
    [InlineData(true, "/app/kiralar?q=a", "/app/kiralar?q=a")]
    [InlineData(true, "/app/giris?returnUrl=%2Fapp", "/app/panel")]                     // döngü yok
    [InlineData(true, "/APP/GIRIS", "/app/panel")]
    [InlineData(true, "/rezervasyonlar?durum=Rezerv", "/app/rezervasyonlar?durum=Rezerv")] // F5.4
    [InlineData(true, "/musaitlik?from=2026-10-01&to=2026-10-04", "/app/musaitlik?from=2026-10-01&to=2026-10-04")]
    [InlineData(true, "/takvim-abonelik", "/takvim-abonelik")]                          // F5 dışı benzer ad Blazor'da
    [InlineData(true, "/vehicles?x=1", "/vehicles?x=1")]                                // taşınmamış modül Blazor'da
    [InlineData(true, "/kiralar/" + G + "/pdf", "/kiralar/" + G + "/pdf")]              // PDF yönlenmez
    [InlineData(true, "//evil.com", "/app/panel")]
    [InlineData(true, "/login", "/app/panel")]
    [InlineData(true, "/listeler/export/cariler", "/app/panel")]
    // pilot değil: Blazor — /app dönüşü Panel'e
    [InlineData(false, null, "/")]
    [InlineData(false, "/app/kiralar", "/")]
    [InlineData(false, "/app", "/")]
    [InlineData(false, "/kiralar?varac=x", "/kiralar?varac=x")]
    [InlineData(false, "/rezervasyonlar?durum=Rezerv", "/rezervasyonlar?durum=Rezerv")]
    [InlineData(false, "/app/rezervasyonlar", "/")]
    [InlineData(false, "/vehicles", "/vehicles")]
    [InlineData(false, "//evil.com", "/")]
    [InlineData(false, "/platform/tenants", "/")]
    public void Oturumlu_giris_sonrasi_hedef(bool pilot, string? donus, string beklenen)
        => Assert.Equal(beklenen, IlkKesis.GirisSonrasi(pilot, donus));

    [Theory]
    [InlineData("/app/panel", "/")]
    [InlineData("/app/kiralar", "/kiralar")]
    [InlineData("/app/kiralar/yeni", "/kiralar/yeni")]
    [InlineData("/app/kiralar/{id}", null)]                // parametreli rota menüde yok
    [InlineData("/app/rezervasyonlar", "/rezervasyonlar")] // F5.4 menü öğeleri
    [InlineData("/app/teklifler", "/teklifler")]
    [InlineData("/app/takvim", "/takvim")]
    [InlineData("/app/musaitlik", "/musaitlik")]
    [InlineData("/app/rez-sartlari", "/rez-sartlari")]
    [InlineData("/app/filo-kiralama", "/filo-kiralama")]
    [InlineData("/app/rezervasyonlar/yeni", null)]         // Blazor'da ayrı "yeni" sayfası yoktu
    [InlineData("/vehicles", null)]
    public void Menu_icin_Blazor_karsiligi(string spa, string? beklenen)
        => Assert.Equal(beklenen, IlkKesis.BlazorKarsiligi(spa));

    /// <summary>
    /// Bir fazın envanter tablosundaki (<c>docs/roadmap/F?.md</c>) sayfa rotaları; aynı sayfaların gerçek <c>@page</c>
    /// satırlarıyla BİREBİR karşılaştırılır (sayfa rotası değişir ya da envantere sayfa eklenirse kırmızı).
    /// </summary>
    private static HashSet<string> EnvanterRotalari(string faz, int beklenenSayfa)
    {
        var kok = RepoKok();
        var md = File.ReadAllText(Path.Combine(kok, $"docs/roadmap/{faz}.md"));
        var satirlar = Regex.Matches(md, @"^\| `(?<dosya>[^`]+\.razor)`[^|]*\| (?<rotalar>[^|]+) \|", RegexOptions.Multiline);
        Assert.Equal(beklenenSayfa, satirlar.Count);

        static string Normal(string r) => r.Trim().Replace("{Id:guid}", "{id:guid}", StringComparison.Ordinal);
        var envanter = new HashSet<string>(StringComparer.Ordinal);
        var sayfalar = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match s in satirlar)
        {
            foreach (var r in s.Groups["rotalar"].Value.Split("<br>")) envanter.Add(Normal(r.Trim('`', ' ')));
            var dosya = Path.Combine(kok, "src/RentACar.Web/Components/Pages", s.Groups["dosya"].Value);
            foreach (Match p in Regex.Matches(File.ReadAllText(dosya), @"^@page\s+""(?<r>[^""]+)""", RegexOptions.Multiline))
                sayfalar.Add(Normal(p.Groups["r"].Value));
        }

        Assert.Equal(envanter.OrderBy(x => x), sayfalar.OrderBy(x => x)); // envanter = sayfaların gerçek rotaları
        return envanter;
    }

    /// <summary>
    /// Harita = kesişi yapılmış fazların (F4, F5) envanterindeki silinecek <c>@page</c> şablonları (<c>/login</c>
    /// hariç — o herkes için tek giriş). Fazlar ayrık; hedefler /app altında ve benzersiz.
    /// </summary>
    [Fact]
    public void Harita_kesisi_yapilmis_fazlarin_page_sablonlarindan_turetilmis()
    {
        var f4 = EnvanterRotalari("F4", 5);
        Assert.Contains("/login", f4);
        f4.Remove("/login");
        var f5 = EnvanterRotalari("F5", 6);
        Assert.Equal(new[] { "/filo-kiralama", "/musaitlik", "/rez-sartlari", "/rezervasyonlar", "/takvim", "/teklifler" },
            f5.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Empty(f4.Intersect(f5));

        Assert.Equal(f4.Concat(f5).OrderBy(x => x), IlkKesis.Harita.Select(e => e.Kaynak).OrderBy(x => x));
        Assert.All(IlkKesis.Harita, e => Assert.StartsWith("/app/", e.Hedef));
        Assert.Equal(IlkKesis.Harita.Count, IlkKesis.Harita.Select(e => e.Hedef).Distinct().Count());
    }

    /// <summary>
    /// F5 haritasının hedefleri SPA'da GERÇEK rota (bağımsız kaynak: Angular rota dosyaları metin olarak okunur —
    /// yoksa pilot kullanıcı 302 sonrası SPA'nın "sayfa yok" ekranına düşerdi).
    /// </summary>
    [Fact]
    public void F5_hedefleri_SPA_rota_dosyalarinda_tanimli()
    {
        var app = Path.Combine(RepoKok(), "src/RentACar.Frontend/src/app");
        var metin = File.ReadAllText(Path.Combine(app, "sayfalar.ts"))
            + File.ReadAllText(Path.Combine(app, "features/rezervasyonlar/rezervasyonlar.routes.ts"));
        foreach (var yol in new[] { "rezervasyonlar", "teklifler", "takvim", "musaitlik", "rez-sartlari", "filo-kiralama" })
            Assert.Contains($"path: '{yol}',", metin);
    }
}

/// <summary>
/// F4.6 ilk kesiş — GERÇEK Web boru hattı (<c>IlkKesisMiddleware</c> + cookie challenge + TenantActive +
/// PlatformIsolation): pilot yönlendirmeleri, negatif liste (önek paylaşan GET uçları, POST, pilot olmayan firma),
/// tek giriş, döngü yokluğu ve platform pilot anahtarı. Beklenen Location değerleri elle yazılmış sabitlerdir.
/// </summary>
[Collection("web")]
public sealed class IlkKesisHostTests(WebFixture fx)
{
    private const string G = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";

    private static string? CerezDegeri(HttpResponseMessage r, string ad)
    {
        if (!r.Headers.TryGetValues("Set-Cookie", out var degerler)) return null;
        foreach (var d in degerler)
            if (d.StartsWith(ad + "=", StringComparison.Ordinal))
                return Uri.UnescapeDataString(d[(ad.Length + 1)..].Split(';')[0]);
        return null;
    }

    /// <summary>Yeni arayüzün giriş ucuyla oturum açar (tek giriş) — cookie istemcide kalır.</summary>
    private async Task<HttpClient> OturumAsync(TestKimlik k)
    {
        var c = fx.Web.Istemci();
        var x = await c.GetAsync("/api/ui/v1/oturum/xsrf");
        var xsrf = CerezDegeri(x, "XSRF-TOKEN") ?? throw new Xunit.Sdk.XunitException("XSRF yok");
        using var istek = new HttpRequestMessage(HttpMethod.Post, "/api/ui/v1/oturum/giris")
        {
            Content = JsonContent.Create(new { firma = k.Firma, kullanici = k.Kullanici, sifre = k.Sifre }),
        };
        istek.Headers.Add("X-XSRF-TOKEN", xsrf);
        var r = await c.SendAsync(istek);
        Assert.True(r.StatusCode == HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return c;
    }

    private async Task<HttpClient> PlatformOturumuAsync()
    {
        var c = fx.Web.Istemci();
        var r = await c.PostAsync("/platform/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["kullanici"] = fx.Platform.Kullanici, ["sifre"] = fx.Platform.Sifre,
        }));
        Assert.Equal("/platform/tenants", r.Headers.Location?.OriginalString);
        return c;
    }

    private static async Task<string?> KonumAsync(HttpClient c, string url, HttpMethod? yontem = null)
    {
        using var istek = new HttpRequestMessage(yontem ?? HttpMethod.Get, url);
        var r = await c.SendAsync(istek);
        return r.Headers.Location?.OriginalString;
    }

    private static async Task YonlenirAsync(HttpClient c, string url, string beklenen, HttpMethod? yontem = null)
    {
        using var istek = new HttpRequestMessage(yontem ?? HttpMethod.Get, url);
        var r = await c.SendAsync(istek);
        Assert.True(r.StatusCode == HttpStatusCode.Redirect, $"{url}: beklenen 302, gelen {(int)r.StatusCode}");
        Assert.Equal(beklenen, r.Headers.Location?.OriginalString);
    }

    /// <summary>302 zincirini elle izler (en çok 8 adım): (adres, durum) listesi. Döngü = aynı adres iki kez.</summary>
    private static async Task<List<(string Adres, HttpStatusCode Durum)>> ZincirAsync(HttpClient c, string url)
    {
        var zincir = new List<(string, HttpStatusCode)>();
        for (var i = 0; i < 8; i++)
        {
            var r = await c.GetAsync(url);
            zincir.Add((url, r.StatusCode));
            if ((int)r.StatusCode is < 300 or >= 400) return zincir;
            var sonraki = r.Headers.Location?.OriginalString ?? throw new Xunit.Sdk.XunitException($"{url}: Location yok");
            Assert.True(zincir.All(z => z.Item1 != sonraki), "Yönlendirme DÖNGÜSÜ: " + string.Join(" → ", zincir.Select(z => z.Item1)) + " → " + sonraki);
            url = sonraki;
        }
        throw new Xunit.Sdk.XunitException("8 adımda bitmeyen yönlendirme zinciri: " + string.Join(" → ", zincir.Select(z => z.Item1)));
    }

    // ------------------------------------------------------------ pilot yönlendirme haritası

    [Fact]
    public async Task Pilot_F4_sayfalari_SPA_ya_302_sorgu_AYNEN()
    {
        var c = await OturumAsync(fx.PilotAdmin);
        const string kirala = "?varac=" + G + "&vfrom=2026-10-01T10%3A00&vto=2026-10-04T10%3A00&vgrup=Ekonomi&musteriId=" + G;

        await YonlenirAsync(c, "/", "/app/panel");
        await YonlenirAsync(c, "/?df=bugun", "/app/panel?df=bugun");
        await YonlenirAsync(c, "/kiralar", "/app/kiralar");
        await YonlenirAsync(c, "/kiralar?q=Y%C4%B1lmaz&bilgi=Kaydedildi", "/app/kiralar?q=Y%C4%B1lmaz&bilgi=Kaydedildi");
        await YonlenirAsync(c, "/kiralar/yeni" + kirala, "/app/kiralar/yeni" + kirala);
        await YonlenirAsync(c, "/kiralar/" + G, "/app/kiralar/" + G);
        await YonlenirAsync(c, "/kiralar/" + G + "?hata=x", "/app/kiralar/" + G + "?hata=x");
        await YonlenirAsync(c, "/kiralar/" + G + "/yazdir", "/app/kiralar/" + G + "/yazdir");
        await YonlenirAsync(c, "/kiralar", "/app/kiralar", HttpMethod.Head);

        // Operatör de (izin kapısı SPA'da/API'de; Blazor sayfaları yalnız [Authorize]).
        var op = await OturumAsync(fx.PilotOperator);
        await YonlenirAsync(op, "/kiralar/yeni" + kirala, "/app/kiralar/yeni" + kirala);
    }

    /// <summary>F5.4: rezervasyon modülünün altı Blazor sayfası pilot firmada SPA'ya; Blazor sorgusu AYNEN taşınır.</summary>
    [Fact]
    public async Task Pilot_F5_sayfalari_SPA_ya_302_sorgu_AYNEN()
    {
        var c = await OturumAsync(fx.PilotAdmin);
        await YonlenirAsync(c, "/rezervasyonlar", "/app/rezervasyonlar");
        await YonlenirAsync(c, "/rezervasyonlar?durum=Rezerv&ara=Y%C4%B1lmaz", "/app/rezervasyonlar?durum=Rezerv&ara=Y%C4%B1lmaz");
        await YonlenirAsync(c, "/rezervasyonlar?vurgu=" + G, "/app/rezervasyonlar?vurgu=" + G); // gelen talep dönüşümü
        await YonlenirAsync(c, "/teklifler", "/app/teklifler");
        await YonlenirAsync(c, "/takvim?ay=2026-10", "/app/takvim?ay=2026-10");
        await YonlenirAsync(c, "/musaitlik?from=2026-10-01&to=2026-10-04", "/app/musaitlik?from=2026-10-01&to=2026-10-04");
        await YonlenirAsync(c, "/rez-sartlari?musteriId=" + G, "/app/rez-sartlari?musteriId=" + G);
        await YonlenirAsync(c, "/filo-kiralama/", "/app/filo-kiralama");
        await YonlenirAsync(c, "/musaitlik", "/app/musaitlik", HttpMethod.Head);

        var op = await OturumAsync(fx.PilotOperator);
        await YonlenirAsync(op, "/rezervasyonlar", "/app/rezervasyonlar");
        await YonlenirAsync(op, "/musaitlik", "/app/musaitlik");
    }

    [Fact]
    public async Task Pilot_olmayan_firma_yonlenmez_Blazor_sayfasi_acilir()
    {
        var c = await OturumAsync(fx.DigerAdmin);
        foreach (var url in new[]
                 {
                     "/", "/kiralar", "/kiralar/yeni",
                     "/rezervasyonlar", "/teklifler", "/takvim", "/musaitlik", "/rez-sartlari", "/filo-kiralama",
                 })
        {
            var r = await c.GetAsync(url);
            Assert.True(r.StatusCode == HttpStatusCode.OK, $"{url}: {(int)r.StatusCode} {r.Headers.Location}");
            Assert.Equal("text/html", r.Content.Headers.ContentType?.MediaType);
        }
    }

    /// <summary>
    /// Önek paylaşan GET uçlarının TAMAMI (uç tablosundan: <c>/kiralar</c> altındaki her minimal-API GET) + PDF,
    /// hesap, export, makbuz: pilot oturumunda /app'e YÖNLENMEZ. Liste elle değil uç tablosundan — yeni bir
    /// <c>/kiralar/…</c> GET ucu eklenirse kendiliğinden kapsanır.
    /// </summary>
    [Fact]
    public async Task Pilot_onek_paylasan_GET_uclari_yonlenmez()
    {
        var c = await OturumAsync(fx.PilotAdmin);
        var kiraGetleri = MinimalGetUclari()
            .Where(u => u.StartsWith("/kiralar/", StringComparison.OrdinalIgnoreCase)).ToList();
        // Canlıda bilinen: PDF, örnek sözleşme PDF, hesapla, müsait-araç, dönüş-hesapla (rg "MapGet(\"/kiralar").
        Assert.Contains("/kiralar/{id:guid}/pdf", kiraGetleri);
        Assert.Contains("/kiralar/ornek-sozlesme/pdf", kiraGetleri);
        Assert.Contains("/kiralar/hesapla", kiraGetleri);
        Assert.Contains("/kiralar/donus-hesapla", kiraGetleri);
        Assert.Contains("/kiralar/musait-arac", kiraGetleri);

        // F5.4: haritadaki HER kaynak sayfanın altındaki minimal-API GET'leri de (bugün yok; eklenirse kapsanır).
        var onekler = IlkKesis.Harita.Select(e => e.Kaynak).Where(k => k != "/" && !k.Contains('{')).ToList();
        var altGetler = MinimalGetUclari()
            .Where(u => onekler.Any(o => u.StartsWith(o + "/", StringComparison.OrdinalIgnoreCase))).ToList();

        var adresler = kiraGetleri.Concat(altGetler).Distinct().Select(Ornek).Concat(
        [
            "/kiralar/" + G + "/pdf?indir=1",
            "/listeler/export/kiralar?format=excel",
            "/listeler/export/kiralar?format=pdf&q=x",
            "/raporlar/export/gelir-gider",
            "/kasa/makbuz/" + G + "/pdf",
            "/faturalar/" + G + "/pdf",
            "/vehicles",
            // F5: export (liste ekranlarının Excel/CSV/PDF'i), takvim beslemesi, benzer adlı Blazor sayfaları
            "/listeler/export/rezervasyonlar?format=excel",
            "/listeler/export/rezervasyonlar?format=pdf&ara=x",
            "/listeler/export/filo-kiralama?format=csv",
            "/listeler/export/teklifler",
            "/feed/calendar/x.ics",
            "/takvim-abonelik",
            "/rezervasyon-kaynaklari",
        ]).ToList();
        var ihlal = new List<string>();
        foreach (var url in adresler)
        {
            var konum = await KonumAsync(c, url);
            if (konum is not null && konum.StartsWith("/app", StringComparison.OrdinalIgnoreCase)) ihlal.Add($"{url} → {konum}");
        }
        Assert.True(ihlal.Count == 0, "Harita dışı GET ucu SPA'ya yönlendi:\n  " + string.Join("\n  ", ihlal));
    }

    [Fact]
    public async Task Pilot_POST_yonlenmez()
    {
        var c = await OturumAsync(fx.PilotAdmin);
        foreach (var url in new[]
                 {
                     "/kiralar/create", "/kiralar/update", "/kiralar/yeni", "/kiralar", "/",
                     // F5 Blazor POST uçları (hedefsiz; bu PR'da silinmedi) + sayfa yollarına POST
                     "/rezervasyonlar/create", "/rezervasyonlar/update", "/rezervasyonlar/confirm", "/rezervasyonlar/cancel",
                     "/rezervasyonlar/convert", "/teklifler/create", "/teklifler/gonder", "/teklifler/kabul", "/teklifler/reddet",
                     "/filo-kiralama/create", "/filo-kiralama/guncelle", "/filo-kiralama/iptal", "/filo-kiralama/tamamla",
                     "/rez-sartlari/create", "/rez-sartlari/update", "/rez-sartlari/karsilandi", "/rez-sartlari/geri-al",
                     "/rez-sartlari/delete", "/rezervasyonlar", "/musaitlik", "/takvim",
                 })
        {
            var r = await c.PostAsync(url, new FormUrlEncodedContent([]));
            var konum = r.Headers.Location?.OriginalString;
            Assert.False(konum?.StartsWith("/app", StringComparison.OrdinalIgnoreCase) == true, $"POST {url} → {konum}");
        }
    }

    /// <summary>Uygulamadaki HER minimal-API GET ucunun örnek adresi haritada yok (Razor sayfası olmayan hiçbir GET yönlenemez).</summary>
    [Fact]
    public void Hicbir_minimal_API_GET_ucu_haritada_degil()
    {
        var uclar = MinimalGetUclari();
        Assert.True(uclar.Count > 40, $"Uç tablosu şüpheli: {uclar.Count}");
        var ihlal = uclar.Where(u => IlkKesis.SpaYolu(Ornek(u).Split('?')[0]) is not null).ToList();
        Assert.True(ihlal.Count == 0, "Haritaya düşen minimal-API GET ucu: " + string.Join(", ", ihlal));
    }

    private List<string> MinimalGetUclari()
        => fx.Web.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<System.Reflection.MethodInfo>() is not null) // Razor sayfası değil
            .Where(e => e.Metadata.GetMetadata<HttpMethodMetadata>() is not { } m || m.HttpMethods.Contains("GET"))
            .Select(e => "/" + (e.RoutePattern.RawText ?? "").TrimStart('/'))
            .Distinct().ToList();

    /// <summary>Rota şablonundan örnek adres: <c>{x:guid}</c> → Guid, <c>{**x}</c> → iki segment, diğer parametre → "x".</summary>
    private static string Ornek(string sablon)
        => Regex.Replace(sablon, @"\{(?<ad>[^}]+)\}", m =>
        {
            var ad = m.Groups["ad"].Value;
            if (ad.Contains(":guid", StringComparison.OrdinalIgnoreCase)) return G;
            if (ad.StartsWith('*')) return "x/y";
            if (ad.Contains(":int", StringComparison.OrdinalIgnoreCase)) return "1";
            return "x";
        });

    // ------------------------------------------------------------ tek giriş + döngü yokluğu

    [Fact]
    public async Task Oturumsuz_login_SPA_girisine_platform_girisi_etkilenmez()
    {
        var c = fx.Web.Istemci();
        await YonlenirAsync(c, "/login", "/app/giris");
        await YonlenirAsync(c, "/login", "/app/giris", HttpMethod.Head);
        await YonlenirAsync(c, "/login?ReturnUrl=%2Fkiralar%3Fvarac%3Dx", "/app/giris?returnUrl=%2Fkiralar%3Fvarac%3Dx");
        await YonlenirAsync(c, "/login?ReturnUrl=%2F%2Fevil.com", "/app/giris");
        await YonlenirAsync(c, "/login?hata=kapali", "/app/giris?neden=kiraci_kapali");

        var platform = await c.GetAsync("/platform/login");
        Assert.Equal(HttpStatusCode.OK, platform.StatusCode);
        Assert.Null(platform.Headers.Location);

        // Cookie challenge ve AccessDeniedPath DEĞİŞMEDİ (/login, /yetkisiz): challenge dönüşü taşır.
        await YonlenirAsync(c, "/vehicles?x=1", "/login?ReturnUrl=%2Fvehicles%3Fx%3D1");
    }

    [Fact]
    public async Task Oturumlu_login_hedefe_pilot_SPA_pilot_degil_Blazor_platform_konsol()
    {
        var pilot = await OturumAsync(fx.PilotAdmin);
        await YonlenirAsync(pilot, "/login", "/app/panel");
        await YonlenirAsync(pilot, "/login?ReturnUrl=%2Fkiralar%3Fvarac%3Dx", "/app/kiralar?varac=x");
        await YonlenirAsync(pilot, "/login?ReturnUrl=%2Fapp%2Fkiralar%3Fq%3Da", "/app/kiralar?q=a");
        await YonlenirAsync(pilot, "/login?ReturnUrl=%2Fvehicles", "/vehicles");
        await YonlenirAsync(pilot, "/login?ReturnUrl=%2Frezervasyonlar%3Fdurum%3DRezerv", "/app/rezervasyonlar?durum=Rezerv"); // F5.4
        await YonlenirAsync(pilot, "/login?ReturnUrl=%2F%2Fevil.com", "/app/panel");
        await YonlenirAsync(pilot, "/login?ReturnUrl=%2Fapp%2Fgiris", "/app/panel");

        var diger = await OturumAsync(fx.DigerAdmin);
        await YonlenirAsync(diger, "/login", "/");
        await YonlenirAsync(diger, "/login?ReturnUrl=%2Fapp%2Fkiralar", "/");
        await YonlenirAsync(diger, "/login?ReturnUrl=%2Fvehicles%3Fx%3D1", "/vehicles?x=1");

        var platform = await PlatformOturumuAsync();
        await YonlenirAsync(platform, "/login", "/platform/tenants");
    }

    /// <summary>
    /// DÖNGÜ YOK: challenge <c>/login</c>'e, <c>/login</c> <c>/app/giris</c>'e gider; <c>/app/giris</c> (ve /app'in hiçbir
    /// yolu) challenge ALMAZ. Oturumsuz her başlangıç en çok iki adımda /app/giris'te biter (SPA bu host'ta kurulu
    /// değil → 404; önemli olan yönlendirme olmaması).
    /// </summary>
    [Fact]
    public async Task Oturumsuz_zincir_app_giriste_biter_dongu_yok()
    {
        var c = fx.Web.Istemci();
        foreach (var baslangic in new[]
                 {
                     "/", "/kiralar", "/kiralar/yeni?varac=" + G, "/kiralar/" + G + "/yazdir", "/kiralar/" + G + "/pdf",
                     "/vehicles", "/login", "/login?ReturnUrl=%2Flogin", "/app/giris", "/app/giris?returnUrl=%2Fkiralar",
                     "/rezervasyonlar", "/musaitlik?from=2026-10-01", "/filo-kiralama",
                 })
        {
            var zincir = await ZincirAsync(c, baslangic);
            var son = zincir[^1];
            Assert.True(son.Adres.StartsWith("/app/giris", StringComparison.Ordinal),
                $"{baslangic}: zincir /app/giris'te bitmedi: " + string.Join(" → ", zincir.Select(z => $"{z.Adres} ({(int)z.Durum})")));
            Assert.True(zincir.Count <= 3, $"{baslangic}: {zincir.Count} adım");
        }
    }

    [Theory]
    [InlineData("/app/giris")]
    [InlineData("/app/giris?returnUrl=%2Fkiralar%3Fvarac%3Dx")]
    [InlineData("/app/giris?neden=kiraci_kapali")]
    [InlineData("/app/")]
    [InlineData("/app/panel")]
    [InlineData("/app/kiralar/yeni?varac=x")]
    public async Task App_yollari_oturumsuz_challenge_almaz(string url)
    {
        foreach (var yontem in new[] { HttpMethod.Get, HttpMethod.Head })
        {
            using var istek = new HttpRequestMessage(yontem, url);
            var r = await fx.Web.Istemci().SendAsync(istek);
            Assert.Null(r.Headers.Location);
            Assert.NotEqual(HttpStatusCode.Unauthorized, r.StatusCode);
        }
    }

    [Fact]
    public async Task Oturumlu_zincir_dongu_yok()
    {
        var pilot = await OturumAsync(fx.PilotAdmin);
        var z1 = await ZincirAsync(pilot, "/login");
        Assert.Equal(new[] { "/login", "/app/panel" }, z1.Select(z => z.Adres));
        var z2 = await ZincirAsync(pilot, "/");
        Assert.Equal(new[] { "/", "/app/panel" }, z2.Select(z => z.Adres));

        var diger = await OturumAsync(fx.DigerAdmin);
        var z3 = await ZincirAsync(diger, "/login?ReturnUrl=%2Fapp%2Fkiralar");
        Assert.Equal(new[] { "/login?ReturnUrl=%2Fapp%2Fkiralar", "/" }, z3.Select(z => z.Adres));
        Assert.Equal(HttpStatusCode.OK, z3[^1].Durum);
    }

    [Fact]
    public async Task Kapatilan_firmanin_oturumu_SPA_girisine_mesajla_duser_dongu_yok()
    {
        var k = await fx.FirmaVeKullaniciAsync("Kapanacak Pilot Firma");
        var id = await fx.TenantIdAsync(k.Firma);
        await fx.PilotYapAsync(id, true);
        var c = await OturumAsync(k);
        await YonlenirAsync(c, "/kiralar", "/app/kiralar");

        await using (var conn = new NpgsqlConnection(fx.Pg.OwnerConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("UPDATE \"Tenants\" SET \"IsActive\" = false WHERE \"Id\" = @i", conn);
            cmd.Parameters.AddWithValue("i", id);
            await cmd.ExecuteNonQueryAsync();
        }
        using (var scope = fx.Web.Services.CreateScope())
            scope.ServiceProvider.GetRequiredService<TenantStatusCache>().Invalidate(id);

        var zincir = await ZincirAsync(c, "/kiralar");
        Assert.Equal(new[] { "/kiralar", "/login?hata=kapali", "/app/giris?neden=kiraci_kapali" }, zincir.Select(z => z.Adres));
    }

    // ------------------------------------------------------------ platform pilot anahtarı

    [Fact]
    public async Task Platform_pilot_anahtari_acar_kapatir_denetime_yazar_firma_kendisi_acamaz()
    {
        var k = await fx.FirmaVeKullaniciAsync("Anahtar Testi Firması");
        var id = await fx.TenantIdAsync(k.Firma);
        var kullanici = await OturumAsync(k);
        Assert.False(await PilotMuAsync(kullanici));
        Assert.Equal(HttpStatusCode.OK, (await kullanici.GetAsync("/kiralar")).StatusCode);

        // Firma yöneticisi (Admin) anahtara dokunamaz: PlatformAdmin politikası.
        var red = await kullanici.PostAsync("/platform/tenants/yeni-arayuz-pilot", Form(id, true));
        Assert.StartsWith("/platform/login", red.Headers.Location?.OriginalString);
        Assert.False(await PilotMuAsync(kullanici));

        var platform = await PlatformOturumuAsync();
        var detay = await (await platform.GetAsync($"/platform/tenants/{id}")).Content.ReadAsStringAsync();
        Assert.Contains("/platform/tenants/yeni-arayuz-pilot", detay); // anahtar formu detay sayfasında

        var ac = await platform.PostAsync("/platform/tenants/yeni-arayuz-pilot", Form(id, true));
        Assert.Equal($"/platform/tenants/{id}?ok=1", ac.Headers.Location?.OriginalString);
        Assert.True(await PilotMuAsync(kullanici));
        await YonlenirAsync(kullanici, "/kiralar", "/app/kiralar"); // önbelleksiz: ANINDA
        Assert.Equal(1, await DenetimSayisiAsync(id, "true"));

        var tekrar = await platform.PostAsync("/platform/tenants/yeni-arayuz-pilot", Form(id, true)); // no-op
        Assert.Equal($"/platform/tenants/{id}?ok=1", tekrar.Headers.Location?.OriginalString);
        Assert.Equal(1, await DenetimSayisiAsync(id, "true"));

        var kapat = await platform.PostAsync("/platform/tenants/yeni-arayuz-pilot", Form(id, false));
        Assert.Equal($"/platform/tenants/{id}?ok=1", kapat.Headers.Location?.OriginalString);
        Assert.False(await PilotMuAsync(kullanici));
        Assert.Equal(HttpStatusCode.OK, (await kullanici.GetAsync("/kiralar")).StatusCode); // ANINDA eski arayüz
        Assert.Equal(1, await DenetimSayisiAsync(id, "false"));

        var yok = await platform.PostAsync("/platform/tenants/yeni-arayuz-pilot", Form(Guid.NewGuid(), true));
        Assert.Contains("hata=", yok.Headers.Location?.OriginalString);
    }

    private static FormUrlEncodedContent Form(Guid id, bool aktif) => new(new Dictionary<string, string>
    {
        ["id"] = id.ToString(), ["aktif"] = aktif ? "true" : "false",
    });

    private static async Task<bool> PilotMuAsync(HttpClient c)
    {
        var j = JsonDocument.Parse(await c.GetStringAsync("/api/ui/v1/oturum/ben")).RootElement;
        return j.GetProperty("pilot").GetBoolean();
    }

    /// <summary>Firmanın denetim kaydındaki platform pilot satırları (FORCE RLS: tx-yerel tenant GUC ile okunur).</summary>
    private async Task<long> DenetimSayisiAsync(Guid tenantId, string yeniDeger)
    {
        await using var conn = new NpgsqlConnection(fx.Pg.OwnerConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var set = new NpgsqlCommand("SELECT set_config('app.tenant_id', @t, true)", conn, tx))
        {
            set.Parameters.AddWithValue("t", tenantId.ToString());
            await set.ExecuteScalarAsync();
        }
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM \"AuditLogs\" WHERE \"TenantId\" = @t AND \"EntityName\" = 'TenantSettings' " +
            "AND \"UserName\" LIKE 'platform:%' AND \"NewValues\" = CAST(@v AS jsonb)", conn, tx);
        cmd.Parameters.AddWithValue("t", tenantId);
        cmd.Parameters.AddWithValue("v", "{\"YeniArayuzPilot\":" + yeniDeger + "}");
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
