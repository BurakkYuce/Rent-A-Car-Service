using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Calendar;

namespace RentACar.IntegrationTests;

/// <summary>
/// iCal takvim feed'i (kimliksiz, token). GÜVENLİK-KRİTİK: feed müşteri adı/plaka içerir → token→user (RLS-free
/// Users) → GUC ile SADECE o tenant'ın verisi. BAĞIMSIZ ORACLE: A'nın token'ı A'nın plakasını verir; B'nin
/// token'ı A'nın plakasını ASLA içermez (çapraz-tenant sızma yok); geçersiz token → null (404).
/// </summary>
[Collection("postgres")]
public sealed class CalendarFeedTests(PostgresFixture fx)
{
    private IConfiguration Cfg() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:Default"] = fx.AppConnectionString, // feed app-conn (racar_app) ile okur
    }).Build();

    private AppDbContext Owner() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options,
        NullTenantContext.Instance, NullCurrentUser.Instance);

    private async Task SeedAsync(string token, string plaka)
    {
        await using var db = Owner();
        var t = new Tenant { Code = "cf" + Guid.NewGuid().ToString("N")[..10], Name = "CF" };
        db.Tenants.Add(t);
        db.Users.Add(new User { TenantId = t.Id, UserName = "u" + Guid.NewGuid().ToString("N")[..8],
            DisplayName = "Feed User", CalendarToken = token, PasswordHash = "x" });
        await db.SaveChangesAsync();

        // Tenant-owned (RLS) → GUC gerekir.
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.tenant_id', {t.Id.ToString()}, false)");
        var v = new Vehicle { TenantId = t.Id, Plaka = plaka };
        db.Vehicles.Add(v);
        db.InsurancePolicies.Add(new InsurancePolicy
        {
            TenantId = t.Id, VehicleId = v.Id, Tip = InsuranceType.Trafik,
            Baslangic = DateTimeOffset.UtcNow.AddDays(-345), Bitis = DateTimeOffset.UtcNow.AddDays(20),
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Feed_kendi_verisini_dondurur_capraz_tenant_sizmaz()
    {
        var tokA = "tokA-" + Guid.NewGuid().ToString("N");
        var tokB = "tokB-" + Guid.NewGuid().ToString("N");
        var plakaA = "34AA" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        await SeedAsync(tokA, plakaA);
        await SeedAsync(tokB, "34BB" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant());

        var svc = new CalendarFeedService(Cfg());

        var icsA = await svc.BuildAsync(tokA);
        Assert.NotNull(icsA);
        Assert.Contains("BEGIN:VCALENDAR", icsA);
        Assert.Contains(plakaA, icsA);              // kendi plakası feed'de

        var icsB = await svc.BuildAsync(tokB);
        Assert.NotNull(icsB);
        Assert.DoesNotContain(plakaA, icsB!);        // ÇAPRAZ TENANT SIZMASI YOK
    }

    [Fact]
    public async Task Feed_gecersiz_token_null()
    {
        var svc = new CalendarFeedService(Cfg());
        Assert.Null(await svc.BuildAsync("gecersiz-token-" + Guid.NewGuid().ToString("N")));
    }
}
