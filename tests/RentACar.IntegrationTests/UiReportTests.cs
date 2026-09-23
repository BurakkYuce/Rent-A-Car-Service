using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.VehicleSales;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// F10.1 — <c>/api/ui/v1/raporlar/*</c> GERÇEK Web boru hattında (cookie + CSRF + pilot, <c>racar_app</c>).
/// BAĞIMSIZ ORACLE: defter senaryosu servislerle elle kurulur, beklenen sayılar işlem girdilerinden yazılır
/// (rapor kodundan DEĞİL):
/// <list type="bullet">
/// <item>Nakit gider net 1000 @%20 → Gider 1000, KDV indirilecek 200, Kasa çıkış 1200.</item>
/// <item>Araç satışı (A carisine) net 5000 @%20 → Gelir 5000, KDV 1000, A borç 6000.</item>
/// <item>Araç satışı (ANONİM B carisine) net 1000 @%20 → Gelir 1000, KDV 200, B borç 1200.</item>
/// <item>A'dan nakit tahsilat 3000 → Kasa giriş 3000, A alacak 3000.</item>
/// </list>
/// Beklenen: gelir 6000, gider 1000, net kâr 5000, KDV tahsil 1200 / indirilecek 200, kasa 3000−1200 = 1800,
/// A bakiye 3000, B bakiye 1200, yaşlandırma 0-30 kovası 6000 + 1200 = 7200.
/// </summary>
[Collection("web")]
public sealed partial class UiReportTests(WebFixture fx)
{
    private const string V1 = "/api/ui/v1";
    private const string Rapor = V1 + "/raporlar";
    private const string MusteriA = "Ayla";
    private const string AnonimGercekAd = "Gizlenecek";

    private enum Who { Admin, Accounting, OperatorA, OperatorB }

    private sealed class Env
    {
        public required Guid TenantId { get; init; }
        public required string Code { get; init; }
        public required string Password { get; init; }
        public required Dictionary<Who, string> Users { get; init; }
        public Guid CustomerA { get; set; }
        public Guid CustomerB { get; set; }
        public Guid VehicleA { get; set; }
        public Guid VehicleB { get; set; }
    }

    private static string Random(string prefix) => prefix + Guid.NewGuid().ToString("N")[..10];

    private async Task<Env> SetupAsync(bool ledger = true)
    {
        var e = new Env
        {
            TenantId = Guid.NewGuid(), Code = Random("f101"), Password = WebFixture.RastgeleParola(),
            Users = Enum.GetValues<Who>().ToDictionary(k => k, _ => Random("u")),
        };
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.Pg.OwnerConnectionString).Options;
        await using (var db = new AppDbContext(opts, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            db.Tenants.Add(new Tenant { Id = e.TenantId, Code = e.Code, Name = e.Code, IsActive = true });
            var hasher = fx.Web.Services.GetRequiredService<IPasswordHasher<User>>();
            foreach (var (who, name) in e.Users)
            {
                var (role, branch) = who switch
                {
                    Who.Admin => (UserRole.Admin, (string?)null),
                    Who.OperatorA => (UserRole.Operator, "SubeA"),
                    Who.OperatorB => (UserRole.Operator, "SubeB"),
                    _ => (UserRole.Muhasebe, null),
                };
                var u = new User { TenantId = e.TenantId, UserName = name, DisplayName = name, Rol = role, AtanmisSube = branch, IsActive = true };
                u.PasswordHash = hasher.HashPassword(u, e.Password);
                db.Users.Add(u);
            }
            await db.SaveChangesAsync();
        }
        await fx.PilotYapAsync(e.TenantId, true);

        var a = new Customer { Tip = CariType.Bireysel, Ad = MusteriA, Soyad = "Deniz", CepTel = "05320000001" };
        var b = new Customer { Tip = CariType.Bireysel, Ad = AnonimGercekAd, Soyad = "Kisi", CepTel = "05329999999", AnonimAd = true, AnonimTelefon = true };
        var va = new Vehicle { Plaka = "34RPA" + Guid.NewGuid().ToString("N")[..3].ToUpperInvariant(), Durum = VehicleStatus.Musait, Sube = "SubeA", Grup = "C" };
        var vb = new Vehicle { Plaka = "34RPB" + Guid.NewGuid().ToString("N")[..3].ToUpperInvariant(), Durum = VehicleStatus.Musait, Sube = "SubeB", Grup = "D" };
        await WriteAsync(e.TenantId, db => { db.Customers.AddRange(a, b); db.Vehicles.AddRange(va, vb); });
        (e.CustomerA, e.CustomerB, e.VehicleA, e.VehicleB) = (a.Id, b.Id, va.Id, vb.Id);
        if (!ledger) return e;

        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(e.TenantId);
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Genel, NetTutar = 1000m, KdvOrani = 0.20m, OdemeYontemi = OdemeYontemi.Nakit });
        var sales = sp.GetRequiredService<VehicleSaleService>();
        await sales.CreateAsync(new VehicleSaleInput { VehicleId = va.Id, AliciCariId = a.Id, SatisNet = 5000m, KdvOrani = 0.20m });
        await sales.CreateAsync(new VehicleSaleInput { VehicleId = vb.Id, AliciCariId = b.Id, SatisNet = 1000m, KdvOrani = 0.20m });
        await sp.GetRequiredService<CashService>().CollectAsync(new CashInput { CariId = a.Id, Tutar = 3000m });
        return e;
    }

    private async Task WriteAsync(Guid tenantId, Action<AppDbContext> write)
    {
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(tenantId);
        var f = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        write(db);
        await db.SaveChangesAsync();
    }

    private sealed record Session(HttpClient C, string Xsrf);

    private static string? Cookie(HttpResponseMessage r, string name)
    {
        if (!r.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var v in values)
            if (v.StartsWith(name + "=", StringComparison.Ordinal))
                return Uri.UnescapeDataString(v[(name.Length + 1)..].Split(';')[0]);
        return null;
    }

    private async Task<Session> LoginAsync(Env e, Who who)
    {
        var c = fx.Web.Istemci();
        var first = Cookie(await c.GetAsync(V1 + "/oturum/xsrf"), "XSRF-TOKEN")!;
        var req = new HttpRequestMessage(HttpMethod.Post, V1 + "/oturum/giris")
        { Content = JsonContent.Create(new { firma = e.Code, kullanici = e.Users[who], sifre = e.Password }) };
        req.Headers.Add("X-XSRF-TOKEN", first);
        var r = await c.SendAsync(req);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"giriş başarısız: {await r.Content.ReadAsStringAsync()}");
        return new Session(c, Cookie(r, "XSRF-TOKEN")!);
    }

    private static async Task<JsonElement> GetJson(Session s, string url, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var r = await s.C.GetAsync(url);
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == expected, $"{url}: beklenen {(int)expected}, gelen {(int)r.StatusCode}: {text}");
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task ExpectProblem(Session s, string url, HttpStatusCode status, string? code, string? field = null)
    {
        var root = await GetJson(s, url, status);
        if (code is not null) Assert.Equal(code, root.GetProperty("kod").GetString());
        if (field is not null)
            Assert.True(root.GetProperty("errors").TryGetProperty(field, out _), $"errors[{field}] yok: {root}");
    }

    private static string Today => RentACar.Infrastructure.Persistence.TenantGun.Gun(DateTimeOffset.UtcNow).ToString("yyyy-MM-dd");

    private static decimal Dec(JsonElement e, string name) => e.GetProperty(name).GetDecimal();
}
