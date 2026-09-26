using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// F5.1 — <c>/api/ui/v1/{rezervasyonlar,teklifler,takvim,musaitlik,rez-sartlari,filo-kiralama}</c> GERÇEK Web boru
/// hattında (cookie + CSRF + pilot, <c>racar_app</c>). BAĞIMSIZ ORACLE: beklenenler elle kurulmuş senaryodan
/// ("KDV Dahil Günlük" 100 × 3 gün = 300; filo 1.000 net × %20 = 1.200 × 3 ay = 3.600). Kullanıcılar çalışma anında
/// rastgele parolayla üretilir (depoda kimlik bilgisi yok).
/// </summary>
[Collection("web")]
public sealed partial class UiRezervasyonTests(WebFixture fx)
{
    private const string V1 = "/api/ui/v1";
    private const string TestReservation = V1 + "/rezervasyonlar";

    private enum Kim { Admin, OperatorA, OperatorB, Muhasebe }

    private sealed class Ortam
    {
        public required Guid TenantId { get; init; }
        public required string Kod { get; init; }
        public required string Sifre { get; init; }
        public required Dictionary<Kim, string> Kullanicilar { get; init; }
        public Guid MusteriId { get; set; }
    }

    private static string RandomText(string prefix) => prefix + Guid.NewGuid().ToString("N")[..10];

    /// <summary>Tam saniyeye hizalı gelecek an (Linux 100ns / Mac µs farkı DB eşitliğini bozmasın).</summary>
    private static DateTimeOffset Tomorrow(int extraDays = 1)
        => DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddDays(extraDays).ToUnixTimeSeconds());

    private async Task<Ortam> SetUpEnvironmentAsync()
    {
        var o = new Ortam
        {
            TenantId = Guid.NewGuid(), Kod = RandomText("f51"), Sifre = WebFixture.RandomPassword(),
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
        await fx.MakePilotAsync(o.TenantId, true);
        var m = new Customer { Tip = CustomerType.Bireysel, Ad = "Ece", Soyad = "Kaya", CepTel = "05321112233" };
        await WriteDataAsync(o.TenantId, db => db.Customers.Add(m));
        o.MusteriId = m.Id;
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

    private async Task<Guid> VehicleAsync(Ortam o, string branch = "SubeA", string group = "C")
    {
        var v = new Vehicle { Plaka = "34F51" + Guid.NewGuid().ToString("N")[..5].ToUpperInvariant(), Marka = "Fiat", Tip = "Egea", Grup = group, Sube = branch, Durum = VehicleStatus.Musait, Km = 1000 };
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

    private async Task<Oturum> LoginAsync(Ortam o, Kim kim)
    {
        var c = fx.Web.Client();
        var once = CookieValue(await c.GetAsync(V1 + "/oturum/xsrf"), "XSRF-TOKEN")!;
        var req = new HttpRequestMessage(HttpMethod.Post, V1 + "/oturum/giris")
        { Content = JsonContent.Create(new { firma = o.Kod, kullanici = o.Kullanicilar[kim], sifre = o.Sifre }) };
        req.Headers.Add("X-XSRF-TOKEN", once);
        var r = await c.SendAsync(req);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"giriş başarısız: {await r.Content.ReadAsStringAsync()}");
        return new Oturum(c, CookieValue(r, "XSRF-TOKEN")!);
    }

    private static Task<HttpResponseMessage> Gonder(Oturum s, HttpMethod m, string url, object? body = null)
    {
        var req = new HttpRequestMessage(m, url);
        req.Headers.Add("X-XSRF-TOKEN", s.Xsrf);
        if (body is not null) req.Content = JsonContent.Create(body);
        return s.C.SendAsync(req);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == expected, $"Beklenen {(int)expected}, gelen {(int)r.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<JsonElement> ExpectProblem(HttpResponseMessage r, HttpStatusCode status, string code, string? alan = null)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(status == r.StatusCode, $"Beklenen {(int)status}, gelen {(int)r.StatusCode}: {text}");
        var root = JsonDocument.Parse(text).RootElement.Clone();
        Assert.Equal(code, root.GetProperty("kod").GetString());
        if (alan is not null)
            Assert.True(root.GetProperty("errors").TryGetProperty(alan, out _), $"errors[{alan}] yok: {text}");
        return root;
    }

    /// <summary>Rezervasyon gövdesi: "KDV Dahil Günlük" 100 × <paramref name="day"/> gün (oracle: tutar = 100 × gün).</summary>
    private static Dictionary<string, object?> ReservationBody(Ortam o, Guid vehicle, DateTimeOffset start, int day = 3, string office = "SubeA")
        => new()
        {
            ["musteriId"] = o.MusteriId, ["vehicleId"] = vehicle, ["basTar"] = start, ["bitTar"] = start.AddDays(day),
            ["gunlukUcret"] = 100m, ["fiyatTuru"] = "KDV Dahil Günlük", ["cikisOfisi"] = office, ["donusOfisi"] = office,
            ["talepTuru"] = "Kurumsal", ["projeAdi"] = "Fuar",
        };

    private async Task<Guid> OpenReservationAsync(Oturum s, Ortam o, Guid vehicle, DateTimeOffset start, int day = 3, string office = "SubeA")
        => (await Json(await Gonder(s, HttpMethod.Post, TestReservation, ReservationBody(o, vehicle, start, day, office)), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
}
