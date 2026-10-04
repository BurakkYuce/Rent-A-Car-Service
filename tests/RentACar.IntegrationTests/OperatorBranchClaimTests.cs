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
/// Kabul B-0 (b): şube FK'sı BOŞ, yalnız şube ADI taşıyan operatör (seed/eski hesap, FK dolmadan açılmış oturum)
/// ofis adını şube adıyla karşılaştıran metin yolu yüzünden kendi şubesinin ofisinde ("O1 Havalimanı" → Merkez)
/// kira açamıyordu. Artık kullanıcının şube adı şubeye çözülür ve ofisin bağlı olduğu şubeyle karşılaştırılır.
/// ORACLE: senaryo elle kurulur — O1 Merkez'e, K1 Kadıköy'e bağlı; operatör Merkez → O1 201, K1 403.
/// </summary>
[Collection("web")]
public sealed class OperatorBranchClaimTests(WebFixture fx)
{
    private const string V1 = "/api/ui/v1";

    private sealed record Session(HttpClient C, string Xsrf);

    private static string Rnd(string p) => p + Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task FK_siz_operator_kendi_subesinin_ofisinde_kira_acar_baska_subede_403()
    {
        var tenantId = Guid.NewGuid();
        var code = Rnd("b0");
        var password = WebFixture.RandomPassword();
        var operatorName = Rnd("op");
        var adminName = Rnd("ad");
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.Pg.OwnerConnectionString).Options;
        await using (var db = new AppDbContext(opts, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            db.Tenants.Add(new Tenant { Id = tenantId, Code = code, Name = code, IsActive = true });
            var hasher = fx.Web.Services.GetRequiredService<IPasswordHasher<User>>();
            foreach (var (name, role, branch) in new[] { (operatorName, UserRole.Operator, "Merkez"), (adminName, UserRole.Admin, (string?)null) })
            {
                var u = new User { TenantId = tenantId, UserName = name, DisplayName = name, Rol = role, AtanmisSube = branch, IsActive = true };
                u.PasswordHash = hasher.HashPassword(u, password);
                db.Users.Add(u);
            }
            await db.SaveChangesAsync();
        }

        var customer = new Customer { Tip = CustomerType.Bireysel, Ad = "Deniz", Soyad = "Yılmaz", CepTel = "05320001122" };
        var vehicle = new Vehicle
        {
            Plaka = "34B0" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(), Marka = "Fiat", Tip = "Egea", Grup = "C",
            Sube = "Merkez", Durum = VehicleStatus.Musait, Km = 1000,
        };
        using (var host = new TestHost(fx.Pg.AppConnectionString))
        using (var scope = host.ScopeFor(tenantId))
        {
            var f = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await f.CreateDbContextAsync();
            db.Branches.AddRange(new Branch { Kod = "MRK", Ad = "Merkez" }, new Branch { Kod = "KDK", Ad = "Kadıköy" });
            await db.SaveChangesAsync();
            db.Locations.AddRange(
                new Location { Kod = "O1", Ad = "O1 Havalimanı", Sube = "Merkez" },
                new Location { Kod = "K1", Ad = "K1 Kadıköy", Sube = "Kadıköy" });
            db.Customers.Add(customer);
            db.Vehicles.Add(vehicle);
            await db.SaveChangesAsync();
        }

        // Ön koşul: operatörün FK'sı GERÇEKTEN boş (owner bağlamı interceptor'sız yazdı); ofisler şubelere bağlı.
        await using (var db = new AppDbContext(opts, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            Assert.Null((await db.Users.AsNoTracking().SingleAsync(u => u.TenantId == tenantId && u.UserName == operatorName)).AtanmisSubeId);
            await TenantGuc.OpenAsync(db, tenantId);
            var o1 = await db.Locations.IgnoreQueryFilters().SingleAsync(l => l.TenantId == tenantId && l.Kod == "O1");
            var merkez = await db.Branches.IgnoreQueryFilters().SingleAsync(b => b.TenantId == tenantId && b.Kod == "MRK");
            Assert.Equal(merkez.Id, o1.SubeId);
        }

        var s = await LoginAsync(code, operatorName, password);
        var start = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()).AddHours(-1);

        var own = await OpenRentalAsync(s, customer.Id, vehicle.Id, start, "O1 Havalimanı");
        var ownText = await own.Content.ReadAsStringAsync();
        Assert.True(own.StatusCode == HttpStatusCode.Created, $"kendi şubesi: beklenen 201, gelen {(int)own.StatusCode}: {ownText}");
        var rentalId = JsonDocument.Parse(ownText).RootElement.GetProperty("id").GetGuid();

        // Kendi kaydını okuyabilir ve listesinde görür (guard ve liste aynı kuraldan).
        Assert.Equal(HttpStatusCode.OK, (await s.C.GetAsync($"{V1}/kiralar/{rentalId}")).StatusCode);

        var other = await OpenRentalAsync(s, customer.Id, vehicle.Id, start.AddDays(10), "K1 Kadıköy");
        var otherText = await other.Content.ReadAsStringAsync();
        Assert.True(other.StatusCode == HttpStatusCode.Forbidden, $"başka şube: beklenen 403, gelen {(int)other.StatusCode}: {otherText}");
        Assert.Equal("yetki_yok", JsonDocument.Parse(otherText).RootElement.GetProperty("kod").GetString());
    }

    private Task<HttpResponseMessage> OpenRentalAsync(Session s, Guid customer, Guid vehicle, DateTimeOffset start, string office)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, V1 + "/kiralar")
        {
            Content = JsonContent.Create(new
            {
                musteriId = customer, vehicleId = vehicle, basTar = start, bitTar = start.AddDays(3),
                gunlukUcret = 100m, fiyatTuru = "Günlük", cikisOfisi = office, donusOfisi = office,
            }),
        };
        req.Headers.Add("X-XSRF-TOKEN", s.Xsrf);
        return s.C.SendAsync(req);
    }

    private async Task<Session> LoginAsync(string company, string user, string password)
    {
        var c = fx.Web.Client();
        var once = Cookie(await c.GetAsync(V1 + "/oturum/xsrf"), "XSRF-TOKEN")!;
        var req = new HttpRequestMessage(HttpMethod.Post, V1 + "/oturum/giris")
        {
            Content = JsonContent.Create(new { firma = company, kullanici = user, sifre = password }),
        };
        req.Headers.Add("X-XSRF-TOKEN", once);
        var r = await c.SendAsync(req);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"giriş başarısız: {await r.Content.ReadAsStringAsync()}");
        return new Session(c, Cookie(r, "XSRF-TOKEN")!);
    }

    private static string? Cookie(HttpResponseMessage r, string name)
    {
        if (!r.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var d in values)
            if (d.StartsWith(name + "=", StringComparison.Ordinal))
                return Uri.UnescapeDataString(d[(name.Length + 1)..].Split(';')[0]);
        return null;
    }
}
