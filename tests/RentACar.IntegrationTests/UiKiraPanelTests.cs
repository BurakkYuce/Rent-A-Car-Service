using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Api;
using RentACar.Web.Api.Kira;
using RentACar.Web.Identity;

namespace RentACar.IntegrationTests;

/// <summary>
/// F4.1 — <c>/api/ui/v1/kiralar/*</c> ve <c>/api/ui/v1/panel/ozet</c> GERÇEK Web boru hattında (cookie + CSRF + pilot).
/// BAĞIMSIZ ORACLE: para beklentileri elle kurulmuş senaryodan (3 gün × 100 net = 300 + %20 KDV 60 = 360 brüt;
/// GPS 50 net + 10 KDV = 60) — servis/motor kodundan TÜRETİLMEZ. Kullanıcılar çalışma anında rastgele parolayla
/// üretilir (depoda kimlik bilgisi yok).
/// </summary>
[Collection("web")]
public sealed class UiKiraPanelTests(WebFixture fx)
{
    private const string V1 = "/api/ui/v1";
    private const string Kira = V1 + "/kiralar";

    // ------------------------------------------------------------ ortam

    private enum Kim { Admin, OperatorA, OperatorB, Muhasebe }

    private sealed class Ortam
    {
        public required Guid TenantId { get; init; }
        public required string Kod { get; init; }
        public required string Sifre { get; init; }
        public required Dictionary<Kim, string> Kullanicilar { get; init; }
        public Guid MusteriId { get; set; }
        public Guid EkHizmetId { get; set; }
    }

    private static string Rastgele(string onek) => onek + Guid.NewGuid().ToString("N")[..10];

    /// <summary>Tam saniyeye hizalı "şimdi" (Mac µs / Linux tick farkı DB eşitliğini bozmasın).</summary>
    private static DateTimeOffset Simdi() => DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    private async Task<Ortam> OrtamKurAsync(bool pilot = true)
    {
        var o = new Ortam
        {
            TenantId = Guid.NewGuid(),
            Kod = Rastgele("f41"),
            Sifre = WebFixture.RastgeleParola(),
            Kullanicilar = Enum.GetValues<Kim>().ToDictionary(k => k, _ => Rastgele("u")),
        };
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.Pg.OwnerConnectionString).Options;
        await using (var db = new AppDbContext(opts, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            db.Tenants.Add(new Tenant { Id = o.TenantId, Code = o.Kod, Name = o.Kod, IsActive = true });
            var hasher = fx.Web.Services.GetRequiredService<IPasswordHasher<User>>();
            foreach (var (kim, ad) in o.Kullanicilar)
            {
                var (rol, sube) = kim switch
                {
                    Kim.Admin => (UserRole.Admin, (string?)null),
                    Kim.OperatorA => (UserRole.Operator, "SubeA"),
                    Kim.OperatorB => (UserRole.Operator, "SubeB"),
                    _ => (UserRole.Muhasebe, null),
                };
                var u = new User { TenantId = o.TenantId, UserName = ad, DisplayName = ad, Rol = rol, AtanmisSube = sube, IsActive = true };
                u.PasswordHash = hasher.HashPassword(u, o.Sifre);
                db.Users.Add(u);
            }
            await db.SaveChangesAsync();
        }
        await fx.PilotYapAsync(o.TenantId, pilot);

        var musteri = new Customer { Tip = CariType.Bireysel, Ad = "Deniz", Soyad = "Yılmaz", CepTel = "05320001122", Email = "deniz@ornek.test" };
        var gps = new EkHizmetTanim { Kod = "GPS", Ad = "Navigasyon", BirimUcret = 50m, KdvOrani = 0.20m, Aktif = true };
        await VeriYazAsync(o.TenantId, db => { db.Customers.Add(musteri); db.EkHizmetTanimlari.Add(gps); });
        o.MusteriId = musteri.Id;
        o.EkHizmetId = gps.Id;
        return o;
    }

    private async Task VeriYazAsync(Guid tenantId, Action<AppDbContext> yaz)
    {
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(tenantId);
        var f = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        yaz(db);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> AracAsync(Ortam o, string sube = "SubeA")
    {
        var v = new Vehicle { Plaka = "34 F41 " + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), Marka = "Fiat", Tip = "Egea", Grup = "C", Sube = sube, Durum = VehicleStatus.Musait, Km = 1000 };
        await VeriYazAsync(o.TenantId, db => db.Vehicles.Add(v));
        return v.Id;
    }

    private sealed record Oturum(HttpClient C, string Xsrf);

    private static string? CerezDegeri(HttpResponseMessage r, string ad)
    {
        if (!r.Headers.TryGetValues("Set-Cookie", out var degerler)) return null;
        foreach (var d in degerler)
            if (d.StartsWith(ad + "=", StringComparison.Ordinal))
                return Uri.UnescapeDataString(d[(ad.Length + 1)..].Split(';')[0]);
        return null;
    }

    private Task<Oturum> GirisAsync(Ortam o, Kim kim) => GirisAsync(o.Kod, o.Kullanicilar[kim], o.Sifre);

    private async Task<Oturum> GirisAsync(string firma, string kullanici, string sifre)
    {
        var c = fx.Web.Istemci();
        var x = await c.GetAsync(V1 + "/oturum/xsrf");
        var once = CerezDegeri(x, "XSRF-TOKEN")!;
        var req = new HttpRequestMessage(HttpMethod.Post, V1 + "/oturum/giris")
        {
            Content = JsonContent.Create(new { firma, kullanici, sifre }),
        };
        req.Headers.Add("X-XSRF-TOKEN", once);
        var r = await c.SendAsync(req);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"giriş başarısız: {await r.Content.ReadAsStringAsync()}");
        return new Oturum(c, CerezDegeri(r, "XSRF-TOKEN")!);
    }

    private static Task<HttpResponseMessage> Gonder(Oturum s, HttpMethod m, string url, object? govde = null)
    {
        var req = new HttpRequestMessage(m, url);
        req.Headers.Add("X-XSRF-TOKEN", s.Xsrf);
        // Low-B: ek hizmet ekleme Idempotency-Key ister; buradaki senaryolar her gönderimi AYRI işlem sayar
        // (mükerrer davranışı LowTemizligiBUiTests'te kilitli).
        if (m == HttpMethod.Post && url.EndsWith("/ek-hizmetler", StringComparison.Ordinal))
            req.Headers.Add("Idempotency-Key", "f41-" + Guid.NewGuid().ToString("N"));
        if (govde is not null) req.Content = JsonContent.Create(govde);
        return s.C.SendAsync(req);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r, HttpStatusCode beklenen = HttpStatusCode.OK)
    {
        var metin = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == beklenen, $"Beklenen {(int)beklenen}, gelen {(int)r.StatusCode}: {metin}");
        return JsonDocument.Parse(metin).RootElement.Clone();
    }

    private static async Task<JsonElement> ProblemBekle(HttpResponseMessage r, HttpStatusCode durum, string kod)
    {
        var metin = await r.Content.ReadAsStringAsync();
        Assert.True(durum == r.StatusCode, $"Beklenen {(int)durum}, gelen {(int)r.StatusCode}: {metin}");
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        var kok = JsonDocument.Parse(metin).RootElement.Clone();
        Assert.Equal(kod, kok.GetProperty("kod").GetString());
        return kok;
    }

    private static decimal Dec(JsonElement e, string ad) => e.GetProperty(ad).GetDecimal();

    /// <summary>API ile yeni kira (3 gün × 100 NET "Günlük" modu, isteğe bağlı GPS).</summary>
    private async Task<Guid> KiraAcAsync(Oturum s, Ortam o, Guid arac, DateTimeOffset bas, int gun = 3, bool gps = false,
        string cikisOfisi = "SubeA")
    {
        var r = await Gonder(s, HttpMethod.Post, Kira, new
        {
            musteriId = o.MusteriId, vehicleId = arac, basTar = bas, bitTar = bas.AddDays(gun),
            gunlukUcret = 100m, fiyatTuru = "Günlük", cikisOfisi, donusOfisi = cikisOfisi,
            ekHizmetler = gps ? new[] { new { tanimId = o.EkHizmetId, miktar = 1m } } : null,
        });
        var j = await Json(r, HttpStatusCode.Created);
        Assert.True(j.GetProperty("uyari").ValueKind == JsonValueKind.Null, j.ToString());
        return j.GetProperty("id").GetGuid();
    }

    // ------------------------------------------------------------ mutlu yol + para oracle

    [Fact]
    public async Task Olustur_teslim_uzat_donus_para_oracle()
    {
        var o = await OrtamKurAsync();
        var arac = await AracAsync(o);
        var s = await GirisAsync(o, Kim.OperatorA);
        var bas = Simdi().AddHours(-1);

        var id = await KiraAcAsync(s, o, arac, bas, gun: 3, gps: true);

        // ORACLE: 3 gün × 100 net = 300; KDV %20 = 60 → baz 360 brüt. GPS: 50 net + 10 KDV = 60. Genel = 420.
        var d = await Json(await s.C.GetAsync($"{Kira}/{id}"));
        var k = d.GetProperty("kira");
        Assert.Equal("Kirada", k.GetProperty("durum").GetString());
        Assert.Equal(3, k.GetProperty("gun").GetInt32());
        Assert.Equal(120m, Dec(k, "gunlukUcret")); // net 100 → brüt 120
        Assert.Equal(360m, Dec(k, "tutar"));
        Assert.Equal(420m, Dec(k, "genelToplam"));
        Assert.Equal(420m, Dec(k, "bakiye"));
        Assert.Equal(0m, Dec(k, "tahsilat"));
        var kalemler = d.GetProperty("ekHizmetler");
        Assert.Equal(1, kalemler.GetArrayLength());
        Assert.Equal(50m, Dec(kalemler[0], "netTutar"));
        Assert.Equal(10m, Dec(kalemler[0], "kdvTutar"));
        Assert.Equal(60m, Dec(kalemler[0], "toplam"));
        Assert.Equal("Deniz Yılmaz", d.GetProperty("musteri").GetProperty("ad").GetString());
        Assert.StartsWith("34 F41", d.GetProperty("arac").GetProperty("plaka").GetString());
        var y = d.GetProperty("yetkiler");
        Assert.True(y.GetProperty("operasyon").GetBoolean());
        Assert.False(y.GetProperty("silme").GetBoolean()); // operatör silemez
        Assert.False(y.GetProperty("finans").GetBoolean());

        // Teslim
        var t = await Json(await Gonder(s, HttpMethod.Post, $"{Kira}/{id}/teslim", new { cikisKm = 1000, cikisYakit = 8 }));
        Assert.Equal(1000, t.GetProperty("cikisKm").GetInt32());
        // İkinci teslim: yapısal red (durum geçişi), 400
        await ProblemBekle(await Gonder(s, HttpMethod.Post, $"{Kira}/{id}/teslim", new { cikisKm = 1000, cikisYakit = 8 }),
            HttpStatusCode.BadRequest, UiHata.Dogrulama);

        // Uzat +1 gün → ORACLE: 4 gün; ek 1 gün × 120 brüt → Tutar 480, Genel 540.
        var yeniBit = bas.AddDays(4);
        var u = await Json(await Gonder(s, HttpMethod.Post, $"{Kira}/{id}/uzat", new { yeniBitTar = yeniBit }));
        Assert.Equal(4, u.GetProperty("gun").GetInt32());
        Assert.Equal(480m, Dec(u, "tutar"));
        Assert.Equal(540m, Dec(u, "genelToplam"));
        Assert.Equal(540m, Dec(u, "bakiye"));

        // Dönüş önizlemesi (persist yok): zamanında, km limitsiz, yakıt aynı → ek bedel 0; genel 540.
        var q = $"donusKm=1300&donusYakit=8&gercekDonus={Uri.EscapeDataString(yeniBit.ToString("O"))}";
        var p = await Json(await s.C.GetAsync($"{Kira}/{id}/donus-hesapla?{q}"));
        Assert.True(p.GetProperty("ok").GetBoolean(), p.ToString());
        Assert.Equal(300, p.GetProperty("kullanilanKm").GetInt32());
        Assert.Equal(0m, Dec(p, "uzatmaBedeli"));
        Assert.Equal(60m, Dec(p, "ekHizmetToplam"));
        Assert.Equal(540m, Dec(p, "yeniGenelToplam"));
        Assert.Equal(540m, Dec(p, "kalan"));
        var hala = await Json(await s.C.GetAsync($"{Kira}/{id}"));
        Assert.Equal("Kirada", hala.GetProperty("kira").GetProperty("durum").GetString());

        // Dönüş
        var r = await Json(await Gonder(s, HttpMethod.Post, $"{Kira}/{id}/donus",
            new { donusKm = 1300, donusYakit = 8, gercekDonus = yeniBit }));
        Assert.Equal("Tamamlandi", r.GetProperty("durum").GetString());
        Assert.Equal(1300, r.GetProperty("donusKm").GetInt32());
        Assert.Equal(540m, Dec(r, "genelToplam"));
        Assert.Equal(540m, Dec(r, "bakiye"));
        Assert.Equal(0, r.GetProperty("uzatmaGun").GetInt32());
    }

    [Fact]
    public async Task Gec_donus_uzatma_bedeli_oracle()
    {
        var o = await OrtamKurAsync();
        var arac = await AracAsync(o);
        var s = await GirisAsync(o, Kim.OperatorA);
        var bas = Simdi().AddDays(-4);
        var id = await KiraAcAsync(s, o, arac, bas, gun: 2);
        await Json(await Gonder(s, HttpMethod.Post, $"{Kira}/{id}/teslim", new { cikisKm = 500, cikisYakit = 8 }));

        // ORACLE: 2 gün × 120 brüt = 240; 25 saat geç → 24 saatlik blok yukarı → 2 gün × 120 = 240 uzatma → 480.
        var gercek = bas.AddDays(2).AddHours(25);
        var r = await Json(await Gonder(s, HttpMethod.Post, $"{Kira}/{id}/donus",
            new { donusKm = 600, donusYakit = 8, gercekDonus = gercek }));
        Assert.Equal(2, r.GetProperty("uzatmaGun").GetInt32());
        Assert.Equal(240m, Dec(r, "uzatmaBedeli"));
        Assert.Equal(480m, Dec(r, "genelToplam"));
    }

    [Fact]
    public async Task Ayni_arac_ayni_tarih_ikinci_kira_409_cakisma()
    {
        var o = await OrtamKurAsync();
        var arac = await AracAsync(o);
        var s = await GirisAsync(o, Kim.Admin);
        var bas = Simdi().AddHours(2);
        await KiraAcAsync(s, o, arac, bas);
        var r = await Gonder(s, HttpMethod.Post, Kira, new
        {
            musteriId = o.MusteriId, vehicleId = arac, basTar = bas.AddDays(1), bitTar = bas.AddDays(2), gunlukUcret = 100m,
        });
        await ProblemBekle(r, HttpStatusCode.Conflict, UiHata.Cakisma);
    }

    // ------------------------------------------------------------ güncelleme (whitelist + required)

    private static readonly string[] GuncelleAlanlari = typeof(KiraGuncelleIstegi)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).ToArray();

    [Fact]
    public async Task Guncelle_tam_govde_yazar_eksik_alan_400_para_alani_tipte_yok()
    {
        var o = await OrtamKurAsync();
        var arac = await AracAsync(o);
        var s = await GirisAsync(o, Kim.OperatorA);
        var id = await KiraAcAsync(s, o, arac, Simdi().AddHours(3));

        var kira = (await Json(await s.C.GetAsync($"{Kira}/{id}"))).GetProperty("kira");
        var govde = new JsonObject();
        foreach (var alan in GuncelleAlanlari)
            govde[alan] = JsonNode.Parse(kira.GetProperty(alan).GetRawText());
        govde["aciklama"] = "F41 not";
        govde["kmLimit"] = 1000;
        govde["fazlaKmUcret"] = 2.5m;
        // Whitelist: gövdeye para/tarih alanı koymak onları DEĞİŞTİRMEZ (tipte yoklar; yok sayılır).
        govde["tutar"] = 1m;
        govde["genelToplam"] = 1m;
        govde["bitTar"] = Simdi().AddDays(30);

        var g = await Json(await Gonder(s, HttpMethod.Put, $"{Kira}/{id}", govde));
        Assert.Equal("F41 not", g.GetProperty("aciklama").GetString());
        Assert.Equal(1000, g.GetProperty("kmLimit").GetInt32());
        Assert.Equal(2.5m, Dec(g, "fazlaKmUcret"));
        Assert.Equal(360m, Dec(g, "tutar"));
        Assert.Equal(360m, Dec(g, "genelToplam"));
        Assert.Equal(kira.GetProperty("bitTar").GetDateTimeOffset(), g.GetProperty("bitTar").GetDateTimeOffset());

        // Eksik alan (kmLimit) → 400: tam değiştirmede unutulan alan sessizce 0'a (sınırsız km) düşmez.
        govde.Remove("kmLimit");
        await ProblemBekle(await Gonder(s, HttpMethod.Put, $"{Kira}/{id}", govde), HttpStatusCode.BadRequest, UiHata.Dogrulama);
        var sonra = (await Json(await s.C.GetAsync($"{Kira}/{id}"))).GetProperty("kira");
        Assert.Equal(1000, sonra.GetProperty("kmLimit").GetInt32());
    }

    // ------------------------------------------------------------ alan bazlı doğrulama

    [Fact]
    public async Task Alan_bazli_400_errors_alan_tasir()
    {
        var o = await OrtamKurAsync();
        var arac = await AracAsync(o);
        var s = await GirisAsync(o, Kim.OperatorA);
        var bas = Simdi().AddHours(-2);
        var id = await KiraAcAsync(s, o, arac, bas);

        async Task Alan(HttpResponseMessage r, string alan)
        {
            var p = await ProblemBekle(r, HttpStatusCode.BadRequest, UiHata.Dogrulama);
            Assert.True(p.TryGetProperty("errors", out var e) && e.TryGetProperty(alan, out _), $"errors.{alan} yok: {p}");
        }

        await Alan(await Gonder(s, HttpMethod.Post, $"{Kira}/{id}/teslim", new { cikisKm = -5, cikisYakit = 8 }), "cikisKm");
        await Json(await Gonder(s, HttpMethod.Post, $"{Kira}/{id}/teslim", new { cikisKm = 1000, cikisYakit = 8 }));
        await Alan(await Gonder(s, HttpMethod.Post, $"{Kira}/{id}/donus",
            new { donusKm = 900, donusYakit = 8, gercekDonus = bas.AddDays(3) }), "donusKm");
        await Alan(await Gonder(s, HttpMethod.Post, $"{Kira}/{id}/donus",
            new { donusKm = 1100, donusYakit = 8, gercekDonus = bas.AddDays(-1) }), "gercekDonus");
        await Alan(await Gonder(s, HttpMethod.Post, $"{Kira}/{id}/uzat", new { yeniBitTar = bas.AddDays(1) }), "yeniBitTar");
        await Alan(await Gonder(s, HttpMethod.Post, Kira, new
        {
            musteriId = o.MusteriId, vehicleId = arac, basTar = bas.AddDays(10), bitTar = bas.AddDays(9), gunlukUcret = 100m,
        }), "bitTar");
        await Alan(await Gonder(s, HttpMethod.Post, $"{Kira}/musteri", new { soyad = "Yalnız" }), "ad");
        await Alan(await s.C.GetAsync($"{Kira}?sirala=olmayanAlan"), "sirala");
        // Kapsam/yetki hataları alan ALMAZ (kodları korunur) — eşleme yalnız düz ValidationException'da.
        Assert.Null(AlanEsleme.Bul("Bu kayıt şube kapsamınız dışında.", [("Dönüş KM", "donusKm")]));
    }

    // ------------------------------------------------------------ yetki + şube kapsamı

    [Fact]
    public async Task Sube_B_operatoru_A_kirasina_ve_alt_kayitlarina_403_listede_gormez()
    {
        var o = await OrtamKurAsync();
        var arac = await AracAsync(o);
        var a = await GirisAsync(o, Kim.OperatorA);
        // Gecikmiş dönüş (bitiş 2 gün önce): panelin "gec" kovasında A görür, B görmez.
        var id = await KiraAcAsync(a, o, arac, Simdi().AddDays(-5), gps: true);
        var kalemId = (await Json(await a.C.GetAsync($"{Kira}/{id}"))).GetProperty("ekHizmetler")[0].GetProperty("id").GetGuid();

        var b = await GirisAsync(o, Kim.OperatorB);
        foreach (var yol in new[] { "", "/faturalar", "/cezalar", "/dis-hizmetler", "/kaynak-rezervasyon", "/donem-plani", "/paylasim",
                     "/donus-hesapla?donusKm=1&donusYakit=1&gercekDonus=2030-01-01T00:00:00Z" })
            await ProblemBekle(await b.C.GetAsync($"{Kira}/{id}{yol}"), HttpStatusCode.Forbidden, UiHata.YetkiYok);
        await ProblemBekle(await b.C.GetAsync($"{Kira}/hesapla?basTar=2030-01-01T00:00:00Z&bitTar=2030-01-02T00:00:00Z&gunlukUcret=100&rentalId={id}"),
            HttpStatusCode.Forbidden, UiHata.YetkiYok);

        await ProblemBekle(await Gonder(b, HttpMethod.Post, $"{Kira}/{id}/teslim", new { cikisKm = 1, cikisYakit = 1 }),
            HttpStatusCode.Forbidden, UiHata.YetkiYok);
        await ProblemBekle(await Gonder(b, HttpMethod.Post, $"{Kira}/{id}/uzat", new { yeniBitTar = Simdi().AddDays(1) }),
            HttpStatusCode.Forbidden, UiHata.YetkiYok);
        await ProblemBekle(await Gonder(b, HttpMethod.Post, $"{Kira}/{id}/provizyon/al"), HttpStatusCode.Forbidden, UiHata.YetkiYok);
        await ProblemBekle(await Gonder(b, HttpMethod.Post, $"{Kira}/{id}/ek-hizmetler", new { ekHizmetTanimId = o.EkHizmetId, miktar = 1m }),
            HttpStatusCode.Forbidden, UiHata.YetkiYok);
        await ProblemBekle(await Gonder(b, HttpMethod.Delete, $"{Kira}/{id}/ek-hizmetler/{kalemId}"), HttpStatusCode.Forbidden, UiHata.YetkiYok);
        await ProblemBekle(await Gonder(b, HttpMethod.Delete, $"{Kira}/{id}/paylasim"), HttpStatusCode.Forbidden, UiHata.YetkiYok);

        var liste = await Json(await b.C.GetAsync(Kira));
        Assert.DoesNotContain(liste.GetProperty("kayitlar").EnumerateArray(), r => r.GetProperty("id").GetGuid() == id);
        var panel = await Json(await b.C.GetAsync(V1 + "/panel/ozet"));
        Assert.DoesNotContain(panel.GetProperty("donusler").GetProperty("gecikmis").EnumerateArray(), r => r.GetProperty("rentalId").GetGuid() == id);
        var panelA = await Json(await a.C.GetAsync(V1 + "/panel/ozet"));
        Assert.Contains(panelA.GetProperty("donusler").GetProperty("gecikmis").EnumerateArray(), r => r.GetProperty("rentalId").GetGuid() == id);

        // A'nın kirası değişmedi (yazma denemeleri hiçbir şey yazmadı).
        var d = (await Json(await a.C.GetAsync($"{Kira}/{id}"))).GetProperty("kira");
        Assert.True(d.GetProperty("cikisKm").ValueKind == JsonValueKind.Null);
        Assert.Equal(420m, Dec(d, "genelToplam"));
        Assert.Contains((await Json(await a.C.GetAsync(Kira))).GetProperty("kayitlar").EnumerateArray(), r => r.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Iptal_dar_izin_operator_403_yonetici_iptal_eder()
    {
        var o = await OrtamKurAsync();
        var arac = await AracAsync(o);
        var op = await GirisAsync(o, Kim.OperatorA);
        var id = await KiraAcAsync(op, o, arac, Simdi().AddHours(5));

        await ProblemBekle(await Gonder(op, HttpMethod.Post, $"{Kira}/{id}/iptal"), HttpStatusCode.Forbidden, UiHata.YetkiYok);
        Assert.Equal("Kirada", (await Json(await op.C.GetAsync($"{Kira}/{id}"))).GetProperty("kira").GetProperty("durum").GetString());

        var ad = await GirisAsync(o, Kim.Admin);
        var r = await Json(await Gonder(ad, HttpMethod.Post, $"{Kira}/{id}/iptal"));
        Assert.Equal("Iptal", r.GetProperty("durum").GetString());
        // İkinci iptal: yapısal 400 (durum geçişi)
        await ProblemBekle(await Gonder(ad, HttpMethod.Post, $"{Kira}/{id}/iptal"), HttpStatusCode.BadRequest, UiHata.Dogrulama);
    }

    [Fact]
    public async Task Muhasebe_okur_ama_operasyon_yazamaz_karne_yalniz_finans()
    {
        var o = await OrtamKurAsync();
        var arac = await AracAsync(o);
        var op = await GirisAsync(o, Kim.OperatorA);
        var id = await KiraAcAsync(op, o, arac, Simdi().AddHours(6));

        var mu = await GirisAsync(o, Kim.Muhasebe);
        var d = await Json(await mu.C.GetAsync($"{Kira}/{id}"));
        Assert.True(d.GetProperty("yetkiler").GetProperty("finans").GetBoolean());
        Assert.False(d.GetProperty("yetkiler").GetProperty("operasyon").GetBoolean());
        Assert.True(d.GetProperty("paylasim").ValueKind == JsonValueKind.Null); // paylaşım barı yalnız OperationsWrite
        await Json(await mu.C.GetAsync($"{Kira}/{id}/faturalar"));
        await Json(await mu.C.GetAsync($"{Kira}/{id}/karne-ozeti"));
        await ProblemBekle(await Gonder(mu, HttpMethod.Post, $"{Kira}/{id}/teslim", new { cikisKm = 1, cikisYakit = 1 }),
            HttpStatusCode.Forbidden, UiHata.YetkiYok);
        await ProblemBekle(await mu.C.GetAsync($"{Kira}/hesapla?basTar=2030-01-01T00:00:00Z&bitTar=2030-01-02T00:00:00Z"),
            HttpStatusCode.Forbidden, UiHata.YetkiYok);

        await ProblemBekle(await op.C.GetAsync($"{Kira}/{id}/karne-ozeti"), HttpStatusCode.Forbidden, UiHata.YetkiYok);
    }

    [Fact]
    public async Task Pilot_olmayan_firma_403_pilot_degil()
    {
        var o = await OrtamKurAsync(pilot: false);
        var s = await GirisAsync(o, Kim.Admin);
        await ProblemBekle(await s.C.GetAsync(Kira), HttpStatusCode.Forbidden, UiHata.PilotDegil);
        await ProblemBekle(await s.C.GetAsync(V1 + "/panel/ozet"), HttpStatusCode.Forbidden, UiHata.PilotDegil);
        await ProblemBekle(await Gonder(s, HttpMethod.Post, Kira, new { musteriId = o.MusteriId, vehicleId = Guid.NewGuid(),
            basTar = Simdi(), bitTar = Simdi().AddDays(1) }), HttpStatusCode.Forbidden, UiHata.PilotDegil);
    }

    [Fact]
    public async Task Baska_kiracinin_kirasi_404()
    {
        var o = await OrtamKurAsync();
        var arac = await AracAsync(o);
        var id = await KiraAcAsync(await GirisAsync(o, Kim.Admin), o, arac, Simdi().AddHours(7));
        var diger = await OrtamKurAsync();
        var s = await GirisAsync(diger, Kim.Admin);
        var r = await s.C.GetAsync($"{Kira}/{id}");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.C.GetAsync($"{Kira}/{id}/faturalar")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Gonder(s, HttpMethod.Post, $"{Kira}/{id}/iptal")).StatusCode);
    }

    // ------------------------------------------------------------ canlı hesap paritesi

    [Fact]
    public async Task Hesapla_Blazor_ucuyla_birebir_ve_oracle()
    {
        var o = await OrtamKurAsync();
        var arac = await AracAsync(o);
        var s = await GirisAsync(o, Kim.OperatorA);
        var bas = Simdi().AddDays(2);
        var q = $"vehicleId={arac}&basTar={Uri.EscapeDataString(bas.ToString("O"))}&bitTar={Uri.EscapeDataString(bas.AddDays(3).ToString("O"))}" +
                $"&gunlukUcret=100&fiyatTuru={Uri.EscapeDataString("Günlük")}&musteriId={o.MusteriId}&ek={o.EkHizmetId}:1";

        var api = await Json(await s.C.GetAsync($"{Kira}/hesapla?{q}"));
        var blazorYanit = await s.C.GetAsync($"/kiralar/hesapla?{q}");
        Assert.Equal(HttpStatusCode.OK, blazorYanit.StatusCode);
        var blazor = JsonDocument.Parse(await blazorYanit.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(blazor.GetRawText(), api.GetRawText()); // aynı motor, aynı JSON

        // ORACLE: 3 × 100 net = 300; KDV 60; baz 360. GPS 60. Genel 420.
        Assert.True(api.GetProperty("ok").GetBoolean(), api.ToString());
        Assert.Equal(3, api.GetProperty("gun").GetInt32());
        Assert.Equal(300m, Dec(api, "net"));
        Assert.Equal(60m, Dec(api, "kdv"));
        Assert.Equal(360m, Dec(api, "tutar"));
        Assert.Equal(60m, Dec(api, "ekHizmetToplam"));
        Assert.Equal(420m, Dec(api, "genelToplam"));
        Assert.Equal(420m, Dec(api, "kalan"));

        // Bozuk ek biçimi: Blazor sessiz atlar, API 400 (alan: ek)
        var bozuk = await s.C.GetAsync($"{Kira}/hesapla?basTar={Uri.EscapeDataString(bas.ToString("O"))}&bitTar={Uri.EscapeDataString(bas.AddDays(1).ToString("O"))}&ek=bozuk");
        var p = await ProblemBekle(bozuk, HttpStatusCode.BadRequest, UiHata.Dogrulama);
        Assert.True(p.GetProperty("errors").TryGetProperty("ek", out _));
    }

    // ------------------------------------------------------------ TahsilatAnahtar

    private static readonly Regex IslemAnahtari = new("name=\"islemAnahtari\" value=\"([0-9a-fA-F-]{36})\"", RegexOptions.Compiled);

    [Fact]
    public async Task Tahsilat_anahtari_liste_ve_panelde_Blazor_ile_ayni_islem_sonrasi_degisir_tekrari_mukerrer()
    {
        var o = await OrtamKurAsync();
        var arac = await AracAsync(o);
        var ad = await GirisAsync(o, Kim.Admin);
        // Gecikmiş dönüş: panelde "gec" kovasına düşer, Blazor Home varsayılan olarak o sekmeyi açar.
        var id = await KiraAcAsync(ad, o, arac, Simdi().AddDays(-5), gun: 3);

        async Task<JsonElement> ListeSatiri(Oturum s)
            => (await Json(await s.C.GetAsync(Kira))).GetProperty("kayitlar").EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id);

        // F4.6: pilot firmada Blazor /kiralar ve / yeni arayüze yönlenir (IlkKesisMiddleware). Blazor paritesini
        // ölçmek için sayfa pilot KAPALIYKEN okunur (bayrak önbelleksiz — bir sonraki istekte geçerli), sonra açılır.
        async Task<string> BlazorHtml(Oturum s, string yol)
        {
            var yon = await s.C.GetAsync(yol);
            Assert.Equal(HttpStatusCode.Redirect, yon.StatusCode);
            Assert.StartsWith("/app/", yon.Headers.Location?.OriginalString);
            await fx.PilotYapAsync(o.TenantId, false);
            try { return await (await s.C.GetAsync(yol)).Content.ReadAsStringAsync(); }
            finally { await fx.PilotYapAsync(o.TenantId, true); }
        }

        var satir = await ListeSatiri(ad);
        var th = satir.GetProperty("tahsilat");
        var k1 = th.GetProperty("anahtar").GetGuid();
        Assert.Equal(o.MusteriId, th.GetProperty("cariId").GetGuid());
        Assert.Equal(id, th.GetProperty("rentalId").GetGuid());
        Assert.Equal("TRY", th.GetProperty("doviz").GetString());
        Assert.Equal(360m, Dec(th, "varsayilanTutar"));

        // Blazor kira listesi ve panosu AYNI anahtarı basıyor.
        var listeHtml = await BlazorHtml(ad, "/kiralar");
        Assert.Contains(k1.ToString(), IslemAnahtari.Matches(listeHtml).Select(m => m.Groups[1].Value));
        var panoHtml = await BlazorHtml(ad, "/");
        Assert.Contains(k1.ToString(), IslemAnahtari.Matches(panoHtml).Select(m => m.Groups[1].Value));

        var panel = await Json(await ad.C.GetAsync(V1 + "/panel/ozet"));
        var donus = panel.GetProperty("donusler");
        Assert.Equal("gec", donus.GetProperty("varsayilanSekme").GetString());
        var ps = donus.GetProperty("gecikmis").EnumerateArray().Single(r => r.GetProperty("rentalId").GetGuid() == id);
        Assert.Equal(k1, ps.GetProperty("tahsilat").GetProperty("anahtar").GetGuid());
        Assert.True(panel.GetProperty("finans").ValueKind == JsonValueKind.Object);

        // Anahtar gerçek idempotency anahtarı: onunla tahsilat → ikinci gönderim 409 mükerrer (E01).
        using (var host = new TestHost(fx.Pg.AppConnectionString))
        {
            using (var scope = host.ScopeFor(o.TenantId))
                await scope.ServiceProvider.GetRequiredService<CashService>()
                    .CollectAsync(new CashInput { CariId = o.MusteriId, RentalId = id, Tutar = 100m, IslemAnahtari = k1 });
            using (var scope = host.ScopeFor(o.TenantId))
                await Assert.ThrowsAsync<MukerrerIslemException>(() => scope.ServiceProvider.GetRequiredService<CashService>()
                    .CollectAsync(new CashInput { CariId = o.MusteriId, RentalId = id, Tutar = 100m, IslemAnahtari = k1 }));
        }

        // Tahsilat sonrası: bakiye 360 − 100 = 260, işlem sayısı arttı → YENİ anahtar (ikinci meşru tahsilat engellenmez).
        var sonra = await ListeSatiri(ad);
        Assert.Equal(260m, Dec(sonra, "bakiye"));
        var k2 = sonra.GetProperty("tahsilat").GetProperty("anahtar").GetGuid();
        Assert.NotEqual(k1, k2);
        var listeHtml2 = await BlazorHtml(ad, "/kiralar");
        Assert.Contains(k2.ToString(), IslemAnahtari.Matches(listeHtml2).Select(m => m.Groups[1].Value));

        // Finans yetkisi olmayan operatörde tahsilat verisi ve finans özeti YOK.
        var op = await GirisAsync(o, Kim.OperatorA);
        Assert.True((await ListeSatiri(op)).GetProperty("tahsilat").ValueKind == JsonValueKind.Null);
        var opPanel = await Json(await op.C.GetAsync(V1 + "/panel/ozet"));
        Assert.True(opPanel.GetProperty("finans").ValueKind == JsonValueKind.Null);
        Assert.True(opPanel.GetProperty("donusler").GetProperty("gecikmis").EnumerateArray()
            .Single(r => r.GetProperty("rentalId").GetGuid() == id).GetProperty("tahsilat").ValueKind == JsonValueKind.Null);
    }

    // ------------------------------------------------------------ liste, ek hizmet, müşteri, paylaşım, varsayılanlar

    [Fact]
    public async Task Liste_sayfalama_siralama_filtre()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Admin);
        var bas = Simdi().AddDays(10);
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++) ids.Add(await KiraAcAsync(s, o, await AracAsync(o), bas.AddDays(i * 7), gun: i + 1));

        var s1 = await Json(await s.C.GetAsync($"{Kira}?boyut=2&sayfa=1&sirala=-tutar"));
        Assert.Equal(3, s1.GetProperty("toplam").GetInt32());
        var tutarlar = s1.GetProperty("kayitlar").EnumerateArray().Select(r => Dec(r, "tutar")).ToList();
        Assert.Equal([360m, 240m], tutarlar); // 3×120, 2×120 (azalan)
        var s2 = await Json(await s.C.GetAsync($"{Kira}?boyut=2&sayfa=2&sirala=-tutar"));
        Assert.Equal([120m], s2.GetProperty("kayitlar").EnumerateArray().Select(r => Dec(r, "tutar")).ToList());

        await Json(await Gonder(s, HttpMethod.Post, $"{Kira}/{ids[0]}/iptal"));
        var kirada = await Json(await s.C.GetAsync($"{Kira}?durum=Kirada"));
        Assert.Equal(2, kirada.GetProperty("toplam").GetInt32());
        Assert.Equal(1, (await Json(await s.C.GetAsync($"{Kira}?durum=iptal"))).GetProperty("toplam").GetInt32());
        // Sayı ya da tanımsız ad sessizce "filtre yok"a düşmez: 400 + alan.
        foreach (var bozuk in new[] { "1", "Yok" })
        {
            var p = await ProblemBekle(await s.C.GetAsync($"{Kira}?durum={bozuk}"), HttpStatusCode.BadRequest, UiHata.Dogrulama);
            Assert.True(p.GetProperty("errors").TryGetProperty("durum", out _));
        }
        var ozet = await Json(await s.C.GetAsync($"{Kira}/ozet"));
        Assert.Equal(3, ozet.GetProperty("toplam").GetInt32());
        Assert.Equal(2, ozet.GetProperty("kirada").GetInt32());
    }

    /// <summary>F4.2 — SPA listesinin dışa aktarması (<c>/listeler/export/kiralar</c>) ekrandaki süzgeci taşır;
    /// parametresiz çağrı (Blazor bağlantısı) eskisi gibi hepsi. Oracle: 3 kira açıldı, 1'i iptal edildi.</summary>
    [Fact]
    public async Task Export_kiralar_ekrandaki_suzgeci_tasir_parametresiz_hepsi()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Admin);
        var bas = Simdi().AddDays(40);
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++) ids.Add(await KiraAcAsync(s, o, await AracAsync(o), bas.AddDays(i * 7)));
        await Json(await Gonder(s, HttpMethod.Post, $"{Kira}/{ids[0]}/iptal"));

        static async Task<int> VeriSatiriAsync(HttpResponseMessage r)
        {
            var metin = await r.Content.ReadAsStringAsync();
            Assert.True(r.StatusCode == HttpStatusCode.OK, $"{(int)r.StatusCode}: {metin}");
            Assert.Equal("text/csv", r.Content.Headers.ContentType?.MediaType);
            return metin.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length - 1;
        }

        const string Uc = "/listeler/export/kiralar?format=csv";
        Assert.Equal(3, await VeriSatiriAsync(await s.C.GetAsync(Uc)));
        Assert.Equal(1, await VeriSatiriAsync(await s.C.GetAsync(Uc + "&durum=Iptal")));
        // Sayfa taşınmaz: dosya filtreye uyan TÜM kayıtlar (2 kirada), ekrandaki sayfa değil.
        Assert.Equal(2, await VeriSatiriAsync(await s.C.GetAsync(Uc + "&durum=Kirada&sayfa=2&boyut=1")));
        // API ile aynı gün kuralı: son kiranın başlangıç günü ve sonrası → 1.
        var sonGun = bas.AddDays(14).ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd");
        Assert.Equal(1, await VeriSatiriAsync(await s.C.GetAsync(Uc + "&basMin=" + sonGun)));

        // Tanımsız durum adı sessizce "hepsi"ne düşmez (yanlış dosya indirilmez): doğrulama hatası sayfası.
        var bozuk = await s.C.GetAsync(Uc + "&durum=Yok");
        Assert.Equal(HttpStatusCode.Redirect, bozuk.StatusCode);
        Assert.StartsWith("/hata", bozuk.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Ek_hizmet_ekle_sil_toplam_ve_yabanci_kalem_404()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.OperatorA);
        var id = await KiraAcAsync(s, o, await AracAsync(o), Simdi().AddHours(8));
        var baska = await KiraAcAsync(s, o, await AracAsync(o), Simdi().AddHours(8), gps: true);

        // ORACLE: 2 × GPS (50 net) = 100 net + 20 KDV = 120 → genel 360 + 120 = 480.
        var e = await Json(await Gonder(s, HttpMethod.Post, $"{Kira}/{id}/ek-hizmetler", new { ekHizmetTanimId = o.EkHizmetId, miktar = 2m }));
        Assert.Equal(120m, Dec(e.GetProperty("kalemler")[0], "toplam"));
        Assert.Equal(480m, Dec(e.GetProperty("kira"), "genelToplam"));
        var kalem = e.GetProperty("kalemler")[0].GetProperty("id").GetGuid();

        // Başka kiranın kalemi bu rotadan silinemez.
        var yabanci = (await Json(await s.C.GetAsync($"{Kira}/{baska}"))).GetProperty("ekHizmetler")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await Gonder(s, HttpMethod.Delete, $"{Kira}/{id}/ek-hizmetler/{yabanci}")).StatusCode);
        Assert.Equal(420m, Dec((await Json(await s.C.GetAsync($"{Kira}/{baska}"))).GetProperty("kira"), "genelToplam"));

        var sil = await Json(await Gonder(s, HttpMethod.Delete, $"{Kira}/{id}/ek-hizmetler/{kalem}"));
        Assert.Equal(0, sil.GetProperty("kalemler").GetArrayLength());
        Assert.Equal(360m, Dec(sil.GetProperty("kira"), "genelToplam"));
    }

    [Fact]
    public async Task Sistem_ucret_kalemi_manuel_eklenemez_silinemez()
    {
        var o = await OrtamKurAsync();
        var sys = new EkHizmetTanim { Kod = "SYS-DROP", Ad = "Drop ücreti", BirimUcret = 100m, KdvOrani = 0.20m, Aktif = true };
        await VeriYazAsync(o.TenantId, db => db.EkHizmetTanimlari.Add(sys));
        var s = await GirisAsync(o, Kim.Admin);
        var arac = await AracAsync(o);
        var bas = Simdi().AddDays(60);

        // Kira açılmadan temiz red (yarım kayıt yok).
        await ProblemBekle(await Gonder(s, HttpMethod.Post, Kira, new
        {
            musteriId = o.MusteriId, vehicleId = arac, basTar = bas, bitTar = bas.AddDays(1), gunlukUcret = 100m,
            ekHizmetler = new[] { new { tanimId = sys.Id, miktar = 1m } },
        }), HttpStatusCode.BadRequest, UiHata.Dogrulama);
        Assert.Equal(0, (await Json(await s.C.GetAsync(Kira))).GetProperty("toplam").GetInt32());

        // Sistem satırı (FeeLineService'in yazdığı gibi) varsa bu uçtan silinemez; toplam korunur.
        var id = await KiraAcAsync(s, o, arac, bas, gun: 1);
        using (var host = new TestHost(fx.Pg.AppConnectionString))
        using (var scope = host.ScopeFor(o.TenantId))
            await scope.ServiceProvider.GetRequiredService<RentACar.Application.RentalAddOns.RentalAddOnService>()
                .AddAsync(id, sys.Id, 1m, sistem: true);
        var d = await Json(await s.C.GetAsync($"{Kira}/{id}"));
        var genel = Dec(d.GetProperty("kira"), "genelToplam");
        Assert.Equal(120m + 120m, genel); // 1 × 120 brüt + SYS 100 net + 20 KDV
        var kalem = d.GetProperty("ekHizmetler")[0].GetProperty("id").GetGuid();
        await ProblemBekle(await Gonder(s, HttpMethod.Delete, $"{Kira}/{id}/ek-hizmetler/{kalem}"), HttpStatusCode.BadRequest, UiHata.Dogrulama);
        Assert.Equal(genel, Dec((await Json(await s.C.GetAsync($"{Kira}/{id}"))).GetProperty("kira"), "genelToplam"));
    }

    private static string GecerliTc()
    {
        var d = new int[11];
        d[0] = Random.Shared.Next(1, 10);
        for (var i = 1; i < 9; i++) d[i] = Random.Shared.Next(0, 10);
        var tek = d[0] + d[2] + d[4] + d[6] + d[8];
        var cift = d[1] + d[3] + d[5] + d[7];
        d[9] = (((tek * 7) - cift) % 10 + 10) % 10;
        d[10] = d.Take(10).Sum() % 10;
        return string.Concat(d);
    }

    [Fact]
    public async Task Hizli_musteri_yalniz_kimlik_ve_etiket_doner_tc_tekrari_409()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.OperatorA);
        var tc = GecerliTc();
        var r = await Gonder(s, HttpMethod.Post, $"{Kira}/musteri", new { ad = "Can", soyad = "Er", tcKimlik = tc, cepTel = "05551234567", ehliyetNo = "EHL-99" });
        var metin = await r.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = JsonDocument.Parse(metin).RootElement;
        Assert.Equal(["etiket", "id"], j.EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.Equal("Can Er", j.GetProperty("etiket").GetString());
        Assert.DoesNotContain(tc, metin);
        Assert.DoesNotContain("EHL-99", metin);
        Assert.DoesNotContain("05551234567", metin);

        await ProblemBekle(await Gonder(s, HttpMethod.Post, $"{Kira}/musteri", new { ad = "Başka", tcKimlik = tc }),
            HttpStatusCode.Conflict, UiHata.Cakisma);
    }

    [Fact]
    public async Task Provizyon_al_kapat_ve_form_varsayilanlari_musait_arac()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.OperatorA);
        var arac = await AracAsync(o);
        var bas = Simdi().AddDays(40);
        var r = await Gonder(s, HttpMethod.Post, Kira, new
        {
            musteriId = o.MusteriId, vehicleId = arac, basTar = bas, bitTar = bas.AddDays(2), gunlukUcret = 100m, provizyon = 500m,
            cikisOfisi = "SubeA",
        });
        var id = (await Json(r, HttpStatusCode.Created)).GetProperty("id").GetGuid();

        var al = await Json(await Gonder(s, HttpMethod.Post, $"{Kira}/{id}/provizyon/al"));
        Assert.Equal("Alindi", al.GetProperty("provizyonDurum").GetString());
        await ProblemBekle(await Gonder(s, HttpMethod.Post, $"{Kira}/{id}/provizyon/al"), HttpStatusCode.BadRequest, UiHata.Dogrulama);
        var kapa = await Json(await Gonder(s, HttpMethod.Post, $"{Kira}/{id}/provizyon/kapat", new { kapamaTutar = 300m }));
        Assert.Equal("Kapandi", kapa.GetProperty("provizyonDurum").GetString());
        Assert.Equal(300m, Dec(kapa, "provizyonKapamaTutar"));
        Assert.Equal(200m, Dec(kapa, "bakiye")); // provizyon deftere/bakiyeye yazmaz: 2 × 100 brüt (varsayılan mod)

        var v = await Json(await s.C.GetAsync($"{Kira}/form-varsayilanlari"));
        Assert.Equal(8, v.GetProperty("cikisYakit").GetInt32());
        Assert.Contains("Günlük", v.GetProperty("fiyatTurleri").EnumerateArray().Select(x => x.GetString()));

        // Müsait araç: kiralı araç o aralıkta yok, boş araç var.
        var bos = await AracAsync(o);
        var gun = DateOnly.FromDateTime(bas.UtcDateTime);
        var m = await Json(await s.C.GetAsync($"{Kira}/musait-arac?vfrom={gun:yyyy-MM-dd}&vto={gun.AddDays(1):yyyy-MM-dd}"));
        var idler = m.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(bos, idler);
        Assert.DoesNotContain(arac, idler);
        await ProblemBekle(await s.C.GetAsync($"{Kira}/musait-arac?vfrom={gun:yyyy-MM-dd}&vto={gun:yyyy-MM-dd}"),
            HttpStatusCode.BadRequest, UiHata.Dogrulama);
    }

    [Fact]
    public async Task Paylasim_iptal_kapsam_kapisindan_gecer_ve_link_yoksa_false()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Admin);
        var id = await KiraAcAsync(s, o, await AracAsync(o), Simdi().AddHours(9));
        var d = await Json(await s.C.GetAsync($"{Kira}/{id}/paylasim"));
        Assert.True(d.GetProperty("link").ValueKind == JsonValueKind.Null);
        var iptal = await Json(await Gonder(s, HttpMethod.Delete, $"{Kira}/{id}/paylasim"));
        Assert.False(iptal.GetProperty("iptalEdildi").GetBoolean());
        var paylas = await Json(await Gonder(s, HttpMethod.Post, $"{Kira}/{id}/paylasim"));
        var yol = paylas.GetProperty("link").GetProperty("yol").GetString();
        Assert.StartsWith("/sozlesme/", yol);
        // Paylaş tekrarı AYNI linki döner (müşterinin elindeki adres bozulmaz).
        Assert.Equal(yol, (await Json(await Gonder(s, HttpMethod.Post, $"{Kira}/{id}/paylasim"))).GetProperty("link").GetProperty("yol").GetString());
        Assert.True((await Json(await Gonder(s, HttpMethod.Delete, $"{Kira}/{id}/paylasim"))).GetProperty("iptalEdildi").GetBoolean());
    }

    // ------------------------------------------------------------ izin haritası (yapısal)

    private List<RouteEndpoint> KiraPanelUclari()
        => fx.Web.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => ("/" + (e.RoutePattern.RawText ?? "").TrimStart('/')) is var r
                        && (r.StartsWith(Kira, StringComparison.Ordinal) || r == V1 + "/panel/ozet"))
            .ToList();

    [Fact]
    public void Izin_haritasi_Blazor_ile_ayni()
    {
        static string Anahtar(RouteEndpoint e)
            => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Single() ?? "?") + " " + "/" + e.RoutePattern.RawText!.Trim('/');
        var harita = KiraPanelUclari().ToDictionary(Anahtar, e => e);

        string Etkin(string anahtar)
        {
            var e = harita[anahtar];
            if (e.Metadata.GetMetadata<IzinMuafMetadata>() is not null && e.Metadata.GetMetadata<IzinMetadata>() is null) return "muaf";
            return e.Metadata.GetMetadata<IzinMetadata>()?.Izin.ToString()
                   ?? string.Join("|", e.Metadata.GetMetadata<IzinlerdenBiriMetadata>()!.Izinler);
        }

        const string Oku = "OperationsWrite|FinanceWrite";
        var beklenen = new Dictionary<string, string>
        {
            ["GET " + Kira] = Oku,
            ["GET " + Kira + "/ozet"] = Oku,
            ["GET " + Kira + "/filtre-secenekleri"] = Oku,
            ["GET " + Kira + "/{id:guid}"] = Oku,
            ["GET " + Kira + "/{id:guid}/faturalar"] = Oku,
            ["GET " + Kira + "/{id:guid}/cezalar"] = Oku,
            ["GET " + Kira + "/{id:guid}/dis-hizmetler"] = Oku,
            ["GET " + Kira + "/{id:guid}/kaynak-rezervasyon"] = Oku,
            ["GET " + Kira + "/{id:guid}/karne-ozeti"] = "FinanceWrite",
            ["GET " + Kira + "/{id:guid}/musteri-ozet"] = Oku, // F4.3b
            ["GET " + Kira + "/ek-hizmet-katalogu"] = "OperationsWrite", // F4.3b
            ["GET " + Kira + "/form-varsayilanlari"] = "OperationsWrite",
            ["GET " + Kira + "/hesapla"] = "OperationsWrite",
            ["GET " + Kira + "/musait-arac"] = "OperationsWrite",
            ["GET " + Kira + "/{id:guid}/donus-hesapla"] = "OperationsWrite",
            ["GET " + Kira + "/{id:guid}/donem-plani"] = "OperationsWrite",
            ["GET " + Kira + "/{id:guid}/paylasim"] = "OperationsWrite",
            ["POST " + Kira] = "OperationsWrite",
            ["POST " + Kira + "/musteri"] = "OperationsWrite",
            ["PUT " + Kira + "/{id:guid}"] = "OperationsWrite",
            ["POST " + Kira + "/{id:guid}/teslim"] = "OperationsWrite",
            ["POST " + Kira + "/{id:guid}/donus"] = "OperationsWrite",
            ["POST " + Kira + "/{id:guid}/uzat"] = "OperationsWrite",
            ["POST " + Kira + "/{id:guid}/iptal"] = "OperationsDelete",
            ["POST " + Kira + "/{id:guid}/provizyon/al"] = "OperationsWrite",
            ["POST " + Kira + "/{id:guid}/provizyon/kapat"] = "OperationsWrite",
            ["POST " + Kira + "/{id:guid}/ek-hizmetler"] = "OperationsWrite",
            ["DELETE " + Kira + "/{id:guid}/ek-hizmetler/{kalemId:guid}"] = "OperationsWrite",
            ["POST " + Kira + "/{id:guid}/paylasim"] = "OperationsWrite",
            ["POST " + Kira + "/{id:guid}/paylasim/yeni-surum"] = "OperationsWrite",
            ["DELETE " + Kira + "/{id:guid}/paylasim"] = "OperationsWrite",
            ["GET " + V1 + "/panel/ozet"] = "muaf",
        };
        Assert.Equal(beklenen.Keys.OrderBy(x => x, StringComparer.Ordinal), harita.Keys.OrderBy(x => x, StringComparer.Ordinal));
        foreach (var (anahtar, izin) in beklenen)
            Assert.True(izin == Etkin(anahtar), $"{anahtar}: beklenen {izin}, gelen {Etkin(anahtar)}");

        // Yazma uçları OKUMA kapısını da taşır (grup) — "izinlerden biri" + dar izin VE ile birleşir.
        Assert.All(harita.Where(kv => kv.Key.Contains("/kiralar")).Select(kv => kv.Value),
            e => Assert.NotNull(e.Metadata.GetMetadata<IzinlerdenBiriMetadata>()));
    }

    [Fact]
    public void Alan_eslemesi_onek_ve_sira()
    {
        (string, string)[] kurallar = [("Dönüş KM", "donusKm"), ("Dönüş", "genel")];
        Assert.Equal("donusKm", AlanEsleme.Bul("Dönüş KM, çıkış KM'den küçük olamaz.", kurallar));
        Assert.Equal("genel", AlanEsleme.Bul("Dönüş tarihi başlangıçtan önce olamaz.", kurallar));
        Assert.Null(AlanEsleme.Bul("Başka bir hata.", kurallar));
        Assert.Throws<ArgumentException>(() => new RouteGroupBuilderStub().RequireAnyPermission(Permission.OperationsWrite));
    }

    // ================================================================== F4.1 adversarial kapanışları
    // Bağımsız inceleyicinin probe'ları (AdvF41ProbeTests P3/P4/P6/P7/P8/P9/P10/P11/P12) kalıcı teste çevrildi.

    private async Task<int> KiraSayisiAsync(Guid tenantId)
    {
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(tenantId);
        await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        return await db.Rentals.CountAsync();
    }

    private static async Task AlanBekle(HttpResponseMessage r, string alan)
    {
        var p = await ProblemBekle(r, HttpStatusCode.BadRequest, UiHata.Dogrulama);
        Assert.True(p.TryGetProperty("errors", out var e) && e.TryGetProperty(alan, out _), $"errors.{alan} yok: {p}");
    }

    [Fact]
    public async Task M1_Operator_baska_subeye_ve_ofissiz_kira_yazamaz()
    {
        var o = await OrtamKurAsync();
        var aracB = await AracAsync(o, "SubeB");
        var a = await GirisAsync(o, Kim.OperatorA);
        var b = await GirisAsync(o, Kim.OperatorB);
        var bas = Simdi().AddDays(10);

        // A, B şubesinin ofisiyle kira açamaz: 403, HİÇBİR ŞEY yazılmaz (B'nin aracı bloke olmaz).
        await ProblemBekle(await Gonder(a, HttpMethod.Post, Kira, new
        {
            musteriId = o.MusteriId, vehicleId = aracB, basTar = bas, bitTar = bas.AddDays(3), gunlukUcret = 1m, cikisOfisi = "SubeB",
        }), HttpStatusCode.Forbidden, UiHata.YetkiYok);
        // Şubeye bağlı operatör ofissiz ("yetim") kira açamaz: 400 errors.cikisOfisi.
        await AlanBekle(await Gonder(a, HttpMethod.Post, Kira, new
        {
            musteriId = o.MusteriId, vehicleId = aracB, basTar = bas, bitTar = bas.AddDays(3), gunlukUcret = 100m,
        }), "cikisOfisi");
        Assert.Equal(0, await KiraSayisiAsync(o.TenantId));

        // B kendi aracını aynı tarihte kiralayabilir (A'nın denemesi bloke etmedi).
        await Json(await Gonder(b, HttpMethod.Post, Kira, new
        {
            musteriId = o.MusteriId, vehicleId = aracB, basTar = bas.AddDays(1), bitTar = bas.AddDays(2), gunlukUcret = 100m, cikisOfisi = "SubeB",
        }), HttpStatusCode.Created);

        // Kapsamsız rol (Admin) ofissiz açabilir (mevcut davranış — kapsam yok).
        var ad = await GirisAsync(o, Kim.Admin);
        await Json(await Gonder(ad, HttpMethod.Post, Kira, new
        {
            musteriId = o.MusteriId, vehicleId = await AracAsync(o), basTar = bas, bitTar = bas.AddDays(1), gunlukUcret = 100m,
        }), HttpStatusCode.Created);

        // Kök serviste: Blazor /kiralar/create ve harici API de aynı RentalService.CreateDirectAsync'ten geçer.
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(o.TenantId, Guid.NewGuid(), "op", UserRole.Operator, "SubeA");
        await Assert.ThrowsAsync<YetkiYokException>(() => scope.ServiceProvider.GetRequiredService<RentACar.Application.Bookings.RentalService>()
            .CreateDirectAsync(new RentACar.Application.Bookings.BookingInput
            {
                MusteriId = o.MusteriId, VehicleId = aracB, BasTar = bas.AddDays(20), BitTar = bas.AddDays(21), GunlukUcret = 100m, CikisOfisi = "SubeB",
            }));
        Assert.Equal(2, await KiraSayisiAsync(o.TenantId));
    }

    [Fact]
    public async Task M2_Esanli_donus_ile_ek_hizmet_ya_da_uzatma_tutarsizlik_uretmez()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Admin);
        var s2 = await GirisAsync(o, Kim.Admin);
        var ihlal = new List<string>();
        for (var i = 0; i < 10; i++)
        foreach (var tur in new[] { "ek", "uzat" })
        {
            var bas = Simdi().AddDays(-3).AddMinutes(-i);
            var id = await KiraAcAsync(s, o, await AracAsync(o), bas);
            // Eksik yakıt bedeli (4 × 100 = 400) — bayat yazım bu bileşeni düşürürse tutarsızlık görünür olsun.
            var g = new JsonObject();
            var kira0 = (await Json(await s.C.GetAsync($"{Kira}/{id}"))).GetProperty("kira");
            foreach (var alan in GuncelleAlanlari) g[alan] = JsonNode.Parse(kira0.GetProperty(alan).GetRawText());
            g["yakitBirimUcret"] = 100m;
            await Json(await Gonder(s, HttpMethod.Put, $"{Kira}/{id}", g));
            await Json(await Gonder(s, HttpMethod.Post, $"{Kira}/{id}/teslim", new { cikisKm = 1000, cikisYakit = 8 }));
            // Dönüş eski bitişten 12 saat SONRA, uzatılmış bitişten ÖNCE: sıra sonuçtan okunabilir
            // (uzatma önce → geç dönüş yok; dönüş önce → uzatma reddedilir, 1 gün geç dönüş bedeli).
            var donus = Gonder(s, HttpMethod.Post, $"{Kira}/{id}/donus", new { donusKm = 1100, donusYakit = 4, gercekDonus = bas.AddDays(3).AddHours(12) });
            var diger = tur == "ek"
                ? Gonder(s2, HttpMethod.Post, $"{Kira}/{id}/ek-hizmetler", new { ekHizmetTanimId = o.EkHizmetId, miktar = 1m })
                : Gonder(s2, HttpMethod.Post, $"{Kira}/{id}/uzat", new { yeniBitTar = bas.AddDays(4) });
            var sonuc = await Task.WhenAll(donus, diger);
            Assert.Equal(HttpStatusCode.OK, sonuc[0].StatusCode);

            var d = await Json(await s.C.GetAsync($"{Kira}/{id}"));
            var k = d.GetProperty("kira");
            var ek = d.GetProperty("ekHizmetler").EnumerateArray().Sum(x => Dec(x, "toplam"));
            // ORACLE (bileşen tutarlılığı): Genel = Tutar + aşım + yakıt + geç dönüş + Σ ek hizmet; Bakiye = Genel − Tahsilat.
            var beklenen = Dec(k, "tutar") + Dec(k, "fazlaKmBedeli") + Dec(k, "yakitBedeli") + Dec(k, "uzatmaBedeli") + ek;
            var satir = $"{tur}#{i}: diger={(int)sonuc[1].StatusCode} genel={Dec(k, "genelToplam")} beklenen={beklenen} bit={k.GetProperty("bitTar")}";
            if (Dec(k, "genelToplam") != beklenen || Dec(k, "bakiye") != Dec(k, "genelToplam") - Dec(k, "tahsilat")) ihlal.Add(satir);
            if (Dec(k, "yakitBedeli") != 400m) ihlal.Add("yakıt bedeli kayboldu: " + satir);
            if (tur == "uzat")
            {
                // ORACLE: uzatma dönüşten ÖNCE → 4 gün (Tutar 480), geç dönüş yok, Genel 480 + 400 = 880.
                //         dönüş ÖNCE → uzatma reddedilir (Tamamlandı), 3 gün (360) + 1 gün geç (120) + 400 = 880.
                var uzadi = sonuc[1].StatusCode == HttpStatusCode.OK;
                if (uzadi != (k.GetProperty("bitTar").GetDateTimeOffset() == bas.AddDays(4))) ihlal.Add("bitiş/uzatma sonucu uyuşmuyor: " + satir);
                if (Dec(k, "tutar") != (uzadi ? 480m : 360m)) ihlal.Add("tutar: " + satir);
                if (Dec(k, "uzatmaBedeli") != (uzadi ? 0m : 120m)) ihlal.Add("geç dönüş: " + satir);
                if (Dec(k, "genelToplam") != 880m) ihlal.Add("genel: " + satir);
            }
            else
            {
                // ORACLE: 360 + 1 gün geç (120) + yakıt 400 = 880; ek hizmet eklendiyse + 60.
                if (sonuc[1].StatusCode == HttpStatusCode.OK && ek != 60m) ihlal.Add("ek hizmet kaybı: " + satir);
                if (Dec(k, "genelToplam") != 880m + ek) ihlal.Add("genel: " + satir);
            }
        }
        Assert.True(ihlal.Count == 0, $"{ihlal.Count} yarışta tutarsızlık:\n" + string.Join("\n", ihlal));
    }

    private static async Task<int> DefterSatiriAsync(TestHost host, Guid tenantId)
    {
        using var scope = host.ScopeFor(tenantId);
        await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        return await db.AccountLedgerEntries.CountAsync();
    }

    [Fact]
    public async Task M3_Faturali_ya_da_tahsilatli_kira_iptal_edilemez_iade_sonrasi_edilir()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Admin);
        using var host = new TestHost(fx.Pg.AppConnectionString);

        // (a) Faturalı kira: iptal reddedilir, defter ve durum değişmez; iade faturası sonrası iptal olur.
        var id = await KiraAcAsync(s, o, await AracAsync(o), Simdi().AddHours(-2));
        Guid faturaId;
        using (var sc = host.ScopeFor(o.TenantId))
            faturaId = await sc.ServiceProvider.GetRequiredService<InvoiceService>().CreateFromRentalAsync(id);
        var defter = await DefterSatiriAsync(host, o.TenantId);
        var p = await ProblemBekle(await Gonder(s, HttpMethod.Post, $"{Kira}/{id}/iptal"), HttpStatusCode.BadRequest, UiHata.Dogrulama);
        Assert.Contains("iade", p.GetProperty("detail").GetString());
        Assert.Equal("Kirada", (await Json(await s.C.GetAsync($"{Kira}/{id}"))).GetProperty("kira").GetProperty("durum").GetString());
        Assert.Equal(defter, await DefterSatiriAsync(host, o.TenantId));
        using (var sc = host.ScopeFor(o.TenantId))
            await sc.ServiceProvider.GetRequiredService<InvoiceService>().CreateIadeAsync(faturaId);
        Assert.Equal("Iptal", (await Json(await Gonder(s, HttpMethod.Post, $"{Kira}/{id}/iptal"))).GetProperty("durum").GetString());

        // (b) Tahsilatlı kira: iptal reddedilir; tahsilat iade edilince (ödeme) iptal olur.
        var id2 = await KiraAcAsync(s, o, await AracAsync(o), Simdi().AddHours(-2));
        using (var sc = host.ScopeFor(o.TenantId))
            await sc.ServiceProvider.GetRequiredService<CashService>().CollectAsync(new CashInput { CariId = o.MusteriId, RentalId = id2, Tutar = 100m });
        await ProblemBekle(await Gonder(s, HttpMethod.Post, $"{Kira}/{id2}/iptal"), HttpStatusCode.BadRequest, UiHata.Dogrulama);
        using (var sc = host.ScopeFor(o.TenantId))
            await sc.ServiceProvider.GetRequiredService<CashService>().PayAsync(new CashInput { CariId = o.MusteriId, RentalId = id2, Tutar = 100m });
        Assert.Equal(0m, Dec((await Json(await s.C.GetAsync($"{Kira}/{id2}"))).GetProperty("kira"), "tahsilat"));
        Assert.Equal("Iptal", (await Json(await Gonder(s, HttpMethod.Post, $"{Kira}/{id2}/iptal"))).GetProperty("durum").GetString());

        // (c) Yarış: iptal ile fatura kesimi eşzamanlı — sonuç ya "fatura + Kirada" ya "iptal + faturasız"; ikisi birden ASLA.
        for (var i = 0; i < 6; i++)
        {
            var idr = await KiraAcAsync(s, o, await AracAsync(o), Simdi().AddHours(-3).AddMinutes(-i));
            var iptal = Gonder(s, HttpMethod.Post, $"{Kira}/{idr}/iptal");
            var fatura = Task.Run(async () =>
            {
                using var sc = host.ScopeFor(o.TenantId);
                try { await sc.ServiceProvider.GetRequiredService<InvoiceService>().CreateFromRentalAsync(idr); return true; }
                catch (ValidationException) { return false; }
            });
            await Task.WhenAll(iptal, fatura);
            var durum = (await Json(await s.C.GetAsync($"{Kira}/{idr}"))).GetProperty("kira").GetProperty("durum").GetString();
            var faturaSayisi = (await Json(await s.C.GetAsync($"{Kira}/{idr}/faturalar"))).GetArrayLength();
            Assert.False(durum == "Iptal" && faturaSayisi > 0, $"yarış #{i}: iptal edilmiş kirada {faturaSayisi} fatura");
            Assert.Equal(fatura.Result, faturaSayisi == 1);
        }
    }

    [Fact]
    public async Task L2_Islem_govdesinde_eksik_alan_400_alanli_hicbir_sey_yazilmaz()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.OperatorA);
        var bas = Simdi().AddDays(-3);
        var id = await KiraAcAsync(s, o, await AracAsync(o), bas);

        async Task<HttpResponseMessage> Ham(string yol, string govde)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"{Kira}/{id}/{yol}")
            { Content = new StringContent(govde, System.Text.Encoding.UTF8, "application/json") };
            req.Headers.Add("X-XSRF-TOKEN", s.Xsrf);
            return await s.C.SendAsync(req);
        }

        await AlanBekle(await Ham("teslim", "{\"cikisKm\":1000}"), "cikisYakit");
        await AlanBekle(await Ham("teslim", "{\"cikisYakit\":8}"), "cikisKm");
        Assert.True((await Json(await s.C.GetAsync($"{Kira}/{id}"))).GetProperty("kira").GetProperty("cikisKm").ValueKind == JsonValueKind.Null);
        await Json(await Gonder(s, HttpMethod.Post, $"{Kira}/{id}/teslim", new { cikisKm = 1000, cikisYakit = 8 }));
        await AlanBekle(await Ham("donus", $"{{\"donusKm\":1100,\"gercekDonus\":\"{bas.AddDays(3):O}\"}}"), "donusYakit");
        await AlanBekle(await Ham("donus", "{\"donusKm\":1100,\"donusYakit\":8}"), "gercekDonus");
        await AlanBekle(await Ham("uzat", "{}"), "yeniBitTar");
        await AlanBekle(await Ham("ek-hizmetler", "{\"miktar\":1}"), "ekHizmetTanimId");
        await AlanBekle(await Ham("ek-hizmetler", $"{{\"ekHizmetTanimId\":\"{o.EkHizmetId}\"}}"), "miktar");
        var k = (await Json(await s.C.GetAsync($"{Kira}/{id}"))).GetProperty("kira");
        Assert.Equal("Kirada", k.GetProperty("durum").GetString());
        Assert.Equal(360m, Dec(k, "genelToplam"));
    }

    [Fact]
    public async Task L3_L4_L6_Tasma_ve_uc_deger_girdileri_400_alanli_kayit_birakmaz()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Admin);
        var bas = Simdi().AddDays(-1);
        var id = await KiraAcAsync(s, o, await AracAsync(o), bas);
        var arac2 = await AracAsync(o);
        var b2 = Simdi().AddDays(30);
        object Govde(object ek) => ek; // okunabilirlik
        async Task Olustur(string alan, object govde) => await AlanBekle(await Gonder(s, HttpMethod.Post, Kira, govde), alan);

        await Olustur("gunlukUcret", Govde(new { musteriId = o.MusteriId, vehicleId = arac2, basTar = b2, bitTar = b2.AddDays(3), gunlukUcret = 100000000000000000m }));
        await Olustur("depozito", new { musteriId = o.MusteriId, vehicleId = arac2, basTar = b2, bitTar = b2.AddDays(3), gunlukUcret = 100m, depozito = 100000000000000000m });
        await Olustur("dropUcreti", new { musteriId = o.MusteriId, vehicleId = arac2, basTar = b2, bitTar = b2.AddDays(3), gunlukUcret = 100m, cikisOfisi = "A", donusOfisi = "B", dropUcreti = 900000000000000m });
        await Olustur("komisyonOran", new { musteriId = o.MusteriId, vehicleId = arac2, basTar = b2, bitTar = b2.AddDays(3), gunlukUcret = 100m, komisyonOran = 1000000m });
        await Olustur("kiralamaTuru", new { musteriId = o.MusteriId, vehicleId = arac2, basTar = b2, bitTar = b2.AddDays(3), gunlukUcret = 100m, kiralamaTuru = new string('a', 500) });
        // L4: süre ≤ 5 yıl (9999 → önceden 500 + araç yüzyıllarca bloke + kayıt kalıyordu)
        await Olustur("bitTar", new { musteriId = o.MusteriId, vehicleId = arac2, basTar = b2, bitTar = new DateTimeOffset(9999, 12, 30, 0, 0, 0, TimeSpan.Zero), gunlukUcret = 100m });
        await Olustur("bitTar", new { musteriId = o.MusteriId, vehicleId = arac2, basTar = b2, bitTar = b2.AddYears(5).AddDays(1), gunlukUcret = 100m });
        Assert.Equal(1, await KiraSayisiAsync(o.TenantId)); // yalnız kurulum kirası: hiçbir red kayıt bırakmadı
        // Sınırda: tam 5 yıl kabul (uzun dönem kiralama)
        await Json(await Gonder(s, HttpMethod.Post, Kira, new { musteriId = o.MusteriId, vehicleId = arac2, basTar = b2, bitTar = b2.AddYears(5), gunlukUcret = 100m }), HttpStatusCode.Created);

        var g = new JsonObject();
        var kira = (await Json(await s.C.GetAsync($"{Kira}/{id}"))).GetProperty("kira");
        foreach (var alan in GuncelleAlanlari) g[alan] = JsonNode.Parse(kira.GetProperty(alan).GetRawText());
        g["provizyon"] = 100000000000000000m;
        await AlanBekle(await Gonder(s, HttpMethod.Put, $"{Kira}/{id}", g), "provizyon");
        g["provizyon"] = null; g["fazlaKmUcret"] = 100000000000000000m;
        await AlanBekle(await Gonder(s, HttpMethod.Put, $"{Kira}/{id}", g), "fazlaKmUcret");

        await AlanBekle(await Gonder(s, HttpMethod.Post, $"{Kira}/{id}/uzat", new { yeniBitTar = new DateTimeOffset(9999, 12, 30, 0, 0, 0, TimeSpan.Zero) }), "yeniBitTar");
        // L6: yakıt 0–12
        await AlanBekle(await Gonder(s, HttpMethod.Post, $"{Kira}/{id}/teslim", new { cikisKm = 1000, cikisYakit = -50 }), "cikisYakit");
        await AlanBekle(await Gonder(s, HttpMethod.Post, $"{Kira}/{id}/teslim", new { cikisKm = 1000, cikisYakit = 13 }), "cikisYakit");
        await Json(await Gonder(s, HttpMethod.Post, $"{Kira}/{id}/teslim", new { cikisKm = 1000, cikisYakit = 12 }));
        var onizleme = await Json(await s.C.GetAsync($"{Kira}/{id}/donus-hesapla?donusKm=1100&donusYakit=-2147483648&gercekDonus={Uri.EscapeDataString(bas.AddDays(3).ToString("O"))}"));
        Assert.False(onizleme.GetProperty("ok").GetBoolean());
        await AlanBekle(await Gonder(s, HttpMethod.Post, $"{Kira}/{id}/donus", new { donusKm = 1100, donusYakit = 13, gercekDonus = bas.AddDays(3) }), "donusYakit");
        await AlanBekle(await Gonder(s, HttpMethod.Post, $"{Kira}/{id}/donus", new { donusKm = 1100, donusYakit = 8, gercekDonus = Simdi().AddYears(2) }), "gercekDonus");
        await AlanBekle(await Gonder(s, HttpMethod.Post, $"{Kira}/musteri", new { ad = new string('a', 5000) }), "ad");

        var son = (await Json(await s.C.GetAsync($"{Kira}/{id}"))).GetProperty("kira");
        Assert.Equal("Kirada", son.GetProperty("durum").GetString());
        Assert.Equal(360m, Dec(son, "genelToplam"));
        Assert.Equal(kira.GetProperty("bitTar").GetDateTimeOffset(), son.GetProperty("bitTar").GetDateTimeOffset());
    }

    [Fact]
    public async Task L5_Yabanci_ya_da_olmayan_musteri_arac_400_alanli()
    {
        var o = await OrtamKurAsync();
        var diger = await OrtamKurAsync();
        var s = await GirisAsync(diger, Kim.Admin);
        var b = Simdi().AddDays(3);
        // Başka kiracının müşterisi (FK denetimi RLS'i atlar — önceden kira YAZILIYORDU).
        await AlanBekle(await Gonder(s, HttpMethod.Post, Kira, new
        {
            musteriId = o.MusteriId, vehicleId = await AracAsync(diger), basTar = b, bitTar = b.AddDays(1), gunlukUcret = 100m,
        }), "musteriId");
        // Başka kiracının aracı
        await AlanBekle(await Gonder(s, HttpMethod.Post, Kira, new
        {
            musteriId = diger.MusteriId, vehicleId = await AracAsync(o), basTar = b, bitTar = b.AddDays(1), gunlukUcret = 100m,
        }), "vehicleId");
        // Var olmayan kimlik (önceden 500)
        await AlanBekle(await Gonder(s, HttpMethod.Post, Kira, new
        {
            musteriId = diger.MusteriId, vehicleId = Guid.NewGuid(), basTar = b, bitTar = b.AddDays(1), gunlukUcret = 100m,
        }), "vehicleId");
        Assert.Equal(0, await KiraSayisiAsync(diger.TenantId));
        Assert.Equal(0, await KiraSayisiAsync(o.TenantId));
    }

    [Fact]
    public async Task L7_Liste_tarih_suzgeci_Istanbul_gunu_sinirlari()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Admin);
        var tz = TenantGun.Dilim;
        var d = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).DateTime).AddDays(40);
        DateTimeOffset Yerel(DateOnly g, int sa, int dk, int sn = 0, int ms = 0)
        {
            var y = g.ToDateTime(new TimeOnly(sa, dk, sn, ms), DateTimeKind.Unspecified);
            return new DateTimeOffset(y, tz.GetUtcOffset(y)).ToUniversalTime();
        }
        var sabah = await KiraAcAsync(s, o, await AracAsync(o), Yerel(d, 0, 30), gun: 1);          // UTC'de önceki gün
        var geceSonu = await KiraAcAsync(s, o, await AracAsync(o), Yerel(d, 23, 59, 59, 500), gun: 1);
        var ertesi = await KiraAcAsync(s, o, await AracAsync(o), Yerel(d.AddDays(1), 0, 0), gun: 1);
        var dun = await KiraAcAsync(s, o, await AracAsync(o), Yerel(d.AddDays(-1), 23, 59, 59, 999), gun: 1);
        var l = await Json(await s.C.GetAsync($"{Kira}?basMin={d:yyyy-MM-dd}&basMax={d:yyyy-MM-dd}&boyut=200"));
        var ids = l.GetProperty("kayitlar").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToHashSet();
        Assert.Contains(sabah, ids);
        Assert.Contains(geceSonu, ids);   // önceden AddSeconds(-1) kesimi 23:59:59.xxx'i dışarıda bırakıyordu
        Assert.DoesNotContain(ertesi, ids);
        Assert.DoesNotContain(dun, ids);
    }

    [Fact]
    public async Task Panel_gun_kovasi_Istanbul_sinirlarinda()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Admin);
        var tz = TenantGun.Dilim;
        var bugun = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).DateTime);
        DateTimeOffset Yerel(DateOnly g, int sa, int dk)
        {
            var y = g.ToDateTime(new TimeOnly(sa, dk), DateTimeKind.Unspecified);
            return new DateTimeOffset(y, tz.GetUtcOffset(y)).ToUniversalTime();
        }
        var vakalar = new (DateTimeOffset Bit, string? Kova)[]
        {
            (Yerel(bugun.AddDays(-1), 23, 59), "gecikmis"), (Yerel(bugun, 0, 1), "bugun"), (Yerel(bugun, 23, 59), "bugun"),
            (Yerel(bugun.AddDays(1), 0, 1), "yarin"), (Yerel(bugun.AddDays(1), 23, 59), "yarin"), (Yerel(bugun.AddDays(2), 0, 1), null),
        };
        var idler = new List<Guid>();
        foreach (var v in vakalar)
            idler.Add((await Json(await Gonder(s, HttpMethod.Post, Kira, new
            {
                musteriId = o.MusteriId, vehicleId = await AracAsync(o), basTar = v.Bit.AddDays(-2), bitTar = v.Bit, gunlukUcret = 100m, cikisOfisi = "SubeA",
            }), HttpStatusCode.Created)).GetProperty("id").GetGuid());
        var p = await Json(await s.C.GetAsync(V1 + "/panel/ozet"));
        if (DateOnly.Parse(p.GetProperty("bugun").GetString()!) != bugun) return; // test gece yarısını geçti — ölçüm geçersiz
        for (var i = 0; i < vakalar.Length; i++)
        {
            string? bulunan = null;
            foreach (var kova in new[] { "gecikmis", "bugun", "yarin" })
                if (p.GetProperty("donusler").GetProperty(kova).EnumerateArray().Any(x => x.GetProperty("rentalId").GetGuid() == idler[i]))
                    bulunan = kova;
            Assert.True(bulunan == vakalar[i].Kova, $"#{i} bit={vakalar[i].Bit:O}: beklenen {vakalar[i].Kova ?? "yok"}, bulunan {bulunan ?? "yok"}");
        }
    }

    [Fact]
    public async Task L1_SYS_tanim_kodu_degistirilemez_silinemez_satir_silinemez()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.OperatorA);
        var bas = Simdi().AddDays(5);
        // Farklı dönüş ofisi + drop ücreti → FeeLineService SYS-DROP satırını yazar.
        var id = (await Json(await Gonder(s, HttpMethod.Post, Kira, new
        {
            musteriId = o.MusteriId, vehicleId = await AracAsync(o), basTar = bas, bitTar = bas.AddDays(3), gunlukUcret = 100m, fiyatTuru = "Günlük",
            cikisOfisi = "SubeA", donusOfisi = "SubeA2", dropUcreti = 100m,
        }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var d = await Json(await s.C.GetAsync($"{Kira}/{id}"));
        var satir = d.GetProperty("ekHizmetler").EnumerateArray().Single();
        var tanimId = satir.GetProperty("ekHizmetTanimId").GetGuid();
        var genel = Dec(d.GetProperty("kira"), "genelToplam");
        Assert.Equal(360m + 120m, genel); // ORACLE: 3 × 100 net + %20 = 360; drop 100 net + %20 = 120

        using (var host = new TestHost(fx.Pg.AppConnectionString))
        {
            using var sc = host.ScopeFor(o.TenantId, Guid.NewGuid(), "op", UserRole.Operator, "SubeA");
            var svc = sc.ServiceProvider.GetRequiredService<RentACar.Application.EkHizmetler.EkHizmetTanimService>();
            var t = (await svc.GetAsync(tanimId))!;
            RentACar.Application.EkHizmetler.EkHizmetTanimInput Girdi(string kod) =>
                new() { Kod = kod, Ad = t.Ad, BirimUcret = t.BirimUcret, KdvOrani = t.KdvOrani, Aktif = t.Aktif };
            await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateAsync(tanimId, Girdi("DROPX")));   // kod değişmez
            await Assert.ThrowsAsync<ValidationException>(() => svc.DeleteAsync(tanimId));                    // silinmez
            Assert.True(await svc.UpdateAsync(tanimId, Girdi(t.Kod)));                                        // aynı kodla düzenleme serbest
            await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateAsync(o.EkHizmetId, new()          // normal → SYS- olmaz
            { Kod = "SYS-GPS", Ad = "Navigasyon", BirimUcret = 50m, KdvOrani = 0.20m, Aktif = true }));
            await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new()                        // elle SYS-* açılmaz
            { Kod = "SYS-YENI", Ad = "Sahte sistem ücreti", BirimUcret = 1m, KdvOrani = 0.20m, Aktif = true }));
        }
        await ProblemBekle(await Gonder(s, HttpMethod.Delete, $"{Kira}/{id}/ek-hizmetler/{satir.GetProperty("id").GetGuid()}"),
            HttpStatusCode.BadRequest, UiHata.Dogrulama);
        Assert.Equal(genel, Dec((await Json(await s.C.GetAsync($"{Kira}/{id}"))).GetProperty("kira"), "genelToplam"));
    }

    // ================================================================== F4.1 adversarial 2. tur (N1–N3)

    private async Task<T> VeriOkuAsync<T>(Guid tenantId, Func<AppDbContext, Task<T>> oku)
    {
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(tenantId);
        await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        return await oku(db);
    }

    [Fact]
    public async Task N1_Turkce_harfli_ofis_adinda_kendi_subesi_acilir_baska_sube_403()
    {
        var o = await OrtamKurAsync();
        var ege = new Branch { Kod = "EG", Ad = "Ege Bölge" };
        var marmara = new Branch { Kod = "MR", Ad = "Marmara Bölge" };
        // 'İ' (U+0130), 'I', 'ı' (U+0131), 'i' — Postgres lower() ile .NET ToLowerInvariant'ın ayrıştığı harfler.
        var egeOfisleri = new[] { "İzmir Merkez", "ISPARTA Işık", "ığdır iı", "Şişli Ofis", "iİıI Karma" };
        // Şubeler ÖNCE kaydedilir: ofis kaydındaki şube-FK interceptor'ı şubeyi DB'den çözer.
        await VeriYazAsync(o.TenantId, db => { db.Branches.Add(ege); db.Branches.Add(marmara); });
        await VeriYazAsync(o.TenantId, db =>
        {
            var k = 0;
            foreach (var ad in egeOfisleri) db.Locations.Add(new Location { Kod = "E" + k++, Ad = ad, Sube = ege.Ad, SubeId = ege.Id, Aktif = true });
            db.Locations.Add(new Location { Kod = "M0", Ad = "İstanbul Avrupa", Sube = marmara.Ad, SubeId = marmara.Id, Aktif = true });
        });
        // Şube FK'li operatör (Ege)
        var ad2 = Rastgele("n1");
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.Pg.OwnerConnectionString).Options,
                         NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            var u = new User { TenantId = o.TenantId, UserName = ad2, DisplayName = ad2, Rol = UserRole.Operator, AtanmisSube = ege.Ad, AtanmisSubeId = ege.Id, IsActive = true };
            u.PasswordHash = fx.Web.Services.GetRequiredService<IPasswordHasher<User>>().HashPassword(u, o.Sifre);
            db.Users.Add(u);
            await db.SaveChangesAsync();
        }
        var op = await GirisAsync(o.Kod, ad2, o.Sifre);
        var bas = Simdi().AddDays(5);
        var i = 0;
        Guid ilk = Guid.Empty;
        foreach (var ofis in egeOfisleri)
        {
            var yanit = await Gonder(op, HttpMethod.Post, Kira, new
            {
                musteriId = o.MusteriId, vehicleId = await AracAsync(o, ege.Ad), basTar = bas.AddDays(i), bitTar = bas.AddDays(i + 1), gunlukUcret = 100m, cikisOfisi = ofis,
            });
            Assert.True(yanit.StatusCode == HttpStatusCode.Created, $"'{ofis}': {(int)yanit.StatusCode} {await yanit.Content.ReadAsStringAsync()}");
            var id = (await Json(yanit, HttpStatusCode.Created)).GetProperty("id").GetGuid();
            i += 2;
            Assert.Equal(HttpStatusCode.OK, (await op.C.GetAsync($"{Kira}/{id}")).StatusCode); // kaydı da görür
            if (ilk == Guid.Empty) ilk = id;
        }
        // Başka şubenin ofisi (adı da 'İ' ile) → 403, hiçbir şey yazılmaz.
        var once = await KiraSayisiAsync(o.TenantId);
        await ProblemBekle(await Gonder(op, HttpMethod.Post, Kira, new
        {
            musteriId = o.MusteriId, vehicleId = await AracAsync(o, ege.Ad), basTar = bas.AddDays(40), bitTar = bas.AddDays(41), gunlukUcret = 100m, cikisOfisi = "İstanbul Avrupa",
        }), HttpStatusCode.Forbidden, UiHata.YetkiYok);
        Assert.Equal(once, await KiraSayisiAsync(o.TenantId));

        // Açık kirada ofis değiştirme (UpdateOpenAsync — aynı eşleme): kendi şubesinin 'İ'li ofisine 200, başkasına 403.
        var g = new JsonObject();
        var kira = (await Json(await op.C.GetAsync($"{Kira}/{ilk}"))).GetProperty("kira");
        foreach (var alan in GuncelleAlanlari) g[alan] = JsonNode.Parse(kira.GetProperty(alan).GetRawText());
        g["cikisOfisi"] = "iİıI Karma";
        Assert.Equal("iİıI Karma", (await Json(await Gonder(op, HttpMethod.Put, $"{Kira}/{ilk}", g))).GetProperty("cikisOfisi").GetString());
        g["cikisOfisi"] = "İstanbul Avrupa";
        await ProblemBekle(await Gonder(op, HttpMethod.Put, $"{Kira}/{ilk}", g), HttpStatusCode.Forbidden, UiHata.YetkiYok);
    }

    [Fact]
    public async Task N2_Fatura_ile_ek_hizmet_ekle_sil_serilesir_fatura_sozlesmeyle_ayni()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Admin);
        using var host = new TestHost(fx.Pg.AppConnectionString);
        var rnd = new Random(17);
        async Task<bool> Fatura(Guid id)
        {
            await Task.Delay(rnd.Next(0, 12));
            using var sc = host.ScopeFor(o.TenantId);
            try { await sc.ServiceProvider.GetRequiredService<InvoiceService>().CreateFromRentalAsync(id); return true; }
            catch (ValidationException) { return false; } // yarışı kaybeden: temiz red (kilitlenme/500 DEĞİL)
        }
        async Task<HttpStatusCode> Http(Func<Task<HttpResponseMessage>> f) { await Task.Delay(rnd.Next(0, 12)); return (await f()).StatusCode; }
        var ihlal = new List<string>();
        for (var i = 0; i < 12; i++)
        foreach (var tur in new[] { "ekle", "sil" })
        {
            // ORACLE: kira 360 (3 × 100 net + %20); GPS'li açılışta 420. Fatura ya ÖNCE (ek hizmet reddedilir) ya SONRA
            // (güncel tutarla) kesilir; bayat tutarla kesilen fatura reddedilir. Her durumda fatura == sözleşme.
            var id = await KiraAcAsync(s, o, await AracAsync(o), Simdi().AddHours(-3).AddMinutes(-i), gps: tur == "sil");
            var kalem = tur == "sil"
                ? (await Json(await s.C.GetAsync($"{Kira}/{id}"))).GetProperty("ekHizmetler")[0].GetProperty("id").GetGuid()
                : Guid.Empty;
            var tf = Fatura(id);
            var te = tur == "ekle"
                ? Http(() => Gonder(s, HttpMethod.Post, $"{Kira}/{id}/ek-hizmetler", new { ekHizmetTanimId = o.EkHizmetId, miktar = 1m }))
                : Http(() => Gonder(s, HttpMethod.Delete, $"{Kira}/{id}/ek-hizmetler/{kalem}"));
            await Task.WhenAll(tf, te);
            Assert.True(te.Result is HttpStatusCode.OK or HttpStatusCode.BadRequest, $"{tur}#{i}: ek hizmet {(int)te.Result}");
            var genel = await VeriOkuAsync(o.TenantId, db => db.Rentals.Where(x => x.Id == id).Select(x => x.GenelToplam).SingleAsync());
            var fatura = await VeriOkuAsync(o.TenantId, db => db.Invoices.Where(x => x.RentalId == id).SumAsync(x => (decimal?)x.GenelToplam)) ?? 0m;
            var satir = $"{tur}#{i}: fatura={tf.Result} ek={(int)te.Result} genel={genel} faturaToplam={fatura}";
            if (tf.Result && fatura != genel) ihlal.Add(satir);
            if (!tf.Result && fatura != 0m) ihlal.Add("reddedilen fatura yazılmış: " + satir);
            if (genel != (tur == "ekle" ? (te.Result == HttpStatusCode.OK ? 420m : 360m) : (te.Result == HttpStatusCode.OK ? 360m : 420m)))
                ihlal.Add("sözleşme toplamı: " + satir);
        }
        Assert.True(ihlal.Count == 0, $"{ihlal.Count} yarışta fatura ≠ sözleşme:\n" + string.Join("\n", ihlal));

        // İptal edilmiş kiraya ek hizmet eklenemez (kilit altında durum kontrolü).
        var idIptal = await KiraAcAsync(s, o, await AracAsync(o), Simdi().AddHours(-3));
        await Json(await Gonder(s, HttpMethod.Post, $"{Kira}/{idIptal}/iptal"));
        await ProblemBekle(await Gonder(s, HttpMethod.Post, $"{Kira}/{idIptal}/ek-hizmetler", new { ekHizmetTanimId = o.EkHizmetId, miktar = 1m }),
            HttpStatusCode.BadRequest, UiHata.Dogrulama);
        Assert.Equal(360m, Dec((await Json(await s.C.GetAsync($"{Kira}/{idIptal}"))).GetProperty("kira"), "genelToplam"));
    }

    [Fact]
    public async Task N3_Iptal_kiraya_tahsilat_yazilamaz_servis_ve_api_yarisi()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Admin);
        var y = await GirisAsync(o, Kim.Admin);
        using var host = new TestHost(fx.Pg.AppConnectionString);

        // (a) sıralı, SERVİS yolu (Blazor /finans/tahsilat aynı CashService'ten geçer): red, hiçbir şey yazılmaz.
        var id0 = await KiraAcAsync(s, o, await AracAsync(o), Simdi().AddHours(-3));
        await Json(await Gonder(s, HttpMethod.Post, $"{Kira}/{id0}/iptal"));
        var defter = await VeriOkuAsync(o.TenantId, db => db.AccountLedgerEntries.CountAsync());
        using (var sc = host.ScopeFor(o.TenantId))
            await Assert.ThrowsAnyAsync<ValidationException>(() => sc.ServiceProvider.GetRequiredService<CashService>()
                .CollectAsync(new CashInput { CariId = o.MusteriId, RentalId = id0, Tutar = 100m }));
        Assert.Equal(defter, await VeriOkuAsync(o.TenantId, db => db.AccountLedgerEntries.CountAsync()));
        Assert.Equal(0m, await VeriOkuAsync(o.TenantId, db => db.Rentals.Where(x => x.Id == id0).Select(x => x.Tahsilat).SingleAsync()));

        // (b) yarış: iptal ∥ tahsilat — servis yolu ve gerçek /api/ui/v1/finans/tahsilat ucu. İptal kirada tahsilat ASLA kalmaz.
        Task<HttpResponseMessage> ApiTahsil(Guid kira)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, V1 + "/finans/tahsilat")
            { Content = JsonContent.Create(new { cariId = o.MusteriId, tutar = 50m, hesap = "Kasa", kiraId = kira }) };
            req.Headers.Add("X-XSRF-TOKEN", y.Xsrf);
            req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            return y.C.SendAsync(req);
        }
        var rnd = new Random(23);
        var ihlal = new List<string>();
        for (var i = 0; i < 20; i++)
        {
            var id = await KiraAcAsync(s, o, await AracAsync(o), Simdi().AddHours(-3).AddMinutes(-i));
            var d1 = rnd.Next(0, 10); var d2 = rnd.Next(0, 10);
            var iptal = Task.Run(async () => { await Task.Delay(d1); return (await Gonder(s, HttpMethod.Post, $"{Kira}/{id}/iptal")).StatusCode; });
            Task<string> tahsil = i % 2 == 0
                ? Task.Run(async () =>
                {
                    await Task.Delay(d2);
                    using var sc = host.ScopeFor(o.TenantId);
                    try { await sc.ServiceProvider.GetRequiredService<CashService>().CollectAsync(new CashInput { CariId = o.MusteriId, RentalId = id, Tutar = 50m }); return "200"; }
                    catch (ValidationException) { return "400"; }
                })
                : Task.Run(async () => { await Task.Delay(d2); return ((int)(await ApiTahsil(id)).StatusCode).ToString(); });
            await Task.WhenAll(iptal, tahsil);
            Assert.True(tahsil.Result is "200" or "400", $"#{i}: tahsilat {tahsil.Result}");
            var k = await VeriOkuAsync(o.TenantId, db => db.Rentals.AsNoTracking().SingleAsync(x => x.Id == id));
            // ORACLE: ya iptal (tahsilat 0) ya tahsilat 50 + Kirada (iptal "tahsilatlı kira" diye reddedildi).
            var tutarli = (k.Durum == RentalStatus.Iptal && k.Tahsilat == 0m) || (k.Durum == RentalStatus.Kirada && k.Tahsilat == 50m);
            if (!tutarli) ihlal.Add($"#{i}: iptal={(int)iptal.Result} tahsilat={tahsil.Result} → durum={k.Durum} tahsilat={k.Tahsilat}");
        }
        Assert.True(ihlal.Count == 0, string.Join("\n", ihlal));
    }

    /// <summary>RequireAnyPermission'ın tek izinle çağrılmasını reddetmesini sınamak için boş kural oluşturucu.</summary>
    private sealed class RouteGroupBuilderStub : Microsoft.AspNetCore.Builder.IEndpointConventionBuilder
    {
        public void Add(Action<Microsoft.AspNetCore.Builder.EndpointBuilder> convention) { }
    }
}
