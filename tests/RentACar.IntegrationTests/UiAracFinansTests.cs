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
    private const string Kredi = V1 + "/arac-kredileri";
    private const string Taksit = V1 + "/musteri-taksitleri";
    private const string Siparis = V1 + "/arac-siparisleri";
    private const string Baf = V1 + "/baflar";
    private const string Hasar = V1 + "/hasar-dosyalari";
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

    private static string Rastgele(string onek) => onek + Guid.NewGuid().ToString("N")[..10];
    private static string Anahtar() => Guid.NewGuid().ToString("D");

    private async Task<Ortam> OrtamKurAsync()
    {
        var o = new Ortam
        {
            TenantId = Guid.NewGuid(), Kod = Rastgele("f61b"), Sifre = WebFixture.RastgeleParola(),
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
        await fx.PilotYapAsync(o.TenantId, true);
        var m = new Customer { Tip = CariType.Bireysel, Ad = "Deniz", Soyad = "Ak", CepTel = "05321112244" };
        await VeriYazAsync(o.TenantId, db => db.Customers.Add(m));
        o.MusteriId = m.Id;
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

    /// <summary><c>racar_app</c> + kiracı bağlamıyla okuma (RLS gerçek).</summary>
    private async Task<T> VeriOkuAsync<T>(Guid tenantId, Func<AppDbContext, Task<T>> oku)
    {
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(tenantId);
        var f = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        return await oku(db);
    }

    private async Task<Guid> AracAsync(Ortam o, string sube = "SubeA")
    {
        var v = new Vehicle { Plaka = "34F6B" + Guid.NewGuid().ToString("N")[..5].ToUpperInvariant(), Marka = "Fiat", Tip = "Egea", Grup = "C", Sube = sube, Durum = VehicleStatus.Musait, Km = 1000 };
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

    private async Task<Oturum> GirisAsync(Ortam o, Kim kim)
    {
        var c = fx.Web.Istemci();
        var once = CerezDegeri(await c.GetAsync(V1 + "/oturum/xsrf"), "XSRF-TOKEN")!;
        var req = new HttpRequestMessage(HttpMethod.Post, V1 + "/oturum/giris")
        { Content = JsonContent.Create(new { firma = o.Kod, kullanici = o.Kullanicilar[kim], sifre = o.Sifre }) };
        req.Headers.Add("X-XSRF-TOKEN", once);
        var r = await c.SendAsync(req);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"giriş başarısız: {await r.Content.ReadAsStringAsync()}");
        return new Oturum(c, CerezDegeri(r, "XSRF-TOKEN")!);
    }

    private static Task<HttpResponseMessage> Gonder(Oturum s, HttpMethod m, string url, object? govde = null, string? anahtar = null)
    {
        var req = new HttpRequestMessage(m, url);
        req.Headers.Add("X-XSRF-TOKEN", s.Xsrf);
        if (anahtar is not null) req.Headers.Add("Idempotency-Key", anahtar);
        if (govde is not null) req.Content = JsonContent.Create(govde);
        return s.C.SendAsync(req);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r, HttpStatusCode beklenen = HttpStatusCode.OK)
    {
        var metin = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == beklenen, $"Beklenen {(int)beklenen}, gelen {(int)r.StatusCode}: {metin}");
        return JsonDocument.Parse(metin).RootElement.Clone();
    }

    private static async Task<JsonElement> ProblemBekle(HttpResponseMessage r, HttpStatusCode durum, string kod, string? alan = null)
    {
        var metin = await r.Content.ReadAsStringAsync();
        Assert.True(durum == r.StatusCode, $"Beklenen {(int)durum}, gelen {(int)r.StatusCode}: {metin}");
        var kok = JsonDocument.Parse(metin).RootElement.Clone();
        Assert.Equal(kod, kok.GetProperty("kod").GetString());
        if (alan is not null)
            Assert.True(kok.GetProperty("errors").TryGetProperty(alan, out _), $"errors[{alan}] yok: {metin}");
        return kok;
    }
}
