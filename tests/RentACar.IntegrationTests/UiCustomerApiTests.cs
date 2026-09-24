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
/// F7.1 — <c>/api/ui/v1/{cariler,anketler,sikayetler,assistans-talepleri,hukuk-dosyalari,crm}</c> GERÇEK Web boru
/// hattında (cookie + CSRF + pilot, <c>racar_app</c>). BAĞIMSIZ ORACLE: cariler, kiralar ve CRM kayıtları elle kurulur;
/// beklenen değerler (maske, görünen ad, kapsam) senaryodan yazılır. TC'ler çalışma anında üretilen geçerli numaralardır
/// (sabit kimlik YOK); kullanıcılar rastgele parolayla üretilir.
/// </summary>
[Collection("web")]
public sealed partial class UiCustomerApiTests(WebFixture fx)
{
    private const string V1 = "/api/ui/v1";
    private const string Customers = V1 + "/cariler";

    private enum Who { Admin, OperatorA, OperatorB, Accounting }

    private sealed class Env
    {
        public required Guid TenantId { get; init; }
        public required string Code { get; init; }
        public required string Password { get; init; }
        public required Dictionary<Who, string> Users { get; init; }
    }

    private static string RandomName(string prefix) => prefix + Guid.NewGuid().ToString("N")[..10];

    /// <summary>Geçerli (sağlama haneli) rastgele TC — algoritma elle: 10. hane ((tekler×7) − çiftler) mod 10, 11. hane ilk 10'un toplamı mod 10.</summary>
    private static string RandomTc()
    {
        var d = new int[11];
        d[0] = Random.Shared.Next(1, 10);
        for (var i = 1; i < 9; i++) d[i] = Random.Shared.Next(0, 10);
        var odd = d[0] + d[2] + d[4] + d[6] + d[8];
        var even = d[1] + d[3] + d[5] + d[7];
        d[9] = ((odd * 7 - even) % 10 + 10) % 10;
        d[10] = d.Take(10).Sum() % 10;
        return string.Concat(d);
    }

    private async Task<Env> SetupAsync()
    {
        var e = new Env
        {
            TenantId = Guid.NewGuid(), Code = RandomName("f71"), Password = WebFixture.RastgeleParola(),
            Users = Enum.GetValues<Who>().ToDictionary(k => k, _ => RandomName("u")),
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

    private async Task<T> ReadAsync<T>(Guid tenantId, Func<AppDbContext, Task<T>> read)
    {
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(tenantId);
        var f = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        return await read(db);
    }

    /// <summary>Kira (şube = çıkış ofisi metni; lokasyon kaydı yok → metin kapsamı) + aracı.</summary>
    private async Task<(Guid RentalId, string Plate, string ContractNo)> RentalAsync(Env e, Guid customerId, string office)
    {
        var plate = "34F" + Guid.NewGuid().ToString("N")[..5].ToUpperInvariant();
        var v = new Vehicle { Plaka = plate, Sube = office, Durum = VehicleStatus.Kirada };
        var no = "K-" + Guid.NewGuid().ToString("N")[..8];
        var r = new RentalContract
        {
            SozlesmeNo = no, MusteriId = customerId, VehicleId = v.Id, Durum = RentalStatus.Kirada, CikisOfisi = office,
            BasTar = TestZaman.GunSonra(-2), BitTar = TestZaman.GunSonra(3), GenelToplam = 1500m, KurSnapshot = 1m,
        };
        await WriteAsync(e.TenantId, db => { db.Vehicles.Add(v); db.Rentals.Add(r); });
        return (r.Id, plate, no);
    }

    private sealed record Session(HttpClient C, string Xsrf);

    private static string? Cookie(HttpResponseMessage r, string name)
    {
        if (!r.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var d in values)
            if (d.StartsWith(name + "=", StringComparison.Ordinal))
                return Uri.UnescapeDataString(d[(name.Length + 1)..].Split(';')[0]);
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

    private static Task<HttpResponseMessage> Send(Session s, HttpMethod m, string url, object? body = null)
    {
        var req = new HttpRequestMessage(m, url);
        req.Headers.Add("X-XSRF-TOKEN", s.Xsrf);
        if (body is not null) req.Content = JsonContent.Create(body);
        return s.C.SendAsync(req);
    }

    /// <summary>Yanıt gövdesi (ham metin) + JSON. Ham metin TC taraması için döner.</summary>
    private static async Task<(JsonElement Json, string Raw)> Json(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == expected, $"Beklenen {(int)expected}, gelen {(int)r.StatusCode}: {text}");
        return (JsonDocument.Parse(text).RootElement.Clone(), text);
    }

    private static async Task<string> Problem(HttpResponseMessage r, HttpStatusCode status, string? code, string? field = null)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(status == r.StatusCode, $"Beklenen {(int)status}, gelen {(int)r.StatusCode}: {text}");
        var root = JsonDocument.Parse(text).RootElement;
        if (code is not null) Assert.Equal(code, root.GetProperty("kod").GetString());
        if (field is not null)
            Assert.True(root.GetProperty("errors").TryGetProperty(field, out _), $"errors[{field}] yok: {text}");
        return text;
    }

    private static List<JsonElement> Records(JsonElement page) => page.GetProperty("kayitlar").EnumerateArray().ToList();
}
