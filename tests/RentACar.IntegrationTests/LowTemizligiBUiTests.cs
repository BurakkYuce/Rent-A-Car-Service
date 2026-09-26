using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Api;

namespace RentACar.IntegrationTests;

/// <summary>
/// Low temizliği B — <c>/api/ui</c> yüzeyi GERÇEK Web boru hattında (cookie + CSRF + pilot):
/// (3) ek hizmet ekleme <c>Idempotency-Key</c> zorunlu + mükerrer 409 (sıralı, eşzamanlı, farklı içerik, başka kira);
/// (5) "+03:00" ofsetli tarih para/kira uçlarında 500 DEĞİL (DB'ye UTC gider, an korunur).
/// BAĞIMSIZ ORACLE: 3 gün × 100 net = 300 + %20 KDV = 360 brüt; GPS 50 net × 2 = 100 + 20 KDV = 120 → 480.
/// Kullanıcılar çalışma anında rastgele parolayla üretilir.
/// </summary>
[Collection("web")]
public sealed class LowTemizligiBUiTests(WebFixture fx)
{
    private const string V1 = "/api/ui/v1";
    private const string Rental = V1 + "/kiralar";
    private static readonly TimeSpan Ist = TimeSpan.FromHours(3);

    private sealed record Ortam(Guid TenantId, string Kod, string Kullanici, string Sifre, Guid MusteriId, Guid GpsId);
    private sealed record Oturum(HttpClient C, string Xsrf);

    private static string RandomText(string prefix) => prefix + Guid.NewGuid().ToString("N")[..10];
    private static DateTimeOffset Now() => DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    private async Task<Ortam> SetUpEnvironmentAsync()
    {
        var tenant = Guid.NewGuid();
        var code = RandomText("lowb");
        var name = RandomText("u");
        var password = WebFixture.RandomPassword();
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.Pg.OwnerConnectionString).Options;
        await using (var db = new AppDbContext(opts, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            db.Tenants.Add(new Tenant { Id = tenant, Code = code, Name = code, IsActive = true });
            var u = new User { TenantId = tenant, UserName = name, DisplayName = name, Rol = UserRole.Admin, IsActive = true };
            u.PasswordHash = fx.Web.Services.GetRequiredService<IPasswordHasher<User>>().HashPassword(u, password);
            db.Users.Add(u);
            await db.SaveChangesAsync();
        }
        await fx.MakePilotAsync(tenant, true);
        var customer = new Customer { Tip = CustomerType.Bireysel, Ad = "Ece", Soyad = "Kaya" };
        var gps = new EkHizmetTanim { Kod = "GPS", Ad = "Navigasyon", BirimUcret = 50m, KdvOrani = 0.20m, Aktif = true };
        await WriteDataAsync(tenant, db => { db.Customers.Add(customer); db.EkHizmetTanimlari.Add(gps); });
        return new Ortam(tenant, code, name, password, customer.Id, gps.Id);
    }

    private async Task WriteDataAsync(Guid tenantId, Action<AppDbContext> write)
    {
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(tenantId);
        await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        write(db);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> VehicleAsync(Ortam o)
    {
        var v = new Vehicle
        {
            Plaka = "34 LB " + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), Marka = "Fiat", Tip = "Egea",
            Grup = "C", Sube = "SubeA", Durum = VehicleStatus.Musait, Km = 1000
        };
        await WriteDataAsync(o.TenantId, db => db.Vehicles.Add(v));
        return v.Id;
    }

    private async Task<Oturum> LoginAsync(Ortam o)
    {
        var c = fx.Web.Client();
        var x = await c.GetAsync(V1 + "/oturum/xsrf");
        var once = CookieValue(x, "XSRF-TOKEN")!;
        var req = new HttpRequestMessage(HttpMethod.Post, V1 + "/oturum/giris")
        {
            Content = JsonContent.Create(new { firma = o.Kod, kullanici = o.Kullanici, sifre = o.Sifre }),
        };
        req.Headers.Add("X-XSRF-TOKEN", once);
        var r = await c.SendAsync(req);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"giriş başarısız: {await r.Content.ReadAsStringAsync()}");
        return new Oturum(c, CookieValue(r, "XSRF-TOKEN")!);
    }

    private static string? CookieValue(HttpResponseMessage r, string name)
    {
        if (!r.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var d in values)
            if (d.StartsWith(name + "=", StringComparison.Ordinal))
                return Uri.UnescapeDataString(d[(name.Length + 1)..].Split(';')[0]);
        return null;
    }

    private static Task<HttpResponseMessage> Gonder(Oturum s, HttpMethod m, string url, object? body = null, string? idem = null)
    {
        var req = new HttpRequestMessage(m, url);
        req.Headers.Add("X-XSRF-TOKEN", s.Xsrf);
        if (idem is not null) req.Headers.Add("Idempotency-Key", idem);
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
        var root = await Json(r, status);
        Assert.Equal(code, root.GetProperty("kod").GetString());
        return root;
    }

    private static string NewKey() => "lowb-" + Guid.NewGuid().ToString("N");

    /// <summary>3 gün × 100 net "Günlük" kira; <paramref name="start"/> ofsetiyle gönderilir.</summary>
    private static async Task<Guid> OpenRentalAsync(Oturum s, Ortam o, Guid vehicle, DateTimeOffset start)
    {
        var j = await Json(await Gonder(s, HttpMethod.Post, Rental, new
        {
            musteriId = o.MusteriId, vehicleId = vehicle, basTar = start, bitTar = start.AddDays(3),
            gunlukUcret = 100m, fiyatTuru = "Günlük", cikisOfisi = "SubeA", donusOfisi = "SubeA",
        }), HttpStatusCode.Created);
        return j.GetProperty("id").GetGuid();
    }

    private static decimal Dec(JsonElement e, string name) => e.GetProperty(name).GetDecimal();

    private static string AppendUrl(Guid rental) => $"{Rental}/{rental}/ek-hizmetler";

    private static async Task<(int Kalem, decimal Genel)> RentalStatusAsync(Oturum s, Guid rental)
    {
        var d = await Json(await s.C.GetAsync($"{Rental}/{rental}"));
        return (d.GetProperty("ekHizmetler").GetArrayLength(), Dec(d.GetProperty("kira"), "genelToplam"));
    }

    // ------------------------------------------------------------ (3) ek hizmet idempotency

    [Fact]
    public async Task Ek_hizmet_basliksiz_400_ayni_anahtar_ikinci_kez_409_mevcut_ile_tek_kalem()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o);
        var rental = await OpenRentalAsync(s, o, await VehicleAsync(o), Now().AddHours(8));
        var body = new { ekHizmetTanimId = o.GpsId, miktar = 2m };

        // Başlıksız: 400 errors[Idempotency-Key], hiçbir şey yazılmaz.
        var p = await ExpectProblem(await Gonder(s, HttpMethod.Post, AppendUrl(rental), body), HttpStatusCode.BadRequest, UiError.Validation);
        Assert.True(p.GetProperty("errors").TryGetProperty("Idempotency-Key", out _), p.ToString());
        Assert.Equal((0, 360m), await RentalStatusAsync(s, rental));

        var key = NewKey();
        var first = await Json(await Gonder(s, HttpMethod.Post, AppendUrl(rental), body, key));
        Assert.Equal(480m, Dec(first.GetProperty("kira"), "genelToplam"));
        var itemId = first.GetProperty("kalemler")[0].GetProperty("id").GetGuid();

        // Kaybolan yanıttan sonraki birebir tekrar: 409 mukerrer + mevcut{ayniIcerik=true}; ikinci kalem YOK.
        var repeat = await ExpectProblem(await Gonder(s, HttpMethod.Post, AppendUrl(rental), body, key), HttpStatusCode.Conflict, UiError.Duplicate);
        var existing = repeat.GetProperty("mevcut");
        Assert.Equal(itemId, existing.GetProperty("id").GetGuid());
        Assert.True(existing.GetProperty("ayniIcerik").GetBoolean());
        Assert.Equal(120m, Dec(existing, "tutar"));

        // Aynı anahtar, farklı miktar: 409 + ayniIcerik=false; gelen kalem YAZILMADI.
        var different = await ExpectProblem(await Gonder(s, HttpMethod.Post, AppendUrl(rental), new { ekHizmetTanimId = o.GpsId, miktar = 5m }, key),
            HttpStatusCode.Conflict, UiError.Duplicate);
        Assert.False(different.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        Assert.Equal((1, 480m), await RentalStatusAsync(s, rental));

        // Yeni anahtar = meşru ikinci kalem (1 × 60 brüt) → 540.
        await Json(await Gonder(s, HttpMethod.Post, AppendUrl(rental), new { ekHizmetTanimId = o.GpsId, miktar = 1m }, NewKey()));
        Assert.Equal((2, 540m), await RentalStatusAsync(s, rental));
    }

    [Fact]
    public async Task Ek_hizmet_eszamanli_ayni_anahtar_tek_kalem_ve_baska_kira_mevcut_sizdirmaz()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o);
        var rental = await OpenRentalAsync(s, o, await VehicleAsync(o), Now().AddHours(8));
        var other = await OpenRentalAsync(s, o, await VehicleAsync(o), Now().AddHours(8));
        var key = NewKey();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            Gonder(s, HttpMethod.Post, AppendUrl(rental), new { ekHizmetTanimId = o.GpsId, miktar = 1m }, key)));
        var codes = responses.Select(r => r.StatusCode).ToList();
        Assert.Equal(1, codes.Count(k => k == HttpStatusCode.OK));
        Assert.All(codes.Where(k => k != HttpStatusCode.OK), k => Assert.Equal(HttpStatusCode.Conflict, k));
        Assert.Equal((1, 420m), await RentalStatusAsync(s, rental)); // 360 + 60

        // Aynı anahtar BAŞKA kirada: 409, mevcut YOK (öbür kiranın kalemi sızmaz), başka kiraya yazılmaz.
        var p = await ExpectProblem(await Gonder(s, HttpMethod.Post, AppendUrl(other), new { ekHizmetTanimId = o.GpsId, miktar = 1m }, key),
            HttpStatusCode.Conflict, UiError.Duplicate);
        Assert.False(p.TryGetProperty("mevcut", out var m) && m.ValueKind != JsonValueKind.Null, p.ToString());
        Assert.Equal((0, 360m), await RentalStatusAsync(s, other));
    }

    // ------------------------------------------------------------ (5) +03:00 ofsetli tarih → 500 değil

    [Fact]
    public async Task Ofsetli_tarih_nakit_tahsilat_ve_odeme_500_vermez_an_korunur()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o);
        var an = Now().AddHours(-2);
        var local = an.ToOffset(Ist); // aynı an, "+03:00" ile

        foreach (var endpoint in new[] { "/finans/tahsilat", "/finans/odeme" })
        {
            var r = await Gonder(s, HttpMethod.Post, V1 + endpoint,
                new { cariId = o.MusteriId, tutar = 10m, hesap = "Kasa", tarih = local }, NewKey());
            await Json(r);
        }

        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(o.TenantId);
        await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var dates = await db.CashTransactions.AsNoTracking().Where(t => t.CariId == o.MusteriId).Select(t => t.Tarih).ToListAsync();
        Assert.Equal(2, dates.Count);
        Assert.All(dates, t => Assert.Equal(an.UtcDateTime, t.UtcDateTime)); // an kaymadı (3 saat hatası yok)
    }

    [Fact]
    public async Task Ofsetli_tarih_kira_olustur_teslim_uzat_donus_500_vermez()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o);
        var start = Now().AddHours(-1);
        var rental = await OpenRentalAsync(s, o, await VehicleAsync(o), start.ToOffset(Ist));
        var d = await Json(await s.C.GetAsync($"{Rental}/{rental}"));
        Assert.Equal(start.UtcDateTime, d.GetProperty("kira").GetProperty("basTar").GetDateTimeOffset().UtcDateTime);

        await Json(await Gonder(s, HttpMethod.Post, $"{Rental}/{rental}/teslim", new { cikisKm = 1000, cikisYakit = 8 }));
        await Json(await Gonder(s, HttpMethod.Post, $"{Rental}/{rental}/uzat", new { yeniBitTar = start.AddDays(4).ToOffset(Ist) }));
        await Json(await Gonder(s, HttpMethod.Post, $"{Rental}/{rental}/donus",
            new { donusKm = 1100, donusYakit = 8, gercekDonus = Now().ToOffset(Ist) }));
    }
}
