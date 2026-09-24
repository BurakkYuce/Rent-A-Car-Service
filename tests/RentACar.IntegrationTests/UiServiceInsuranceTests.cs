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
/// F9.1 — <c>/api/ui/v1</c> servis / sigorta-MTV-muayene / vade / fiyat-tarife uçları GERÇEK Web boru hattında (cookie +
/// CSRF + pilot, <c>racar_app</c>). BAĞIMSIZ ORACLE: beklenenler elle kurulmuş senaryodan (MTV 1.000 = 400 + 600;
/// muayene 500 + ceza 50 = 550; EUR poliçe 1.000 × 30 = 30.000 baz; servis 1.000 + 2 × 250 = 1.500 × %50 = 750;
/// tarife 3 gün × 100 = 300 + ürün 3 × 50 = 150). Kullanıcılar çalışma anında rastgele parolayla üretilir.
/// </summary>
[Collection("web")]
public sealed partial class UiServiceInsuranceTests(WebFixture fx)
{
    private const string V1 = "/api/ui/v1";

    private enum Who { Admin, OperatorA, OperatorB, Accounting }

    private sealed class Env
    {
        public required Guid TenantId { get; init; }
        public required string Code { get; init; }
        public required string Password { get; init; }
        public required Dictionary<Who, string> Users { get; init; }
        public Guid CustomerId { get; set; }
    }

    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..10];
    private static string Key() => Guid.NewGuid().ToString("D");

    private async Task<Env> SetupAsync()
    {
        var e = new Env
        {
            TenantId = Guid.NewGuid(), Code = Unique("f91"), Password = WebFixture.RastgeleParola(),
            Users = Enum.GetValues<Who>().ToDictionary(k => k, _ => Unique("u")),
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
        var c = new Customer { Tip = CariType.Bireysel, Ad = "Ece", Soyad = "Tan", CepTel = "05321119988" };
        await WriteAsync(e.TenantId, db => db.Customers.Add(c));
        e.CustomerId = c.Id;
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

    /// <summary><c>racar_app</c> + tenant context (real RLS).</summary>
    private async Task<T> ReadAsync<T>(Guid tenantId, Func<AppDbContext, Task<T>> read)
    {
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(tenantId);
        var f = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        return await read(db);
    }

    private async Task<Guid> VehicleAsync(Env e, string branch = "SubeA")
    {
        var v = new Vehicle { Plaka = "34F91" + Guid.NewGuid().ToString("N")[..5].ToUpperInvariant(), Marka = "Fiat", Tip = "Egea",
            Grup = "C", Sube = branch, Durum = VehicleStatus.Musait, Km = 1000 };
        await WriteAsync(e.TenantId, db => db.Vehicles.Add(v));
        return v.Id;
    }

    /// <summary>Σ debit / Σ credit (base) and row count of the ledger set of one source.</summary>
    private Task<(decimal Debit, decimal Credit, int Rows)> LedgerAsync(Guid tenant, string sourceType, Guid? sourceId = null)
        => ReadAsync(tenant, async db =>
        {
            var l = await db.AccountLedgerEntries.AsNoTracking()
                .Where(x => x.SourceType == sourceType && (sourceId == null || x.SourceId == sourceId)).ToListAsync();
            return (l.Where(x => x.Direction == LedgerDirection.Debit).Sum(x => x.Amount.AmountInBase),
                l.Where(x => x.Direction == LedgerDirection.Credit).Sum(x => x.Amount.AmountInBase), l.Count);
        });

    private sealed record Session(HttpClient C, string Xsrf);

    private static string? CookieValue(HttpResponseMessage r, string name)
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
        var before = CookieValue(await c.GetAsync(V1 + "/oturum/xsrf"), "XSRF-TOKEN")!;
        var req = new HttpRequestMessage(HttpMethod.Post, V1 + "/oturum/giris")
        { Content = JsonContent.Create(new { firma = e.Code, kullanici = e.Users[who], sifre = e.Password }) };
        req.Headers.Add("X-XSRF-TOKEN", before);
        var r = await c.SendAsync(req);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"login failed: {await r.Content.ReadAsStringAsync()}");
        return new Session(c, CookieValue(r, "XSRF-TOKEN")!);
    }

    private static Task<HttpResponseMessage> Send(Session s, HttpMethod m, string url, object? body = null, string? key = null)
    {
        var req = new HttpRequestMessage(m, url);
        req.Headers.Add("X-XSRF-TOKEN", s.Xsrf);
        if (key is not null) req.Headers.Add("Idempotency-Key", key);
        if (body is not null) req.Content = JsonContent.Create(body);
        return s.C.SendAsync(req);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == expected, $"Expected {(int)expected}, got {(int)r.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<JsonElement> Problem(HttpResponseMessage r, HttpStatusCode status, string code, string? field = null)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(status == r.StatusCode, $"Expected {(int)status}, got {(int)r.StatusCode}: {text}");
        var root = JsonDocument.Parse(text).RootElement.Clone();
        Assert.Equal(code, root.GetProperty("kod").GetString());
        if (field is not null)
            Assert.True(root.GetProperty("errors").TryGetProperty(field, out _), $"errors[{field}] missing: {text}");
        return root;
    }
}
