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
/// F6.1b — <c>/api/ui/v1/{arac-kredileri,musteri-taksitleri,arac-siparisleri,baflar,hasar-dosyalari,filo-plan}</c> GERÇEK
/// Web boru hattında (cookie + CSRF + pilot, <c>racar_app</c>). BAĞIMSIZ ORACLE: beklenenler elle kurulmuş senaryodan —
/// kredi 12.000 TRY × %10 yıllık × 12 ay → faiz 1.200, toplam 13.200, aylık 1.100; müşteri planı 1.000 / 3 →
/// 333,33 + 333,33 + 333,34. Kullanıcılar çalışma anında rastgele parolayla üretilir (depoda kimlik bilgisi yok).
/// </summary>
[Collection("web")]
public sealed partial class UiAracFinansTests(WebFixture fx)
{
    private const string V1 = "/api/ui/v1";
    private const string Loan = V1 + "/arac-kredileri";
    private const string Installment = V1 + "/musteri-taksitleri";
    private const string Order = V1 + "/arac-siparisleri";
    private const string Baf = V1 + "/baflar";
    private const string Damage = V1 + "/hasar-dosyalari";
    private const string Plan = V1 + "/filo-plan";

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
    private static string Key() => Guid.NewGuid().ToString("D");

    private async Task<Ortam> SetUpEnvironmentAsync()
    {
        var o = new Ortam
        {
            TenantId = Guid.NewGuid(), Kod = RandomText("f61b"), Sifre = WebFixture.RandomPassword(),
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
        var m = new Customer { Tip = CustomerType.Bireysel, Ad = "Deniz", Soyad = "Ak", CepTel = "05321112244" };
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

    /// <summary><c>racar_app</c> + kiracı bağlamıyla okuma (RLS gerçek).</summary>
    private async Task<T> ReadDataAsync<T>(Guid tenantId, Func<AppDbContext, Task<T>> read)
    {
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(tenantId);
        var f = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        return await read(db);
    }

    private async Task<Guid> VehicleAsync(Ortam o, string branch = "SubeA")
    {
        var v = new Vehicle { Plaka = "34F6B" + Guid.NewGuid().ToString("N")[..5].ToUpperInvariant(), Marka = "Fiat", Tip = "Egea", Grup = "C", Sube = branch, Durum = VehicleStatus.Musait, Km = 1000 };
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

    private static Task<HttpResponseMessage> Gonder(Oturum s, HttpMethod m, string url, object? body = null, string? key = null)
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
}
