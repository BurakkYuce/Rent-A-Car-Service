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
    private const string Rental = V1 + "/kiralar";

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

    private static string RandomText(string prefix) => prefix + Guid.NewGuid().ToString("N")[..10];

    /// <summary>Tam saniyeye hizalı "şimdi" (Mac µs / Linux tick farkı DB eşitliğini bozmasın).</summary>
    private static DateTimeOffset Now() => DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    private async Task<Ortam> SetUpEnvironmentAsync(bool pilot = true)
    {
        var o = new Ortam
        {
            TenantId = Guid.NewGuid(),
            Kod = RandomText("f41"),
            Sifre = WebFixture.RandomPassword(),
            Kullanicilar = Enum.GetValues<Kim>().ToDictionary(k => k, _ => RandomText("u")),
        };
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.Pg.OwnerConnectionString).Options;
        await using (var db = new AppDbContext(opts, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            db.Tenants.Add(new Tenant { Id = o.TenantId, Code = o.Kod, Name = o.Kod, IsActive = true });
            var hasher = fx.Web.Services.GetRequiredService<IPasswordHasher<User>>();
            foreach (var (kim, name) in o.Kullanicilar)
            {
                var (rol, branch) = kim switch
                {
                    Kim.Admin => (UserRole.Admin, (string?)null),
                    Kim.OperatorA => (UserRole.Operator, "SubeA"),
                    Kim.OperatorB => (UserRole.Operator, "SubeB"),
                    _ => (UserRole.Muhasebe, null),
                };
                var u = new User { TenantId = o.TenantId, UserName = name, DisplayName = name, Rol = rol, AtanmisSube = branch, IsActive = true };
                u.PasswordHash = hasher.HashPassword(u, o.Sifre);
                db.Users.Add(u);
            }
            await db.SaveChangesAsync();
        }
        await fx.MakePilotAsync(o.TenantId, pilot);

        var customer = new Customer { Tip = CustomerType.Bireysel, Ad = "Deniz", Soyad = "Yılmaz", CepTel = "05320001122", Email = "deniz@ornek.test" };
        var gps = new EkHizmetTanim { Kod = "GPS", Ad = "Navigasyon", BirimUcret = 50m, KdvOrani = 0.20m, Aktif = true };
        await WriteDataAsync(o.TenantId, db => { db.Customers.Add(customer); db.EkHizmetTanimlari.Add(gps); });
        o.MusteriId = customer.Id;
        o.EkHizmetId = gps.Id;
        return o;
    }

    private async Task WriteDataAsync(Guid tenantId, Action<AppDbContext> write)
    {
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(tenantId);
        var f = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        write(db);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> VehicleAsync(Ortam o, string branch = "SubeA")
    {
        var v = new Vehicle { Plaka = "34 F41 " + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), Marka = "Fiat", Tip = "Egea", Grup = "C", Sube = branch, Durum = VehicleStatus.Musait, Km = 1000 };
        await WriteDataAsync(o.TenantId, db => db.Vehicles.Add(v));
        return v.Id;
    }

    private sealed record Oturum(HttpClient C, string Xsrf);

    private static string? CookieValue(HttpResponseMessage r, string name)
    {
        if (!r.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var d in values)
            if (d.StartsWith(name + "=", StringComparison.Ordinal))
                return Uri.UnescapeDataString(d[(name.Length + 1)..].Split(';')[0]);
        return null;
    }

    private Task<Oturum> LoginAsync(Ortam o, Kim kim) => LoginAsync(o.Kod, o.Kullanicilar[kim], o.Sifre);

    private async Task<Oturum> LoginAsync(string company, string user, string password)
    {
        var c = fx.Web.Client();
        var x = await c.GetAsync(V1 + "/oturum/xsrf");
        var once = CookieValue(x, "XSRF-TOKEN")!;
        var req = new HttpRequestMessage(HttpMethod.Post, V1 + "/oturum/giris")
        {
            Content = JsonContent.Create(new { firma = company, kullanici = user, sifre = password }),
        };
        req.Headers.Add("X-XSRF-TOKEN", once);
        var r = await c.SendAsync(req);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"giriş başarısız: {await r.Content.ReadAsStringAsync()}");
        return new Oturum(c, CookieValue(r, "XSRF-TOKEN")!);
    }

    private static Task<HttpResponseMessage> Gonder(Oturum s, HttpMethod m, string url, object? body = null)
    {
        var req = new HttpRequestMessage(m, url);
        req.Headers.Add("X-XSRF-TOKEN", s.Xsrf);
        // Low-B: ek hizmet ekleme Idempotency-Key ister; buradaki senaryolar her gönderimi AYRI işlem sayar
        // (mükerrer davranışı LowTemizligiBUiTests'te kilitli).
        if (m == HttpMethod.Post && url.EndsWith("/ek-hizmetler", StringComparison.Ordinal))
            req.Headers.Add("Idempotency-Key", "f41-" + Guid.NewGuid().ToString("N"));
        if (body is not null) req.Content = JsonContent.Create(body);
        return s.C.SendAsync(req);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == expected, $"Beklenen {(int)expected}, gelen {(int)r.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<JsonElement> ExpectProblem(HttpResponseMessage r, HttpStatusCode status, string code)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(status == r.StatusCode, $"Beklenen {(int)status}, gelen {(int)r.StatusCode}: {text}");
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        var root = JsonDocument.Parse(text).RootElement.Clone();
        Assert.Equal(code, root.GetProperty("kod").GetString());
        return root;
    }

    private static decimal Dec(JsonElement e, string name) => e.GetProperty(name).GetDecimal();

    /// <summary>API ile yeni kira (3 gün × 100 NET "Günlük" modu, isteğe bağlı GPS).</summary>
    private async Task<Guid> OpenRentalAsync(Oturum s, Ortam o, Guid vehicle, DateTimeOffset start, int day = 3, bool gps = false,
        string pickupOffice = "SubeA")
    {
        var r = await Gonder(s, HttpMethod.Post, Rental, new
        {
            musteriId = o.MusteriId, vehicleId = vehicle, basTar = start, bitTar = start.AddDays(day),
            gunlukUcret = 100m, fiyatTuru = "Günlük", cikisOfisi = pickupOffice, donusOfisi = pickupOffice,
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
        var o = await SetUpEnvironmentAsync();
        var vehicle = await VehicleAsync(o);
        var s = await LoginAsync(o, Kim.OperatorA);
        var start = Now().AddHours(-1);

        var id = await OpenRentalAsync(s, o, vehicle, start, day: 3, gps: true);

        // ORACLE: 3 gün × 100 net = 300; KDV %20 = 60 → baz 360 brüt. GPS: 50 net + 10 KDV = 60. Genel = 420.
        var d = await Json(await s.C.GetAsync($"{Rental}/{id}"));
        var k = d.GetProperty("kira");
        Assert.Equal("Kirada", k.GetProperty("durum").GetString());
        Assert.Equal(3, k.GetProperty("gun").GetInt32());
        Assert.Equal(120m, Dec(k, "gunlukUcret")); // net 100 → brüt 120
        Assert.Equal(360m, Dec(k, "tutar"));
        Assert.Equal(420m, Dec(k, "genelToplam"));
        Assert.Equal(420m, Dec(k, "bakiye"));
        Assert.Equal(0m, Dec(k, "tahsilat"));
        var items = d.GetProperty("ekHizmetler");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(50m, Dec(items[0], "netTutar"));
        Assert.Equal(10m, Dec(items[0], "kdvTutar"));
        Assert.Equal(60m, Dec(items[0], "toplam"));
        Assert.Equal("Deniz Yılmaz", d.GetProperty("musteri").GetProperty("ad").GetString());
        Assert.StartsWith("34 F41", d.GetProperty("arac").GetProperty("plaka").GetString());
        var y = d.GetProperty("yetkiler");
        Assert.True(y.GetProperty("operasyon").GetBoolean());
        Assert.False(y.GetProperty("silme").GetBoolean()); // operatör silemez
        Assert.False(y.GetProperty("finans").GetBoolean());

        // Teslim
        var t = await Json(await Gonder(s, HttpMethod.Post, $"{Rental}/{id}/teslim", new { cikisKm = 1000, cikisYakit = 8 }));
        Assert.Equal(1000, t.GetProperty("cikisKm").GetInt32());
        // İkinci teslim: yapısal red (durum geçişi), 400
        await ExpectProblem(await Gonder(s, HttpMethod.Post, $"{Rental}/{id}/teslim", new { cikisKm = 1000, cikisYakit = 8 }),
            HttpStatusCode.BadRequest, UiError.Validation);

        // Uzat +1 gün → ORACLE: 4 gün; ek 1 gün × 120 brüt → Tutar 480, Genel 540.
        var newEnd = start.AddDays(4);
        var u = await Json(await Gonder(s, HttpMethod.Post, $"{Rental}/{id}/uzat", new { yeniBitTar = newEnd }));
        Assert.Equal(4, u.GetProperty("gun").GetInt32());
        Assert.Equal(480m, Dec(u, "tutar"));
        Assert.Equal(540m, Dec(u, "genelToplam"));
        Assert.Equal(540m, Dec(u, "bakiye"));

        // Dönüş önizlemesi (persist yok): zamanında, km limitsiz, yakıt aynı → ek bedel 0; genel 540.
        var q = $"donusKm=1300&donusYakit=8&gercekDonus={Uri.EscapeDataString(newEnd.ToString("O"))}";
        var p = await Json(await s.C.GetAsync($"{Rental}/{id}/donus-hesapla?{q}"));
        Assert.True(p.GetProperty("ok").GetBoolean(), p.ToString());
        Assert.Equal(300, p.GetProperty("kullanilanKm").GetInt32());
        Assert.Equal(0m, Dec(p, "uzatmaBedeli"));
        Assert.Equal(60m, Dec(p, "ekHizmetToplam"));
        Assert.Equal(540m, Dec(p, "yeniGenelToplam"));
        Assert.Equal(540m, Dec(p, "kalan"));
        var hala = await Json(await s.C.GetAsync($"{Rental}/{id}"));
        Assert.Equal("Kirada", hala.GetProperty("kira").GetProperty("durum").GetString());

        // Dönüş
        var r = await Json(await Gonder(s, HttpMethod.Post, $"{Rental}/{id}/donus",
            new { donusKm = 1300, donusYakit = 8, gercekDonus = newEnd }));
        Assert.Equal("Tamamlandi", r.GetProperty("durum").GetString());
        Assert.Equal(1300, r.GetProperty("donusKm").GetInt32());
        Assert.Equal(540m, Dec(r, "genelToplam"));
        Assert.Equal(540m, Dec(r, "bakiye"));
        Assert.Equal(0, r.GetProperty("uzatmaGun").GetInt32());
    }

    [Fact]
    public async Task Gec_donus_uzatma_bedeli_oracle()
    {
        var o = await SetUpEnvironmentAsync();
        var vehicle = await VehicleAsync(o);
        var s = await LoginAsync(o, Kim.OperatorA);
        var start = Now().AddDays(-4);
        var id = await OpenRentalAsync(s, o, vehicle, start, day: 2);
        await Json(await Gonder(s, HttpMethod.Post, $"{Rental}/{id}/teslim", new { cikisKm = 500, cikisYakit = 8 }));

        // ORACLE: 2 gün × 120 brüt = 240; 25 saat geç → 24 saatlik blok yukarı → 2 gün × 120 = 240 uzatma → 480.
        var actual = start.AddDays(2).AddHours(25);
        var r = await Json(await Gonder(s, HttpMethod.Post, $"{Rental}/{id}/donus",
            new { donusKm = 600, donusYakit = 8, gercekDonus = actual }));
        Assert.Equal(2, r.GetProperty("uzatmaGun").GetInt32());
        Assert.Equal(240m, Dec(r, "uzatmaBedeli"));
        Assert.Equal(480m, Dec(r, "genelToplam"));
    }

    [Fact]
    public async Task Ayni_arac_ayni_tarih_ikinci_kira_409_cakisma()
    {
        var o = await SetUpEnvironmentAsync();
        var vehicle = await VehicleAsync(o);
        var s = await LoginAsync(o, Kim.Admin);
        var start = Now().AddHours(2);
        await OpenRentalAsync(s, o, vehicle, start);
        var r = await Gonder(s, HttpMethod.Post, Rental, new
        {
            musteriId = o.MusteriId, vehicleId = vehicle, basTar = start.AddDays(1), bitTar = start.AddDays(2), gunlukUcret = 100m,
        });
        await ExpectProblem(r, HttpStatusCode.Conflict, UiError.ConflictCode);
    }

    // ------------------------------------------------------------ güncelleme (whitelist + required)

    private static readonly string[] UpdateFields = typeof(KiraGuncelleIstegi)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).ToArray();

    [Fact]
    public async Task Guncelle_tam_govde_yazar_eksik_alan_400_para_alani_tipte_yok()
    {
        var o = await SetUpEnvironmentAsync();
        var vehicle = await VehicleAsync(o);
        var s = await LoginAsync(o, Kim.OperatorA);
        var id = await OpenRentalAsync(s, o, vehicle, Now().AddHours(3));

        var rental = (await Json(await s.C.GetAsync($"{Rental}/{id}"))).GetProperty("kira");
        var body = new JsonObject();
        foreach (var alan in UpdateFields)
            body[alan] = JsonNode.Parse(rental.GetProperty(alan).GetRawText());
        body["aciklama"] = "F41 not";
        body["kmLimit"] = 1000;
        body["fazlaKmUcret"] = 2.5m;
        // Whitelist: gövdeye para/tarih alanı koymak onları DEĞİŞTİRMEZ (tipte yoklar; yok sayılır).
        body["tutar"] = 1m;
        body["genelToplam"] = 1m;
        body["bitTar"] = Now().AddDays(30);

        var g = await Json(await Gonder(s, HttpMethod.Put, $"{Rental}/{id}", body));
        Assert.Equal("F41 not", g.GetProperty("aciklama").GetString());
        Assert.Equal(1000, g.GetProperty("kmLimit").GetInt32());
        Assert.Equal(2.5m, Dec(g, "fazlaKmUcret"));
        Assert.Equal(360m, Dec(g, "tutar"));
        Assert.Equal(360m, Dec(g, "genelToplam"));
        Assert.Equal(rental.GetProperty("bitTar").GetDateTimeOffset(), g.GetProperty("bitTar").GetDateTimeOffset());

        // Eksik alan (kmLimit) → 400: tam değiştirmede unutulan alan sessizce 0'a (sınırsız km) düşmez.
        body.Remove("kmLimit");
        await ExpectProblem(await Gonder(s, HttpMethod.Put, $"{Rental}/{id}", body), HttpStatusCode.BadRequest, UiError.Validation);
        var after = (await Json(await s.C.GetAsync($"{Rental}/{id}"))).GetProperty("kira");
        Assert.Equal(1000, after.GetProperty("kmLimit").GetInt32());
    }

    // ------------------------------------------------------------ alan bazlı doğrulama

    [Fact]
    public async Task Alan_bazli_400_errors_alan_tasir()
    {
        var o = await SetUpEnvironmentAsync();
        var vehicle = await VehicleAsync(o);
        var s = await LoginAsync(o, Kim.OperatorA);
        var start = Now().AddHours(-2);
        var id = await OpenRentalAsync(s, o, vehicle, start);

        async Task Alan(HttpResponseMessage r, string alan)
        {
            var p = await ExpectProblem(r, HttpStatusCode.BadRequest, UiError.Validation);
            Assert.True(p.TryGetProperty("errors", out var e) && e.TryGetProperty(alan, out _), $"errors.{alan} yok: {p}");
        }

        await Alan(await Gonder(s, HttpMethod.Post, $"{Rental}/{id}/teslim", new { cikisKm = -5, cikisYakit = 8 }), "cikisKm");
        await Json(await Gonder(s, HttpMethod.Post, $"{Rental}/{id}/teslim", new { cikisKm = 1000, cikisYakit = 8 }));
        await Alan(await Gonder(s, HttpMethod.Post, $"{Rental}/{id}/donus",
            new { donusKm = 900, donusYakit = 8, gercekDonus = start.AddDays(3) }), "donusKm");
        await Alan(await Gonder(s, HttpMethod.Post, $"{Rental}/{id}/donus",
            new { donusKm = 1100, donusYakit = 8, gercekDonus = start.AddDays(-1) }), "gercekDonus");
        await Alan(await Gonder(s, HttpMethod.Post, $"{Rental}/{id}/uzat", new { yeniBitTar = start.AddDays(1) }), "yeniBitTar");
        await Alan(await Gonder(s, HttpMethod.Post, Rental, new
        {
            musteriId = o.MusteriId, vehicleId = vehicle, basTar = start.AddDays(10), bitTar = start.AddDays(9), gunlukUcret = 100m,
        }), "bitTar");
        await Alan(await Gonder(s, HttpMethod.Post, $"{Rental}/musteri", new { soyad = "Yalnız" }), "ad");
        await Alan(await s.C.GetAsync($"{Rental}?sirala=olmayanAlan"), "sirala");
        // Kapsam/yetki hataları alan ALMAZ (kodları korunur) — eşleme yalnız düz ValidationException'da.
        Assert.Null(FieldMapping.Find("Bu kayıt şube kapsamınız dışında.", [("Dönüş KM", "donusKm")]));
    }

    // ------------------------------------------------------------ yetki + şube kapsamı

    [Fact]
    public async Task Sube_B_operatoru_A_kirasina_ve_alt_kayitlarina_403_listede_gormez()
    {
        var o = await SetUpEnvironmentAsync();
        var vehicle = await VehicleAsync(o);
        var a = await LoginAsync(o, Kim.OperatorA);
        // Gecikmiş dönüş (bitiş 2 gün önce): panelin "gec" kovasında A görür, B görmez.
        var id = await OpenRentalAsync(a, o, vehicle, Now().AddDays(-5), gps: true);
        var itemId = (await Json(await a.C.GetAsync($"{Rental}/{id}"))).GetProperty("ekHizmetler")[0].GetProperty("id").GetGuid();

        var b = await LoginAsync(o, Kim.OperatorB);
        foreach (var path in new[] { "", "/faturalar", "/cezalar", "/dis-hizmetler", "/kaynak-rezervasyon", "/donem-plani", "/paylasim",
                     "/donus-hesapla?donusKm=1&donusYakit=1&gercekDonus=2030-01-01T00:00:00Z" })
            await ExpectProblem(await b.C.GetAsync($"{Rental}/{id}{path}"), HttpStatusCode.Forbidden, UiError.Forbidden);
        await ExpectProblem(await b.C.GetAsync($"{Rental}/hesapla?basTar=2030-01-01T00:00:00Z&bitTar=2030-01-02T00:00:00Z&gunlukUcret=100&rentalId={id}"),
            HttpStatusCode.Forbidden, UiError.Forbidden);

        await ExpectProblem(await Gonder(b, HttpMethod.Post, $"{Rental}/{id}/teslim", new { cikisKm = 1, cikisYakit = 1 }),
            HttpStatusCode.Forbidden, UiError.Forbidden);
        await ExpectProblem(await Gonder(b, HttpMethod.Post, $"{Rental}/{id}/uzat", new { yeniBitTar = Now().AddDays(1) }),
            HttpStatusCode.Forbidden, UiError.Forbidden);
        await ExpectProblem(await Gonder(b, HttpMethod.Post, $"{Rental}/{id}/provizyon/al"), HttpStatusCode.Forbidden, UiError.Forbidden);
        await ExpectProblem(await Gonder(b, HttpMethod.Post, $"{Rental}/{id}/ek-hizmetler", new { ekHizmetTanimId = o.EkHizmetId, miktar = 1m }),
            HttpStatusCode.Forbidden, UiError.Forbidden);
        await ExpectProblem(await Gonder(b, HttpMethod.Delete, $"{Rental}/{id}/ek-hizmetler/{itemId}"), HttpStatusCode.Forbidden, UiError.Forbidden);
        await ExpectProblem(await Gonder(b, HttpMethod.Delete, $"{Rental}/{id}/paylasim"), HttpStatusCode.Forbidden, UiError.Forbidden);

        var list = await Json(await b.C.GetAsync(Rental));
        Assert.DoesNotContain(list.GetProperty("kayitlar").EnumerateArray(), r => r.GetProperty("id").GetGuid() == id);
        var panel = await Json(await b.C.GetAsync(V1 + "/panel/ozet"));
        Assert.DoesNotContain(panel.GetProperty("donusler").GetProperty("gecikmis").EnumerateArray(), r => r.GetProperty("rentalId").GetGuid() == id);
        var panelA = await Json(await a.C.GetAsync(V1 + "/panel/ozet"));
        Assert.Contains(panelA.GetProperty("donusler").GetProperty("gecikmis").EnumerateArray(), r => r.GetProperty("rentalId").GetGuid() == id);

        // A'nın kirası değişmedi (yazma denemeleri hiçbir şey yazmadı).
        var d = (await Json(await a.C.GetAsync($"{Rental}/{id}"))).GetProperty("kira");
        Assert.True(d.GetProperty("cikisKm").ValueKind == JsonValueKind.Null);
        Assert.Equal(420m, Dec(d, "genelToplam"));
        Assert.Contains((await Json(await a.C.GetAsync(Rental))).GetProperty("kayitlar").EnumerateArray(), r => r.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Iptal_dar_izin_operator_403_yonetici_iptal_eder()
    {
        var o = await SetUpEnvironmentAsync();
        var vehicle = await VehicleAsync(o);
        var op = await LoginAsync(o, Kim.OperatorA);
        var id = await OpenRentalAsync(op, o, vehicle, Now().AddHours(5));

        await ExpectProblem(await Gonder(op, HttpMethod.Post, $"{Rental}/{id}/iptal"), HttpStatusCode.Forbidden, UiError.Forbidden);
        Assert.Equal("Kirada", (await Json(await op.C.GetAsync($"{Rental}/{id}"))).GetProperty("kira").GetProperty("durum").GetString());

        var name = await LoginAsync(o, Kim.Admin);
        var r = await Json(await Gonder(name, HttpMethod.Post, $"{Rental}/{id}/iptal"));
        Assert.Equal("Iptal", r.GetProperty("durum").GetString());
        // İkinci iptal: yapısal 400 (durum geçişi)
        await ExpectProblem(await Gonder(name, HttpMethod.Post, $"{Rental}/{id}/iptal"), HttpStatusCode.BadRequest, UiError.Validation);
    }

    [Fact]
    public async Task Muhasebe_okur_ama_operasyon_yazamaz_karne_yalniz_finans()
    {
        var o = await SetUpEnvironmentAsync();
        var vehicle = await VehicleAsync(o);
        var op = await LoginAsync(o, Kim.OperatorA);
        var id = await OpenRentalAsync(op, o, vehicle, Now().AddHours(6));

        var mu = await LoginAsync(o, Kim.Muhasebe);
        var d = await Json(await mu.C.GetAsync($"{Rental}/{id}"));
        Assert.True(d.GetProperty("yetkiler").GetProperty("finans").GetBoolean());
        Assert.False(d.GetProperty("yetkiler").GetProperty("operasyon").GetBoolean());
        Assert.True(d.GetProperty("paylasim").ValueKind == JsonValueKind.Null); // paylaşım barı yalnız OperationsWrite
        await Json(await mu.C.GetAsync($"{Rental}/{id}/faturalar"));
        await Json(await mu.C.GetAsync($"{Rental}/{id}/karne-ozeti"));
        await ExpectProblem(await Gonder(mu, HttpMethod.Post, $"{Rental}/{id}/teslim", new { cikisKm = 1, cikisYakit = 1 }),
            HttpStatusCode.Forbidden, UiError.Forbidden);
        await ExpectProblem(await mu.C.GetAsync($"{Rental}/hesapla?basTar=2030-01-01T00:00:00Z&bitTar=2030-01-02T00:00:00Z"),
            HttpStatusCode.Forbidden, UiError.Forbidden);

        await ExpectProblem(await op.C.GetAsync($"{Rental}/{id}/karne-ozeti"), HttpStatusCode.Forbidden, UiError.Forbidden);
    }

    /// <summary>F13.1b: pilot kapısı kalktı — bayrağı kapalı firma da kira listesini ve paneli okur.</summary>
    [Fact]
    public async Task Pilot_bayragi_kapali_firma_da_kira_ve_panel_okur()
    {
        var o = await SetUpEnvironmentAsync(pilot: false);
        var s = await LoginAsync(o, Kim.Admin);
        Assert.Equal(HttpStatusCode.OK, (await s.C.GetAsync(Rental)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.C.GetAsync(V1 + "/panel/ozet")).StatusCode);
    }

    [Fact]
    public async Task Baska_kiracinin_kirasi_404()
    {
        var o = await SetUpEnvironmentAsync();
        var vehicle = await VehicleAsync(o);
        var id = await OpenRentalAsync(await LoginAsync(o, Kim.Admin), o, vehicle, Now().AddHours(7));
        var other = await SetUpEnvironmentAsync();
        var s = await LoginAsync(other, Kim.Admin);
        var r = await s.C.GetAsync($"{Rental}/{id}");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.C.GetAsync($"{Rental}/{id}/faturalar")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Gonder(s, HttpMethod.Post, $"{Rental}/{id}/iptal")).StatusCode);
    }

    // ------------------------------------------------------------ canlı hesap paritesi

    [Fact]
    public async Task Hesapla_oracle()
    {
        var o = await SetUpEnvironmentAsync();
        var vehicle = await VehicleAsync(o);
        var s = await LoginAsync(o, Kim.OperatorA);
        var start = Now().AddDays(2);
        var q = $"vehicleId={vehicle}&basTar={Uri.EscapeDataString(start.ToString("O"))}&bitTar={Uri.EscapeDataString(start.AddDays(3).ToString("O"))}" +
                $"&gunlukUcret=100&fiyatTuru={Uri.EscapeDataString("Günlük")}&musteriId={o.MusteriId}&ek={o.EkHizmetId}:1";

        // F13.1a: Blazor GET /kiralar/hesapla silindi (parite karşılaştırması anlamını yitirdi); motor aynı
        // (RentalCalculationService), değerler aşağıdaki elle kurulmuş oracle'la kilitli.
        var api = await Json(await s.C.GetAsync($"{Rental}/hesapla?{q}"));
        // Uç yok: F13.1b'den beri gövdesiz 404 SPA Panel'ine bulunamadı bandıyla gider (JSON değil).
        var gone = await s.C.GetAsync($"/kiralar/hesapla?{q}");
        Assert.StartsWith("/app/panel?hata=", gone.Headers.Location?.OriginalString);

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
        var corrupt = await s.C.GetAsync($"{Rental}/hesapla?basTar={Uri.EscapeDataString(start.ToString("O"))}&bitTar={Uri.EscapeDataString(start.AddDays(1).ToString("O"))}&ek=bozuk");
        var p = await ExpectProblem(corrupt, HttpStatusCode.BadRequest, UiError.Validation);
        Assert.True(p.GetProperty("errors").TryGetProperty("ek", out _));
    }

    // ------------------------------------------------------------ TahsilatAnahtar

    [Fact]
    public async Task Tahsilat_anahtari_liste_ve_panelde_ayni_islem_sonrasi_degisir_tekrari_mukerrer()
    {
        var o = await SetUpEnvironmentAsync();
        var vehicle = await VehicleAsync(o);
        var name = await LoginAsync(o, Kim.Admin);
        // Gecikmiş dönüş: panelde "gec" kovasına düşer, Blazor Home varsayılan olarak o sekmeyi açar.
        var id = await OpenRentalAsync(name, o, vehicle, Now().AddDays(-5), day: 3);

        async Task<JsonElement> ListRow(Oturum s)
            => (await Json(await s.C.GetAsync(Rental))).GetProperty("kayitlar").EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id);

        var row = await ListRow(name);
        var th = row.GetProperty("tahsilat");
        var k1 = th.GetProperty("anahtar").GetGuid();
        Assert.Equal(o.MusteriId, th.GetProperty("cariId").GetGuid());
        Assert.Equal(id, th.GetProperty("rentalId").GetGuid());
        Assert.Equal("TRY", th.GetProperty("doviz").GetString());
        Assert.Equal(360m, Dec(th, "varsayilanTutar"));

        // F13.1a: Blazor kira listesi/panosu silindi; anahtarın deterministik olduğu (liste = panel = sunucunun yeniden
        // hesabı) aşağıda API ile ve mükerrer tekrarla kilitli.

        var panel = await Json(await name.C.GetAsync(V1 + "/panel/ozet"));
        var returnInfo = panel.GetProperty("donusler");
        Assert.Equal("gec", returnInfo.GetProperty("varsayilanSekme").GetString());
        var ps = returnInfo.GetProperty("gecikmis").EnumerateArray().Single(r => r.GetProperty("rentalId").GetGuid() == id);
        Assert.Equal(k1, ps.GetProperty("tahsilat").GetProperty("anahtar").GetGuid());
        Assert.True(panel.GetProperty("finans").ValueKind == JsonValueKind.Object);

        // Anahtar gerçek idempotency anahtarı: onunla tahsilat → ikinci gönderim 409 mükerrer (E01).
        using (var host = new TestHost(fx.Pg.AppConnectionString))
        {
            using (var scope = host.ScopeFor(o.TenantId))
                await scope.ServiceProvider.GetRequiredService<CashService>()
                    .CollectAsync(new CashInput { CariId = o.MusteriId, RentalId = id, Tutar = 100m, IslemAnahtari = k1 });
            using (var scope = host.ScopeFor(o.TenantId))
                await Assert.ThrowsAsync<DuplicateOperationException>(() => scope.ServiceProvider.GetRequiredService<CashService>()
                    .CollectAsync(new CashInput { CariId = o.MusteriId, RentalId = id, Tutar = 100m, IslemAnahtari = k1 }));
        }

        // Tahsilat sonrası: bakiye 360 − 100 = 260, işlem sayısı arttı → YENİ anahtar (ikinci meşru tahsilat engellenmez).
        var after = await ListRow(name);
        Assert.Equal(260m, Dec(after, "bakiye"));
        var k2 = after.GetProperty("tahsilat").GetProperty("anahtar").GetGuid();
        Assert.NotEqual(k1, k2);
        // Panel de aynı yeni anahtarı verir (liste = panel, tek kural).
        var panel2 = await Json(await name.C.GetAsync(V1 + "/panel/ozet"));
        Assert.Equal(k2, panel2.GetProperty("donusler").GetProperty("gecikmis").EnumerateArray()
            .Single(r => r.GetProperty("rentalId").GetGuid() == id).GetProperty("tahsilat").GetProperty("anahtar").GetGuid());

        // Finans yetkisi olmayan operatörde tahsilat verisi ve finans özeti YOK.
        var op = await LoginAsync(o, Kim.OperatorA);
        Assert.True((await ListRow(op)).GetProperty("tahsilat").ValueKind == JsonValueKind.Null);
        var opPanel = await Json(await op.C.GetAsync(V1 + "/panel/ozet"));
        Assert.True(opPanel.GetProperty("finans").ValueKind == JsonValueKind.Null);
        Assert.True(opPanel.GetProperty("donusler").GetProperty("gecikmis").EnumerateArray()
            .Single(r => r.GetProperty("rentalId").GetGuid() == id).GetProperty("tahsilat").ValueKind == JsonValueKind.Null);
    }

    // ------------------------------------------------------------ liste, ek hizmet, müşteri, paylaşım, varsayılanlar

    [Fact]
    public async Task Liste_sayfalama_siralama_filtre()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Admin);
        var start = Now().AddDays(10);
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++) ids.Add(await OpenRentalAsync(s, o, await VehicleAsync(o), start.AddDays(i * 7), day: i + 1));

        var s1 = await Json(await s.C.GetAsync($"{Rental}?boyut=2&sayfa=1&sirala=-tutar"));
        Assert.Equal(3, s1.GetProperty("toplam").GetInt32());
        var amounts = s1.GetProperty("kayitlar").EnumerateArray().Select(r => Dec(r, "tutar")).ToList();
        Assert.Equal([360m, 240m], amounts); // 3×120, 2×120 (azalan)
        var s2 = await Json(await s.C.GetAsync($"{Rental}?boyut=2&sayfa=2&sirala=-tutar"));
        Assert.Equal([120m], s2.GetProperty("kayitlar").EnumerateArray().Select(r => Dec(r, "tutar")).ToList());

        await Json(await Gonder(s, HttpMethod.Post, $"{Rental}/{ids[0]}/iptal"));
        var onRent = await Json(await s.C.GetAsync($"{Rental}?durum=Kirada"));
        Assert.Equal(2, onRent.GetProperty("toplam").GetInt32());
        Assert.Equal(1, (await Json(await s.C.GetAsync($"{Rental}?durum=iptal"))).GetProperty("toplam").GetInt32());
        // Sayı ya da tanımsız ad sessizce "filtre yok"a düşmez: 400 + alan.
        foreach (var corrupt in new[] { "1", "Yok" })
        {
            var p = await ExpectProblem(await s.C.GetAsync($"{Rental}?durum={corrupt}"), HttpStatusCode.BadRequest, UiError.Validation);
            Assert.True(p.GetProperty("errors").TryGetProperty("durum", out _));
        }
        var summary = await Json(await s.C.GetAsync($"{Rental}/ozet"));
        Assert.Equal(3, summary.GetProperty("toplam").GetInt32());
        Assert.Equal(2, summary.GetProperty("kirada").GetInt32());
    }

    /// <summary>F4.2 — SPA listesinin dışa aktarması (<c>/listeler/export/kiralar</c>) ekrandaki süzgeci taşır;
    /// parametresiz çağrı (Blazor bağlantısı) eskisi gibi hepsi. Oracle: 3 kira açıldı, 1'i iptal edildi.</summary>
    [Fact]
    public async Task Export_kiralar_ekrandaki_suzgeci_tasir_parametresiz_hepsi()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Admin);
        var start = Now().AddDays(40);
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++) ids.Add(await OpenRentalAsync(s, o, await VehicleAsync(o), start.AddDays(i * 7)));
        await Json(await Gonder(s, HttpMethod.Post, $"{Rental}/{ids[0]}/iptal"));

        static async Task<int> DataRowAsync(HttpResponseMessage r)
        {
            var text = await r.Content.ReadAsStringAsync();
            Assert.True(r.StatusCode == HttpStatusCode.OK, $"{(int)r.StatusCode}: {text}");
            Assert.Equal("text/csv", r.Content.Headers.ContentType?.MediaType);
            return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length - 1;
        }

        const string Endpoint = "/listeler/export/kiralar?format=csv";
        Assert.Equal(3, await DataRowAsync(await s.C.GetAsync(Endpoint)));
        Assert.Equal(1, await DataRowAsync(await s.C.GetAsync(Endpoint + "&durum=Iptal")));
        // Sayfa taşınmaz: dosya filtreye uyan TÜM kayıtlar (2 kirada), ekrandaki sayfa değil.
        Assert.Equal(2, await DataRowAsync(await s.C.GetAsync(Endpoint + "&durum=Kirada&sayfa=2&boyut=1")));
        // API ile aynı gün kuralı: son kiranın başlangıç günü ve sonrası → 1.
        var lastDay = start.AddDays(14).ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd");
        Assert.Equal(1, await DataRowAsync(await s.C.GetAsync(Endpoint + "&basMin=" + lastDay)));

        // Tanımsız durum adı sessizce "hepsi"ne düşmez (yanlış dosya indirilmez): doğrulama hatası (F13.1b: SPA Panel +
        // hata bandı).
        var corrupt = await s.C.GetAsync(Endpoint + "&durum=Yok");
        Assert.Equal(HttpStatusCode.Redirect, corrupt.StatusCode);
        Assert.StartsWith("/app/panel?hata=", corrupt.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Ek_hizmet_ekle_sil_toplam_ve_yabanci_kalem_404()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.OperatorA);
        var id = await OpenRentalAsync(s, o, await VehicleAsync(o), Now().AddHours(8));
        var other = await OpenRentalAsync(s, o, await VehicleAsync(o), Now().AddHours(8), gps: true);

        // ORACLE: 2 × GPS (50 net) = 100 net + 20 KDV = 120 → genel 360 + 120 = 480.
        var e = await Json(await Gonder(s, HttpMethod.Post, $"{Rental}/{id}/ek-hizmetler", new { ekHizmetTanimId = o.EkHizmetId, miktar = 2m }));
        Assert.Equal(120m, Dec(e.GetProperty("kalemler")[0], "toplam"));
        Assert.Equal(480m, Dec(e.GetProperty("kira"), "genelToplam"));
        var item = e.GetProperty("kalemler")[0].GetProperty("id").GetGuid();

        // Başka kiranın kalemi bu rotadan silinemez.
        var foreign = (await Json(await s.C.GetAsync($"{Rental}/{other}"))).GetProperty("ekHizmetler")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await Gonder(s, HttpMethod.Delete, $"{Rental}/{id}/ek-hizmetler/{foreign}")).StatusCode);
        Assert.Equal(420m, Dec((await Json(await s.C.GetAsync($"{Rental}/{other}"))).GetProperty("kira"), "genelToplam"));

        var remove = await Json(await Gonder(s, HttpMethod.Delete, $"{Rental}/{id}/ek-hizmetler/{item}"));
        Assert.Equal(0, remove.GetProperty("kalemler").GetArrayLength());
        Assert.Equal(360m, Dec(remove.GetProperty("kira"), "genelToplam"));
    }

    [Fact]
    public async Task Sistem_ucret_kalemi_manuel_eklenemez_silinemez()
    {
        var o = await SetUpEnvironmentAsync();
        var sys = new EkHizmetTanim { Kod = "SYS-DROP", Ad = "Drop ücreti", BirimUcret = 100m, KdvOrani = 0.20m, Aktif = true };
        await WriteDataAsync(o.TenantId, db => db.EkHizmetTanimlari.Add(sys));
        var s = await LoginAsync(o, Kim.Admin);
        var vehicle = await VehicleAsync(o);
        var start = Now().AddDays(60);

        // Kira açılmadan temiz red (yarım kayıt yok).
        await ExpectProblem(await Gonder(s, HttpMethod.Post, Rental, new
        {
            musteriId = o.MusteriId, vehicleId = vehicle, basTar = start, bitTar = start.AddDays(1), gunlukUcret = 100m,
            ekHizmetler = new[] { new { tanimId = sys.Id, miktar = 1m } },
        }), HttpStatusCode.BadRequest, UiError.Validation);
        Assert.Equal(0, (await Json(await s.C.GetAsync(Rental))).GetProperty("toplam").GetInt32());

        // Sistem satırı (FeeLineService'in yazdığı gibi) varsa bu uçtan silinemez; toplam korunur.
        var id = await OpenRentalAsync(s, o, vehicle, start, day: 1);
        using (var host = new TestHost(fx.Pg.AppConnectionString))
        using (var scope = host.ScopeFor(o.TenantId))
            await scope.ServiceProvider.GetRequiredService<RentACar.Application.RentalAddOns.RentalAddOnService>()
                .AddAsync(id, sys.Id, 1m, system: true);
        var d = await Json(await s.C.GetAsync($"{Rental}/{id}"));
        var general = Dec(d.GetProperty("kira"), "genelToplam");
        Assert.Equal(120m + 120m, general); // 1 × 120 brüt + SYS 100 net + 20 KDV
        var item = d.GetProperty("ekHizmetler")[0].GetProperty("id").GetGuid();
        await ExpectProblem(await Gonder(s, HttpMethod.Delete, $"{Rental}/{id}/ek-hizmetler/{item}"), HttpStatusCode.BadRequest, UiError.Validation);
        Assert.Equal(general, Dec((await Json(await s.C.GetAsync($"{Rental}/{id}"))).GetProperty("kira"), "genelToplam"));
    }

    private static string ValidNationalId()
    {
        var d = new int[11];
        d[0] = Random.Shared.Next(1, 10);
        for (var i = 1; i < 9; i++) d[i] = Random.Shared.Next(0, 10);
        var tek = d[0] + d[2] + d[4] + d[6] + d[8];
        var duplicate = d[1] + d[3] + d[5] + d[7];
        d[9] = (((tek * 7) - duplicate) % 10 + 10) % 10;
        d[10] = d.Take(10).Sum() % 10;
        return string.Concat(d);
    }

    [Fact]
    public async Task Hizli_musteri_yalniz_kimlik_ve_etiket_doner_tc_tekrari_409()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.OperatorA);
        var nationalId = ValidNationalId();
        var r = await Gonder(s, HttpMethod.Post, $"{Rental}/musteri", new { ad = "Can", soyad = "Er", tcKimlik = nationalId, cepTel = "05551234567", ehliyetNo = "EHL-99" });
        var text = await r.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = JsonDocument.Parse(text).RootElement;
        Assert.Equal(["etiket", "id"], j.EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.Equal("Can Er", j.GetProperty("etiket").GetString());
        Assert.DoesNotContain(nationalId, text);
        Assert.DoesNotContain("EHL-99", text);
        Assert.DoesNotContain("05551234567", text);

        await ExpectProblem(await Gonder(s, HttpMethod.Post, $"{Rental}/musteri", new { ad = "Başka", tcKimlik = nationalId }),
            HttpStatusCode.Conflict, UiError.ConflictCode);
    }

    [Fact]
    public async Task Provizyon_al_kapat_ve_form_varsayilanlari_musait_arac()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.OperatorA);
        var vehicle = await VehicleAsync(o);
        var start = Now().AddDays(40);
        var r = await Gonder(s, HttpMethod.Post, Rental, new
        {
            musteriId = o.MusteriId, vehicleId = vehicle, basTar = start, bitTar = start.AddDays(2), gunlukUcret = 100m, provizyon = 500m,
            cikisOfisi = "SubeA",
        });
        var id = (await Json(r, HttpStatusCode.Created)).GetProperty("id").GetGuid();

        var get = await Json(await Gonder(s, HttpMethod.Post, $"{Rental}/{id}/provizyon/al"));
        Assert.Equal("Alindi", get.GetProperty("provizyonDurum").GetString());
        await ExpectProblem(await Gonder(s, HttpMethod.Post, $"{Rental}/{id}/provizyon/al"), HttpStatusCode.BadRequest, UiError.Validation);
        var close = await Json(await Gonder(s, HttpMethod.Post, $"{Rental}/{id}/provizyon/kapat", new { kapamaTutar = 300m }));
        Assert.Equal("Kapandi", close.GetProperty("provizyonDurum").GetString());
        Assert.Equal(300m, Dec(close, "provizyonKapamaTutar"));
        Assert.Equal(200m, Dec(close, "bakiye")); // provizyon deftere/bakiyeye yazmaz: 2 × 100 brüt (varsayılan mod)

        var v = await Json(await s.C.GetAsync($"{Rental}/form-varsayilanlari"));
        Assert.Equal(8, v.GetProperty("cikisYakit").GetInt32());
        Assert.Contains("Günlük", v.GetProperty("fiyatTurleri").EnumerateArray().Select(x => x.GetString()));

        // Müsait araç: kiralı araç o aralıkta yok, boş araç var.
        var empty = await VehicleAsync(o);
        var day = DateOnly.FromDateTime(start.UtcDateTime);
        var m = await Json(await s.C.GetAsync($"{Rental}/musait-arac?vfrom={day:yyyy-MM-dd}&vto={day.AddDays(1):yyyy-MM-dd}"));
        var ids = m.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(empty, ids);
        Assert.DoesNotContain(vehicle, ids);
        await ExpectProblem(await s.C.GetAsync($"{Rental}/musait-arac?vfrom={day:yyyy-MM-dd}&vto={day:yyyy-MM-dd}"),
            HttpStatusCode.BadRequest, UiError.Validation);
    }

    [Fact]
    public async Task Paylasim_iptal_kapsam_kapisindan_gecer_ve_link_yoksa_false()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Admin);
        var id = await OpenRentalAsync(s, o, await VehicleAsync(o), Now().AddHours(9));
        var d = await Json(await s.C.GetAsync($"{Rental}/{id}/paylasim"));
        Assert.True(d.GetProperty("link").ValueKind == JsonValueKind.Null);
        var cancel = await Json(await Gonder(s, HttpMethod.Delete, $"{Rental}/{id}/paylasim"));
        Assert.False(cancel.GetProperty("iptalEdildi").GetBoolean());
        var share = await Json(await Gonder(s, HttpMethod.Post, $"{Rental}/{id}/paylasim"));
        var path = share.GetProperty("link").GetProperty("yol").GetString();
        Assert.StartsWith("/sozlesme/", path);
        // Paylaş tekrarı AYNI linki döner (müşterinin elindeki adres bozulmaz).
        Assert.Equal(path, (await Json(await Gonder(s, HttpMethod.Post, $"{Rental}/{id}/paylasim"))).GetProperty("link").GetProperty("yol").GetString());
        Assert.True((await Json(await Gonder(s, HttpMethod.Delete, $"{Rental}/{id}/paylasim"))).GetProperty("iptalEdildi").GetBoolean());
    }

    // ------------------------------------------------------------ izin haritası (yapısal)

    private List<RouteEndpoint> RentalPanelEndpoints()
        => fx.Web.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => ("/" + (e.RoutePattern.RawText ?? "").TrimStart('/')) is var r
                        && (r.StartsWith(Rental, StringComparison.Ordinal) || r == V1 + "/panel/ozet"))
            .ToList();

    [Fact]
    public void Izin_haritasi_Blazor_ile_ayni()
    {
        static string Key(RouteEndpoint e)
            => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Single() ?? "?") + " " + "/" + e.RoutePattern.RawText!.Trim('/');
        var map = RentalPanelEndpoints().ToDictionary(Key, e => e);

        string Active(string key)
        {
            var e = map[key];
            if (e.Metadata.GetMetadata<IzinMuafMetadata>() is not null && e.Metadata.GetMetadata<IzinMetadata>() is null) return "muaf";
            return e.Metadata.GetMetadata<IzinMetadata>()?.Izin.ToString()
                   ?? string.Join("|", e.Metadata.GetMetadata<IzinlerdenBiriMetadata>()!.Izinler);
        }

        const string Read = "OperationsWrite|FinanceWrite";
        var expected = new Dictionary<string, string>
        {
            ["GET " + Rental] = Read,
            ["GET " + Rental + "/ozet"] = Read,
            ["GET " + Rental + "/filtre-secenekleri"] = Read,
            ["GET " + Rental + "/{id:guid}"] = Read,
            ["GET " + Rental + "/{id:guid}/faturalar"] = Read,
            ["GET " + Rental + "/{id:guid}/cezalar"] = Read,
            ["GET " + Rental + "/{id:guid}/dis-hizmetler"] = Read,
            ["GET " + Rental + "/{id:guid}/kaynak-rezervasyon"] = Read,
            ["GET " + Rental + "/{id:guid}/karne-ozeti"] = "FinanceWrite",
            ["GET " + Rental + "/{id:guid}/musteri-ozet"] = Read, // F4.3b
            ["GET " + Rental + "/ek-hizmet-katalogu"] = "OperationsWrite", // F4.3b
            ["GET " + Rental + "/form-varsayilanlari"] = "OperationsWrite",
            ["GET " + Rental + "/hesapla"] = "OperationsWrite",
            ["GET " + Rental + "/musait-arac"] = "OperationsWrite",
            ["GET " + Rental + "/{id:guid}/donus-hesapla"] = "OperationsWrite",
            ["GET " + Rental + "/{id:guid}/donem-plani"] = "OperationsWrite",
            ["GET " + Rental + "/{id:guid}/paylasim"] = "OperationsWrite",
            ["POST " + Rental] = "OperationsWrite",
            ["POST " + Rental + "/musteri"] = "OperationsWrite",
            ["PUT " + Rental + "/{id:guid}"] = "OperationsWrite",
            ["POST " + Rental + "/{id:guid}/teslim"] = "OperationsWrite",
            ["POST " + Rental + "/{id:guid}/donus"] = "OperationsWrite",
            ["POST " + Rental + "/{id:guid}/uzat"] = "OperationsWrite",
            ["POST " + Rental + "/{id:guid}/iptal"] = "OperationsDelete",
            ["POST " + Rental + "/{id:guid}/provizyon/al"] = "OperationsWrite",
            ["POST " + Rental + "/{id:guid}/provizyon/kapat"] = "OperationsWrite",
            ["POST " + Rental + "/{id:guid}/ek-hizmetler"] = "OperationsWrite",
            ["DELETE " + Rental + "/{id:guid}/ek-hizmetler/{kalemId:guid}"] = "OperationsWrite",
            ["POST " + Rental + "/{id:guid}/paylasim"] = "OperationsWrite",
            ["POST " + Rental + "/{id:guid}/paylasim/yeni-surum"] = "OperationsWrite",
            ["DELETE " + Rental + "/{id:guid}/paylasim"] = "OperationsWrite",
            ["GET " + V1 + "/panel/ozet"] = "muaf",
        };
        Assert.Equal(expected.Keys.OrderBy(x => x, StringComparer.Ordinal), map.Keys.OrderBy(x => x, StringComparer.Ordinal));
        foreach (var (key, permission) in expected)
            Assert.True(permission == Active(key), $"{key}: beklenen {permission}, gelen {Active(key)}");

        // Yazma uçları OKUMA kapısını da taşır (grup) — "izinlerden biri" + dar izin VE ile birleşir.
        Assert.All(map.Where(kv => kv.Key.Contains("/kiralar")).Select(kv => kv.Value),
            e => Assert.NotNull(e.Metadata.GetMetadata<IzinlerdenBiriMetadata>()));
    }

    [Fact]
    public void Alan_eslemesi_onek_ve_sira()
    {
        (string, string)[] rules = [("Dönüş KM", "donusKm"), ("Dönüş", "genel")];
        Assert.Equal("donusKm", FieldMapping.Find("Dönüş KM, çıkış KM'den küçük olamaz.", rules));
        Assert.Equal("genel", FieldMapping.Find("Dönüş tarihi başlangıçtan önce olamaz.", rules));
        Assert.Null(FieldMapping.Find("Başka bir hata.", rules));
        Assert.Throws<ArgumentException>(() => new RouteGroupBuilderStub().RequireAnyPermission(Permission.OperationsWrite));
    }

    // ================================================================== F4.1 adversarial kapanışları
    // Bağımsız inceleyicinin probe'ları (AdvF41ProbeTests P3/P4/P6/P7/P8/P9/P10/P11/P12) kalıcı teste çevrildi.

    private async Task<int> RentalCountAsync(Guid tenantId)
    {
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(tenantId);
        await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        return await db.Rentals.CountAsync();
    }

    private static async Task WaitForField(HttpResponseMessage r, string alan)
    {
        var p = await ExpectProblem(r, HttpStatusCode.BadRequest, UiError.Validation);
        Assert.True(p.TryGetProperty("errors", out var e) && e.TryGetProperty(alan, out _), $"errors.{alan} yok: {p}");
    }

    [Fact]
    public async Task M1_Operator_baska_subeye_ve_ofissiz_kira_yazamaz()
    {
        var o = await SetUpEnvironmentAsync();
        var vehicleB = await VehicleAsync(o, "SubeB");
        var a = await LoginAsync(o, Kim.OperatorA);
        var b = await LoginAsync(o, Kim.OperatorB);
        var start = Now().AddDays(10);

        // A, B şubesinin ofisiyle kira açamaz: 403, HİÇBİR ŞEY yazılmaz (B'nin aracı bloke olmaz).
        await ExpectProblem(await Gonder(a, HttpMethod.Post, Rental, new
        {
            musteriId = o.MusteriId, vehicleId = vehicleB, basTar = start, bitTar = start.AddDays(3), gunlukUcret = 1m, cikisOfisi = "SubeB",
        }), HttpStatusCode.Forbidden, UiError.Forbidden);
        // Şubeye bağlı operatör ofissiz ("yetim") kira açamaz: 400 errors.cikisOfisi.
        await WaitForField(await Gonder(a, HttpMethod.Post, Rental, new
        {
            musteriId = o.MusteriId, vehicleId = vehicleB, basTar = start, bitTar = start.AddDays(3), gunlukUcret = 100m,
        }), "cikisOfisi");
        Assert.Equal(0, await RentalCountAsync(o.TenantId));

        // B kendi aracını aynı tarihte kiralayabilir (A'nın denemesi bloke etmedi).
        await Json(await Gonder(b, HttpMethod.Post, Rental, new
        {
            musteriId = o.MusteriId, vehicleId = vehicleB, basTar = start.AddDays(1), bitTar = start.AddDays(2), gunlukUcret = 100m, cikisOfisi = "SubeB",
        }), HttpStatusCode.Created);

        // Kapsamsız rol (Admin) ofissiz açabilir (mevcut davranış — kapsam yok).
        var name = await LoginAsync(o, Kim.Admin);
        await Json(await Gonder(name, HttpMethod.Post, Rental, new
        {
            musteriId = o.MusteriId, vehicleId = await VehicleAsync(o), basTar = start, bitTar = start.AddDays(1), gunlukUcret = 100m,
        }), HttpStatusCode.Created);

        // Kök serviste: Blazor /kiralar/create ve harici API de aynı RentalService.CreateDirectAsync'ten geçer.
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(o.TenantId, Guid.NewGuid(), "op", UserRole.Operator, "SubeA");
        await Assert.ThrowsAsync<NoPermissionException>(() => scope.ServiceProvider.GetRequiredService<RentACar.Application.Bookings.RentalService>()
            .CreateDirectAsync(new RentACar.Application.Bookings.BookingInput
            {
                MusteriId = o.MusteriId, VehicleId = vehicleB, BasTar = start.AddDays(20), BitTar = start.AddDays(21), GunlukUcret = 100m, CikisOfisi = "SubeB",
            }));
        Assert.Equal(2, await RentalCountAsync(o.TenantId));
    }

    [Fact]
    public async Task M2_Esanli_donus_ile_ek_hizmet_ya_da_uzatma_tutarsizlik_uretmez()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Admin);
        var s2 = await LoginAsync(o, Kim.Admin);
        var violation = new List<string>();
        for (var i = 0; i < 10; i++)
        foreach (var type in new[] { "ek", "uzat" })
        {
            var start = Now().AddDays(-3).AddMinutes(-i);
            var id = await OpenRentalAsync(s, o, await VehicleAsync(o), start);
            // Eksik yakıt bedeli (4 × 100 = 400) — bayat yazım bu bileşeni düşürürse tutarsızlık görünür olsun.
            var g = new JsonObject();
            var rental0 = (await Json(await s.C.GetAsync($"{Rental}/{id}"))).GetProperty("kira");
            foreach (var alan in UpdateFields) g[alan] = JsonNode.Parse(rental0.GetProperty(alan).GetRawText());
            g["yakitBirimUcret"] = 100m;
            await Json(await Gonder(s, HttpMethod.Put, $"{Rental}/{id}", g));
            await Json(await Gonder(s, HttpMethod.Post, $"{Rental}/{id}/teslim", new { cikisKm = 1000, cikisYakit = 8 }));
            // Dönüş eski bitişten 12 saat SONRA, uzatılmış bitişten ÖNCE: sıra sonuçtan okunabilir
            // (uzatma önce → geç dönüş yok; dönüş önce → uzatma reddedilir, 1 gün geç dönüş bedeli).
            var returnInfo = Gonder(s, HttpMethod.Post, $"{Rental}/{id}/donus", new { donusKm = 1100, donusYakit = 4, gercekDonus = start.AddDays(3).AddHours(12) });
            var other = type == "ek"
                ? Gonder(s2, HttpMethod.Post, $"{Rental}/{id}/ek-hizmetler", new { ekHizmetTanimId = o.EkHizmetId, miktar = 1m })
                : Gonder(s2, HttpMethod.Post, $"{Rental}/{id}/uzat", new { yeniBitTar = start.AddDays(4) });
            var result = await Task.WhenAll(returnInfo, other);
            Assert.Equal(HttpStatusCode.OK, result[0].StatusCode);

            var d = await Json(await s.C.GetAsync($"{Rental}/{id}"));
            var k = d.GetProperty("kira");
            var extra = d.GetProperty("ekHizmetler").EnumerateArray().Sum(x => Dec(x, "toplam"));
            // ORACLE (bileşen tutarlılığı): Genel = Tutar + aşım + yakıt + geç dönüş + Σ ek hizmet; Bakiye = Genel − Tahsilat.
            var expected = Dec(k, "tutar") + Dec(k, "fazlaKmBedeli") + Dec(k, "yakitBedeli") + Dec(k, "uzatmaBedeli") + extra;
            var row = $"{type}#{i}: diger={(int)result[1].StatusCode} genel={Dec(k, "genelToplam")} beklenen={expected} bit={k.GetProperty("bitTar")}";
            if (Dec(k, "genelToplam") != expected || Dec(k, "bakiye") != Dec(k, "genelToplam") - Dec(k, "tahsilat")) violation.Add(row);
            if (Dec(k, "yakitBedeli") != 400m) violation.Add("yakıt bedeli kayboldu: " + row);
            if (type == "uzat")
            {
                // ORACLE: uzatma dönüşten ÖNCE → 4 gün (Tutar 480), geç dönüş yok, Genel 480 + 400 = 880.
                //         dönüş ÖNCE → uzatma reddedilir (Tamamlandı), 3 gün (360) + 1 gün geç (120) + 400 = 880.
                var extended = result[1].StatusCode == HttpStatusCode.OK;
                if (extended != (k.GetProperty("bitTar").GetDateTimeOffset() == start.AddDays(4))) violation.Add("bitiş/uzatma sonucu uyuşmuyor: " + row);
                if (Dec(k, "tutar") != (extended ? 480m : 360m)) violation.Add("tutar: " + row);
                if (Dec(k, "uzatmaBedeli") != (extended ? 0m : 120m)) violation.Add("geç dönüş: " + row);
                if (Dec(k, "genelToplam") != 880m) violation.Add("genel: " + row);
            }
            else
            {
                // ORACLE: 360 + 1 gün geç (120) + yakıt 400 = 880; ek hizmet eklendiyse + 60.
                if (result[1].StatusCode == HttpStatusCode.OK && extra != 60m) violation.Add("ek hizmet kaybı: " + row);
                if (Dec(k, "genelToplam") != 880m + extra) violation.Add("genel: " + row);
            }
        }
        Assert.True(violation.Count == 0, $"{violation.Count} yarışta tutarsızlık:\n" + string.Join("\n", violation));
    }

    private static async Task<int> LedgerLineAsync(TestHost host, Guid tenantId)
    {
        using var scope = host.ScopeFor(tenantId);
        await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        return await db.AccountLedgerEntries.CountAsync();
    }

    [Fact]
    public async Task M3_Faturali_ya_da_tahsilatli_kira_iptal_edilemez_iade_sonrasi_edilir()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Admin);
        using var host = new TestHost(fx.Pg.AppConnectionString);

        // (a) Faturalı kira: iptal reddedilir, defter ve durum değişmez; iade faturası sonrası iptal olur.
        var id = await OpenRentalAsync(s, o, await VehicleAsync(o), Now().AddHours(-2));
        Guid invoiceId;
        using (var sc = host.ScopeFor(o.TenantId))
            invoiceId = await sc.ServiceProvider.GetRequiredService<InvoiceService>().CreateFromRentalAsync(id);
        var ledger = await LedgerLineAsync(host, o.TenantId);
        var p = await ExpectProblem(await Gonder(s, HttpMethod.Post, $"{Rental}/{id}/iptal"), HttpStatusCode.BadRequest, UiError.Validation);
        Assert.Contains("iade", p.GetProperty("detail").GetString());
        Assert.Equal("Kirada", (await Json(await s.C.GetAsync($"{Rental}/{id}"))).GetProperty("kira").GetProperty("durum").GetString());
        Assert.Equal(ledger, await LedgerLineAsync(host, o.TenantId));
        using (var sc = host.ScopeFor(o.TenantId))
            await sc.ServiceProvider.GetRequiredService<InvoiceService>().CreateRefundAsync(invoiceId);
        Assert.Equal("Iptal", (await Json(await Gonder(s, HttpMethod.Post, $"{Rental}/{id}/iptal"))).GetProperty("durum").GetString());

        // (b) Tahsilatlı kira: iptal reddedilir; tahsilat iade edilince (ödeme) iptal olur.
        var id2 = await OpenRentalAsync(s, o, await VehicleAsync(o), Now().AddHours(-2));
        using (var sc = host.ScopeFor(o.TenantId))
            await sc.ServiceProvider.GetRequiredService<CashService>().CollectAsync(new CashInput { CariId = o.MusteriId, RentalId = id2, Tutar = 100m });
        await ExpectProblem(await Gonder(s, HttpMethod.Post, $"{Rental}/{id2}/iptal"), HttpStatusCode.BadRequest, UiError.Validation);
        using (var sc = host.ScopeFor(o.TenantId))
            await sc.ServiceProvider.GetRequiredService<CashService>().PayAsync(new CashInput { CariId = o.MusteriId, RentalId = id2, Tutar = 100m });
        Assert.Equal(0m, Dec((await Json(await s.C.GetAsync($"{Rental}/{id2}"))).GetProperty("kira"), "tahsilat"));
        Assert.Equal("Iptal", (await Json(await Gonder(s, HttpMethod.Post, $"{Rental}/{id2}/iptal"))).GetProperty("durum").GetString());

        // (c) Yarış: iptal ile fatura kesimi eşzamanlı — sonuç ya "fatura + Kirada" ya "iptal + faturasız"; ikisi birden ASLA.
        for (var i = 0; i < 6; i++)
        {
            var idr = await OpenRentalAsync(s, o, await VehicleAsync(o), Now().AddHours(-3).AddMinutes(-i));
            var cancel = Gonder(s, HttpMethod.Post, $"{Rental}/{idr}/iptal");
            var invoice = Task.Run(async () =>
            {
                using var sc = host.ScopeFor(o.TenantId);
                try { await sc.ServiceProvider.GetRequiredService<InvoiceService>().CreateFromRentalAsync(idr); return true; }
                catch (ValidationException) { return false; }
            });
            await Task.WhenAll(cancel, invoice);
            var status = (await Json(await s.C.GetAsync($"{Rental}/{idr}"))).GetProperty("kira").GetProperty("durum").GetString();
            var invoiceCount = (await Json(await s.C.GetAsync($"{Rental}/{idr}/faturalar"))).GetArrayLength();
            Assert.False(status == "Iptal" && invoiceCount > 0, $"yarış #{i}: iptal edilmiş kirada {invoiceCount} fatura");
            Assert.Equal(invoice.Result, invoiceCount == 1);
        }
    }

    [Fact]
    public async Task L2_Islem_govdesinde_eksik_alan_400_alanli_hicbir_sey_yazilmaz()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.OperatorA);
        var start = Now().AddDays(-3);
        var id = await OpenRentalAsync(s, o, await VehicleAsync(o), start);

        async Task<HttpResponseMessage> Raw(string path, string body)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"{Rental}/{id}/{path}")
            { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
            req.Headers.Add("X-XSRF-TOKEN", s.Xsrf);
            return await s.C.SendAsync(req);
        }

        await WaitForField(await Raw("teslim", "{\"cikisKm\":1000}"), "cikisYakit");
        await WaitForField(await Raw("teslim", "{\"cikisYakit\":8}"), "cikisKm");
        Assert.True((await Json(await s.C.GetAsync($"{Rental}/{id}"))).GetProperty("kira").GetProperty("cikisKm").ValueKind == JsonValueKind.Null);
        await Json(await Gonder(s, HttpMethod.Post, $"{Rental}/{id}/teslim", new { cikisKm = 1000, cikisYakit = 8 }));
        await WaitForField(await Raw("donus", $"{{\"donusKm\":1100,\"gercekDonus\":\"{start.AddDays(3):O}\"}}"), "donusYakit");
        await WaitForField(await Raw("donus", "{\"donusKm\":1100,\"donusYakit\":8}"), "gercekDonus");
        await WaitForField(await Raw("uzat", "{}"), "yeniBitTar");
        await WaitForField(await Raw("ek-hizmetler", "{\"miktar\":1}"), "ekHizmetTanimId");
        await WaitForField(await Raw("ek-hizmetler", $"{{\"ekHizmetTanimId\":\"{o.EkHizmetId}\"}}"), "miktar");
        var k = (await Json(await s.C.GetAsync($"{Rental}/{id}"))).GetProperty("kira");
        Assert.Equal("Kirada", k.GetProperty("durum").GetString());
        Assert.Equal(360m, Dec(k, "genelToplam"));
    }

    [Fact]
    public async Task L3_L4_L6_Tasma_ve_uc_deger_girdileri_400_alanli_kayit_birakmaz()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Admin);
        var start = Now().AddDays(-1);
        var id = await OpenRentalAsync(s, o, await VehicleAsync(o), start);
        var vehicle2 = await VehicleAsync(o);
        var b2 = Now().AddDays(30);
        object Body(object extra) => extra; // okunabilirlik
        async Task Create(string alan, object body) => await WaitForField(await Gonder(s, HttpMethod.Post, Rental, body), alan);

        await Create("gunlukUcret", Body(new { musteriId = o.MusteriId, vehicleId = vehicle2, basTar = b2, bitTar = b2.AddDays(3), gunlukUcret = 100000000000000000m }));
        await Create("depozito", new { musteriId = o.MusteriId, vehicleId = vehicle2, basTar = b2, bitTar = b2.AddDays(3), gunlukUcret = 100m, depozito = 100000000000000000m });
        await Create("dropUcreti", new { musteriId = o.MusteriId, vehicleId = vehicle2, basTar = b2, bitTar = b2.AddDays(3), gunlukUcret = 100m, cikisOfisi = "A", donusOfisi = "B", dropUcreti = 900000000000000m });
        await Create("komisyonOran", new { musteriId = o.MusteriId, vehicleId = vehicle2, basTar = b2, bitTar = b2.AddDays(3), gunlukUcret = 100m, komisyonOran = 1000000m });
        await Create("kiralamaTuru", new { musteriId = o.MusteriId, vehicleId = vehicle2, basTar = b2, bitTar = b2.AddDays(3), gunlukUcret = 100m, kiralamaTuru = new string('a', 500) });
        // L4: süre ≤ 5 yıl (9999 → önceden 500 + araç yüzyıllarca bloke + kayıt kalıyordu)
        await Create("bitTar", new { musteriId = o.MusteriId, vehicleId = vehicle2, basTar = b2, bitTar = new DateTimeOffset(9999, 12, 30, 0, 0, 0, TimeSpan.Zero), gunlukUcret = 100m });
        await Create("bitTar", new { musteriId = o.MusteriId, vehicleId = vehicle2, basTar = b2, bitTar = b2.AddYears(5).AddDays(1), gunlukUcret = 100m });
        Assert.Equal(1, await RentalCountAsync(o.TenantId)); // yalnız kurulum kirası: hiçbir red kayıt bırakmadı
        // Sınırda: tam 5 yıl kabul (uzun dönem kiralama)
        await Json(await Gonder(s, HttpMethod.Post, Rental, new { musteriId = o.MusteriId, vehicleId = vehicle2, basTar = b2, bitTar = b2.AddYears(5), gunlukUcret = 100m }), HttpStatusCode.Created);

        var g = new JsonObject();
        var rental = (await Json(await s.C.GetAsync($"{Rental}/{id}"))).GetProperty("kira");
        foreach (var alan in UpdateFields) g[alan] = JsonNode.Parse(rental.GetProperty(alan).GetRawText());
        g["provizyon"] = 100000000000000000m;
        await WaitForField(await Gonder(s, HttpMethod.Put, $"{Rental}/{id}", g), "provizyon");
        g["provizyon"] = null; g["fazlaKmUcret"] = 100000000000000000m;
        await WaitForField(await Gonder(s, HttpMethod.Put, $"{Rental}/{id}", g), "fazlaKmUcret");

        await WaitForField(await Gonder(s, HttpMethod.Post, $"{Rental}/{id}/uzat", new { yeniBitTar = new DateTimeOffset(9999, 12, 30, 0, 0, 0, TimeSpan.Zero) }), "yeniBitTar");
        // L6: yakıt 0–12
        await WaitForField(await Gonder(s, HttpMethod.Post, $"{Rental}/{id}/teslim", new { cikisKm = 1000, cikisYakit = -50 }), "cikisYakit");
        await WaitForField(await Gonder(s, HttpMethod.Post, $"{Rental}/{id}/teslim", new { cikisKm = 1000, cikisYakit = 13 }), "cikisYakit");
        await Json(await Gonder(s, HttpMethod.Post, $"{Rental}/{id}/teslim", new { cikisKm = 1000, cikisYakit = 12 }));
        var preview = await Json(await s.C.GetAsync($"{Rental}/{id}/donus-hesapla?donusKm=1100&donusYakit=-2147483648&gercekDonus={Uri.EscapeDataString(start.AddDays(3).ToString("O"))}"));
        Assert.False(preview.GetProperty("ok").GetBoolean());
        await WaitForField(await Gonder(s, HttpMethod.Post, $"{Rental}/{id}/donus", new { donusKm = 1100, donusYakit = 13, gercekDonus = start.AddDays(3) }), "donusYakit");
        await WaitForField(await Gonder(s, HttpMethod.Post, $"{Rental}/{id}/donus", new { donusKm = 1100, donusYakit = 8, gercekDonus = Now().AddYears(2) }), "gercekDonus");
        await WaitForField(await Gonder(s, HttpMethod.Post, $"{Rental}/musteri", new { ad = new string('a', 5000) }), "ad");

        var last = (await Json(await s.C.GetAsync($"{Rental}/{id}"))).GetProperty("kira");
        Assert.Equal("Kirada", last.GetProperty("durum").GetString());
        Assert.Equal(360m, Dec(last, "genelToplam"));
        Assert.Equal(rental.GetProperty("bitTar").GetDateTimeOffset(), last.GetProperty("bitTar").GetDateTimeOffset());
    }

    [Fact]
    public async Task L5_Yabanci_ya_da_olmayan_musteri_arac_400_alanli()
    {
        var o = await SetUpEnvironmentAsync();
        var other = await SetUpEnvironmentAsync();
        var s = await LoginAsync(other, Kim.Admin);
        var b = Now().AddDays(3);
        // Başka kiracının müşterisi (FK denetimi RLS'i atlar — önceden kira YAZILIYORDU).
        await WaitForField(await Gonder(s, HttpMethod.Post, Rental, new
        {
            musteriId = o.MusteriId, vehicleId = await VehicleAsync(other), basTar = b, bitTar = b.AddDays(1), gunlukUcret = 100m,
        }), "musteriId");
        // Başka kiracının aracı
        await WaitForField(await Gonder(s, HttpMethod.Post, Rental, new
        {
            musteriId = other.MusteriId, vehicleId = await VehicleAsync(o), basTar = b, bitTar = b.AddDays(1), gunlukUcret = 100m,
        }), "vehicleId");
        // Var olmayan kimlik (önceden 500)
        await WaitForField(await Gonder(s, HttpMethod.Post, Rental, new
        {
            musteriId = other.MusteriId, vehicleId = Guid.NewGuid(), basTar = b, bitTar = b.AddDays(1), gunlukUcret = 100m,
        }), "vehicleId");
        Assert.Equal(0, await RentalCountAsync(other.TenantId));
        Assert.Equal(0, await RentalCountAsync(o.TenantId));
    }

    [Fact]
    public async Task L7_Liste_tarih_suzgeci_Istanbul_gunu_sinirlari()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Admin);
        var tz = TenantDay.Slice;
        var d = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).DateTime).AddDays(40);
        DateTimeOffset Local(DateOnly g, int sa, int dk, int sn = 0, int ms = 0)
        {
            var y = g.ToDateTime(new TimeOnly(sa, dk, sn, ms), DateTimeKind.Unspecified);
            return new DateTimeOffset(y, tz.GetUtcOffset(y)).ToUniversalTime();
        }
        var morning = await OpenRentalAsync(s, o, await VehicleAsync(o), Local(d, 0, 30), day: 1);          // UTC'de önceki gün
        var endOfNight = await OpenRentalAsync(s, o, await VehicleAsync(o), Local(d, 23, 59, 59, 500), day: 1);
        var nextDay = await OpenRentalAsync(s, o, await VehicleAsync(o), Local(d.AddDays(1), 0, 0), day: 1);
        var dun = await OpenRentalAsync(s, o, await VehicleAsync(o), Local(d.AddDays(-1), 23, 59, 59, 999), day: 1);
        var l = await Json(await s.C.GetAsync($"{Rental}?basMin={d:yyyy-MM-dd}&basMax={d:yyyy-MM-dd}&boyut=200"));
        var ids = l.GetProperty("kayitlar").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToHashSet();
        Assert.Contains(morning, ids);
        Assert.Contains(endOfNight, ids);   // önceden AddSeconds(-1) kesimi 23:59:59.xxx'i dışarıda bırakıyordu
        Assert.DoesNotContain(nextDay, ids);
        Assert.DoesNotContain(dun, ids);
    }

    [Fact]
    public async Task Panel_gun_kovasi_Istanbul_sinirlarinda()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Admin);
        var tz = TenantDay.Slice;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).DateTime);
        DateTimeOffset Local(DateOnly g, int sa, int dk)
        {
            var y = g.ToDateTime(new TimeOnly(sa, dk), DateTimeKind.Unspecified);
            return new DateTimeOffset(y, tz.GetUtcOffset(y)).ToUniversalTime();
        }
        var cases = new (DateTimeOffset Bit, string? Kova)[]
        {
            (Local(today.AddDays(-1), 23, 59), "gecikmis"), (Local(today, 0, 1), "bugun"), (Local(today, 23, 59), "bugun"),
            (Local(today.AddDays(1), 0, 1), "yarin"), (Local(today.AddDays(1), 23, 59), "yarin"), (Local(today.AddDays(2), 0, 1), null),
        };
        var ids = new List<Guid>();
        foreach (var v in cases)
            ids.Add((await Json(await Gonder(s, HttpMethod.Post, Rental, new
            {
                musteriId = o.MusteriId, vehicleId = await VehicleAsync(o), basTar = v.Bit.AddDays(-2), bitTar = v.Bit, gunlukUcret = 100m, cikisOfisi = "SubeA",
            }), HttpStatusCode.Created)).GetProperty("id").GetGuid());
        var p = await Json(await s.C.GetAsync(V1 + "/panel/ozet"));
        if (DateOnly.Parse(p.GetProperty("bugun").GetString()!) != today) return; // test gece yarısını geçti — ölçüm geçersiz
        for (var i = 0; i < cases.Length; i++)
        {
            string? found = null;
            foreach (var bucket in new[] { "gecikmis", "bugun", "yarin" })
                if (p.GetProperty("donusler").GetProperty(bucket).EnumerateArray().Any(x => x.GetProperty("rentalId").GetGuid() == ids[i]))
                    found = bucket;
            Assert.True(found == cases[i].Kova, $"#{i} bit={cases[i].Bit:O}: beklenen {cases[i].Kova ?? "yok"}, bulunan {found ?? "yok"}");
        }
    }

    [Fact]
    public async Task L1_SYS_tanim_kodu_degistirilemez_silinemez_satir_silinemez()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.OperatorA);
        var start = Now().AddDays(5);
        // Farklı dönüş ofisi + drop ücreti → FeeLineService SYS-DROP satırını yazar.
        var id = (await Json(await Gonder(s, HttpMethod.Post, Rental, new
        {
            musteriId = o.MusteriId, vehicleId = await VehicleAsync(o), basTar = start, bitTar = start.AddDays(3), gunlukUcret = 100m, fiyatTuru = "Günlük",
            cikisOfisi = "SubeA", donusOfisi = "SubeA2", dropUcreti = 100m,
        }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var d = await Json(await s.C.GetAsync($"{Rental}/{id}"));
        var row = d.GetProperty("ekHizmetler").EnumerateArray().Single();
        var definitionId = row.GetProperty("ekHizmetTanimId").GetGuid();
        var general = Dec(d.GetProperty("kira"), "genelToplam");
        Assert.Equal(360m + 120m, general); // ORACLE: 3 × 100 net + %20 = 360; drop 100 net + %20 = 120

        using (var host = new TestHost(fx.Pg.AppConnectionString))
        {
            using var sc = host.ScopeFor(o.TenantId, Guid.NewGuid(), "op", UserRole.Operator, "SubeA");
            var svc = sc.ServiceProvider.GetRequiredService<RentACar.Application.EkHizmetler.AddOnDefinitionService>();
            var t = (await svc.GetAsync(definitionId))!;
            RentACar.Application.EkHizmetler.EkHizmetTanimInput Input(string code) =>
                new() { Kod = code, Ad = t.Ad, BirimUcret = t.BirimUcret, KdvOrani = t.KdvOrani, Aktif = t.Aktif };
            await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateAsync(definitionId, Input("DROPX")));   // kod değişmez
            await Assert.ThrowsAsync<ValidationException>(() => svc.DeleteAsync(definitionId));                    // silinmez
            Assert.True(await svc.UpdateAsync(definitionId, Input(t.Kod)));                                        // aynı kodla düzenleme serbest
            await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateAsync(o.EkHizmetId, new()          // normal → SYS- olmaz
            { Kod = "SYS-GPS", Ad = "Navigasyon", BirimUcret = 50m, KdvOrani = 0.20m, Aktif = true }));
            await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new()                        // elle SYS-* açılmaz
            { Kod = "SYS-YENI", Ad = "Sahte sistem ücreti", BirimUcret = 1m, KdvOrani = 0.20m, Aktif = true }));
        }
        await ExpectProblem(await Gonder(s, HttpMethod.Delete, $"{Rental}/{id}/ek-hizmetler/{row.GetProperty("id").GetGuid()}"),
            HttpStatusCode.BadRequest, UiError.Validation);
        Assert.Equal(general, Dec((await Json(await s.C.GetAsync($"{Rental}/{id}"))).GetProperty("kira"), "genelToplam"));
    }

    // ================================================================== F4.1 adversarial 2. tur (N1–N3)

    private async Task<T> ReadDataAsync<T>(Guid tenantId, Func<AppDbContext, Task<T>> read)
    {
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(tenantId);
        await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        return await read(db);
    }

    [Fact]
    public async Task N1_Turkce_harfli_ofis_adinda_kendi_subesi_acilir_baska_sube_403()
    {
        var o = await SetUpEnvironmentAsync();
        var ege = new Branch { Kod = "EG", Ad = "Ege Bölge" };
        var marmara = new Branch { Kod = "MR", Ad = "Marmara Bölge" };
        // 'İ' (U+0130), 'I', 'ı' (U+0131), 'i' — Postgres lower() ile .NET ToLowerInvariant'ın ayrıştığı harfler.
        var aegeanOffices = new[] { "İzmir Merkez", "ISPARTA Işık", "ığdır iı", "Şişli Ofis", "iİıI Karma" };
        // Şubeler ÖNCE kaydedilir: ofis kaydındaki şube-FK interceptor'ı şubeyi DB'den çözer.
        await WriteDataAsync(o.TenantId, db => { db.Branches.Add(ege); db.Branches.Add(marmara); });
        await WriteDataAsync(o.TenantId, db =>
        {
            var k = 0;
            foreach (var name in aegeanOffices) db.Locations.Add(new Location { Kod = "E" + k++, Ad = name, Sube = ege.Ad, SubeId = ege.Id, Aktif = true });
            db.Locations.Add(new Location { Kod = "M0", Ad = "İstanbul Avrupa", Sube = marmara.Ad, SubeId = marmara.Id, Aktif = true });
        });
        // Şube FK'li operatör (Ege)
        var name2 = RandomText("n1");
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.Pg.OwnerConnectionString).Options,
                         NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            var u = new User { TenantId = o.TenantId, UserName = name2, DisplayName = name2, Rol = UserRole.Operator, AtanmisSube = ege.Ad, AtanmisSubeId = ege.Id, IsActive = true };
            u.PasswordHash = fx.Web.Services.GetRequiredService<IPasswordHasher<User>>().HashPassword(u, o.Sifre);
            db.Users.Add(u);
            await db.SaveChangesAsync();
        }
        var op = await LoginAsync(o.Kod, name2, o.Sifre);
        var start = Now().AddDays(5);
        var i = 0;
        Guid first = Guid.Empty;
        foreach (var office in aegeanOffices)
        {
            var response = await Gonder(op, HttpMethod.Post, Rental, new
            {
                musteriId = o.MusteriId, vehicleId = await VehicleAsync(o, ege.Ad), basTar = start.AddDays(i), bitTar = start.AddDays(i + 1), gunlukUcret = 100m, cikisOfisi = office,
            });
            Assert.True(response.StatusCode == HttpStatusCode.Created, $"'{office}': {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
            var id = (await Json(response, HttpStatusCode.Created)).GetProperty("id").GetGuid();
            i += 2;
            Assert.Equal(HttpStatusCode.OK, (await op.C.GetAsync($"{Rental}/{id}")).StatusCode); // kaydı da görür
            if (first == Guid.Empty) first = id;
        }
        // Başka şubenin ofisi (adı da 'İ' ile) → 403, hiçbir şey yazılmaz.
        var once = await RentalCountAsync(o.TenantId);
        await ExpectProblem(await Gonder(op, HttpMethod.Post, Rental, new
        {
            musteriId = o.MusteriId, vehicleId = await VehicleAsync(o, ege.Ad), basTar = start.AddDays(40), bitTar = start.AddDays(41), gunlukUcret = 100m, cikisOfisi = "İstanbul Avrupa",
        }), HttpStatusCode.Forbidden, UiError.Forbidden);
        Assert.Equal(once, await RentalCountAsync(o.TenantId));

        // Açık kirada ofis değiştirme (UpdateOpenAsync — aynı eşleme): kendi şubesinin 'İ'li ofisine 200, başkasına 403.
        var g = new JsonObject();
        var rental = (await Json(await op.C.GetAsync($"{Rental}/{first}"))).GetProperty("kira");
        foreach (var alan in UpdateFields) g[alan] = JsonNode.Parse(rental.GetProperty(alan).GetRawText());
        g["cikisOfisi"] = "iİıI Karma";
        Assert.Equal("iİıI Karma", (await Json(await Gonder(op, HttpMethod.Put, $"{Rental}/{first}", g))).GetProperty("cikisOfisi").GetString());
        g["cikisOfisi"] = "İstanbul Avrupa";
        await ExpectProblem(await Gonder(op, HttpMethod.Put, $"{Rental}/{first}", g), HttpStatusCode.Forbidden, UiError.Forbidden);
    }

    [Fact]
    public async Task N2_Fatura_ile_ek_hizmet_ekle_sil_serilesir_fatura_sozlesmeyle_ayni()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Admin);
        using var host = new TestHost(fx.Pg.AppConnectionString);
        var rnd = new Random(17);
        async Task<bool> MakeInvoice(Guid id)
        {
            await Task.Delay(rnd.Next(0, 12));
            using var sc = host.ScopeFor(o.TenantId);
            try { await sc.ServiceProvider.GetRequiredService<InvoiceService>().CreateFromRentalAsync(id); return true; }
            catch (ValidationException) { return false; } // yarışı kaybeden: temiz red (kilitlenme/500 DEĞİL)
        }
        async Task<HttpStatusCode> Http(Func<Task<HttpResponseMessage>> f) { await Task.Delay(rnd.Next(0, 12)); return (await f()).StatusCode; }
        var violation = new List<string>();
        for (var i = 0; i < 12; i++)
        foreach (var type in new[] { "ekle", "sil" })
        {
            // ORACLE: kira 360 (3 × 100 net + %20); GPS'li açılışta 420. Fatura ya ÖNCE (ek hizmet reddedilir) ya SONRA
            // (güncel tutarla) kesilir; bayat tutarla kesilen fatura reddedilir. Her durumda fatura == sözleşme.
            var id = await OpenRentalAsync(s, o, await VehicleAsync(o), Now().AddHours(-3).AddMinutes(-i), gps: type == "sil");
            var item = type == "sil"
                ? (await Json(await s.C.GetAsync($"{Rental}/{id}"))).GetProperty("ekHizmetler")[0].GetProperty("id").GetGuid()
                : Guid.Empty;
            var tf = MakeInvoice(id);
            var te = type == "ekle"
                ? Http(() => Gonder(s, HttpMethod.Post, $"{Rental}/{id}/ek-hizmetler", new { ekHizmetTanimId = o.EkHizmetId, miktar = 1m }))
                : Http(() => Gonder(s, HttpMethod.Delete, $"{Rental}/{id}/ek-hizmetler/{item}"));
            await Task.WhenAll(tf, te);
            Assert.True(te.Result is HttpStatusCode.OK or HttpStatusCode.BadRequest, $"{type}#{i}: ek hizmet {(int)te.Result}");
            var general = await ReadDataAsync(o.TenantId, db => db.Rentals.Where(x => x.Id == id).Select(x => x.GenelToplam).SingleAsync());
            var invoice = await ReadDataAsync(o.TenantId, db => db.Invoices.Where(x => x.RentalId == id).SumAsync(x => (decimal?)x.GenelToplam)) ?? 0m;
            var row = $"{type}#{i}: fatura={tf.Result} ek={(int)te.Result} genel={general} faturaToplam={invoice}";
            if (tf.Result && invoice != general) violation.Add(row);
            if (!tf.Result && invoice != 0m) violation.Add("reddedilen fatura yazılmış: " + row);
            if (general != (type == "ekle" ? (te.Result == HttpStatusCode.OK ? 420m : 360m) : (te.Result == HttpStatusCode.OK ? 360m : 420m)))
                violation.Add("sözleşme toplamı: " + row);
        }
        Assert.True(violation.Count == 0, $"{violation.Count} yarışta fatura ≠ sözleşme:\n" + string.Join("\n", violation));

        // İptal edilmiş kiraya ek hizmet eklenemez (kilit altında durum kontrolü).
        var idCancel = await OpenRentalAsync(s, o, await VehicleAsync(o), Now().AddHours(-3));
        await Json(await Gonder(s, HttpMethod.Post, $"{Rental}/{idCancel}/iptal"));
        await ExpectProblem(await Gonder(s, HttpMethod.Post, $"{Rental}/{idCancel}/ek-hizmetler", new { ekHizmetTanimId = o.EkHizmetId, miktar = 1m }),
            HttpStatusCode.BadRequest, UiError.Validation);
        Assert.Equal(360m, Dec((await Json(await s.C.GetAsync($"{Rental}/{idCancel}"))).GetProperty("kira"), "genelToplam"));
    }

    [Fact]
    public async Task N3_Iptal_kiraya_tahsilat_yazilamaz_servis_ve_api_yarisi()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Admin);
        var y = await LoginAsync(o, Kim.Admin);
        using var host = new TestHost(fx.Pg.AppConnectionString);

        // (a) sıralı, SERVİS yolu (Blazor /finans/tahsilat aynı CashService'ten geçer): red, hiçbir şey yazılmaz.
        var id0 = await OpenRentalAsync(s, o, await VehicleAsync(o), Now().AddHours(-3));
        await Json(await Gonder(s, HttpMethod.Post, $"{Rental}/{id0}/iptal"));
        var ledger = await ReadDataAsync(o.TenantId, db => db.AccountLedgerEntries.CountAsync());
        using (var sc = host.ScopeFor(o.TenantId))
            await Assert.ThrowsAnyAsync<ValidationException>(() => sc.ServiceProvider.GetRequiredService<CashService>()
                .CollectAsync(new CashInput { CariId = o.MusteriId, RentalId = id0, Tutar = 100m }));
        Assert.Equal(ledger, await ReadDataAsync(o.TenantId, db => db.AccountLedgerEntries.CountAsync()));
        Assert.Equal(0m, await ReadDataAsync(o.TenantId, db => db.Rentals.Where(x => x.Id == id0).Select(x => x.Tahsilat).SingleAsync()));

        // (b) yarış: iptal ∥ tahsilat — servis yolu ve gerçek /api/ui/v1/finans/tahsilat ucu. İptal kirada tahsilat ASLA kalmaz.
        Task<HttpResponseMessage> ApiCollect(Guid rental)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, V1 + "/finans/tahsilat")
            { Content = JsonContent.Create(new { cariId = o.MusteriId, tutar = 50m, hesap = "Kasa", kiraId = rental }) };
            req.Headers.Add("X-XSRF-TOKEN", y.Xsrf);
            req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            return y.C.SendAsync(req);
        }
        var rnd = new Random(23);
        var violation = new List<string>();
        for (var i = 0; i < 20; i++)
        {
            var id = await OpenRentalAsync(s, o, await VehicleAsync(o), Now().AddHours(-3).AddMinutes(-i));
            var d1 = rnd.Next(0, 10); var d2 = rnd.Next(0, 10);
            var cancel = Task.Run(async () => { await Task.Delay(d1); return (await Gonder(s, HttpMethod.Post, $"{Rental}/{id}/iptal")).StatusCode; });
            Task<string> collect = i % 2 == 0
                ? Task.Run(async () =>
                {
                    await Task.Delay(d2);
                    using var sc = host.ScopeFor(o.TenantId);
                    try { await sc.ServiceProvider.GetRequiredService<CashService>().CollectAsync(new CashInput { CariId = o.MusteriId, RentalId = id, Tutar = 50m }); return "200"; }
                    catch (ValidationException) { return "400"; }
                })
                : Task.Run(async () => { await Task.Delay(d2); return ((int)(await ApiCollect(id)).StatusCode).ToString(); });
            await Task.WhenAll(cancel, collect);
            Assert.True(collect.Result is "200" or "400", $"#{i}: tahsilat {collect.Result}");
            var k = await ReadDataAsync(o.TenantId, db => db.Rentals.AsNoTracking().SingleAsync(x => x.Id == id));
            // ORACLE: ya iptal (tahsilat 0) ya tahsilat 50 + Kirada (iptal "tahsilatlı kira" diye reddedildi).
            var consistent = (k.Durum == RentalStatus.Iptal && k.Tahsilat == 0m) || (k.Durum == RentalStatus.Kirada && k.Tahsilat == 50m);
            if (!consistent) violation.Add($"#{i}: iptal={(int)cancel.Result} tahsilat={collect.Result} → durum={k.Durum} tahsilat={k.Tahsilat}");
        }
        Assert.True(violation.Count == 0, string.Join("\n", violation));
    }

    /// <summary>RequireAnyPermission'ın tek izinle çağrılmasını reddetmesini sınamak için boş kural oluşturucu.</summary>
    private sealed class RouteGroupBuilderStub : Microsoft.AspNetCore.Builder.IEndpointConventionBuilder
    {
        public void Add(Action<Microsoft.AspNetCore.Builder.EndpointBuilder> convention) { }
    }
}
