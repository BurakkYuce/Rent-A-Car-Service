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

    private async Task SeedAsync(string token, string plate)
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
        var v = new Vehicle { TenantId = t.Id, Plaka = plate };
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
        var plateA = "34AA" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        await SeedAsync(tokA, plateA);
        await SeedAsync(tokB, "34BB" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant());

        var svc = new CalendarFeedService(Cfg());

        var icsA = await svc.BuildAsync(tokA);
        Assert.NotNull(icsA);
        Assert.Contains("BEGIN:VCALENDAR", icsA);
        Assert.Contains(plateA, icsA);              // kendi plakası feed'de

        var icsB = await svc.BuildAsync(tokB);
        Assert.NotNull(icsB);
        Assert.DoesNotContain(plateA, icsB!);        // ÇAPRAZ TENANT SIZMASI YOK
    }

    [Fact]
    public async Task Feed_gecersiz_token_null()
    {
        var svc = new CalendarFeedService(Cfg());
        Assert.Null(await svc.BuildAsync("gecersiz-token-" + Guid.NewGuid().ToString("N")));
    }

    [Fact]
    public async Task Operator_feed_sube_kapsamli_ve_fatura_gormez() // denetim O4
    {
        var tokAdmin = "tokAd-" + Guid.NewGuid().ToString("N");
        var tokOp = "tokOp-" + Guid.NewGuid().ToString("N");
        var plateHeadOffice = "34MK" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var plateBranch2 = "34SB" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        await using (var db = Owner())
        {
            var t = new Tenant { Code = "cf" + Guid.NewGuid().ToString("N")[..10], Name = "CF-O4" };
            db.Tenants.Add(t);
            db.Users.Add(new User { TenantId = t.Id, UserName = "ad" + Guid.NewGuid().ToString("N")[..8],
                DisplayName = "Admin", Rol = UserRole.Admin, CalendarToken = tokAdmin, PasswordHash = "x" });
            db.Users.Add(new User { TenantId = t.Id, UserName = "op" + Guid.NewGuid().ToString("N")[..8],
                DisplayName = "Operator", Rol = UserRole.Operator, AtanmisSube = "MERKEZ", CalendarToken = tokOp, PasswordHash = "x" });
            await db.SaveChangesAsync();

            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.tenant_id', {t.Id.ToString()}, false)");
            var vM = new Vehicle { TenantId = t.Id, Plaka = plateHeadOffice, Sube = "MERKEZ" };
            var vS = new Vehicle { TenantId = t.Id, Plaka = plateBranch2, Sube = "SUBE2" };
            db.Vehicles.AddRange(vM, vS);
            // Araç-bazlı vade: iki araca da sigorta.
            db.InsurancePolicies.Add(new InsurancePolicy { TenantId = t.Id, VehicleId = vM.Id, Tip = InsuranceType.Trafik,
                Baslangic = DateTimeOffset.UtcNow.AddDays(-300), Bitis = DateTimeOffset.UtcNow.AddDays(15) });
            db.InsurancePolicies.Add(new InsurancePolicy { TenantId = t.Id, VehicleId = vS.Id, Tip = InsuranceType.Trafik,
                Baslangic = DateTimeOffset.UtcNow.AddDays(-300), Bitis = DateTimeOffset.UtcNow.AddDays(15) });
            // Finansal: vade tarihli fatura (Operatör GÖRMEMELİ).
            db.Invoices.Add(new Invoice { TenantId = t.Id, No = "FT-O4-" + Guid.NewGuid().ToString("N")[..6],
                CariId = Guid.NewGuid(), Tarih = DateTimeOffset.UtcNow, VadeTarihi = DateTimeOffset.UtcNow.AddDays(10),
                NetTutar = 100m, KdvTutar = 20m, GenelToplam = 120m, Currency = "TRY", Kur = 1m });
            await db.SaveChangesAsync();
        }

        var svc = new CalendarFeedService(Cfg());

        var icsOp = await svc.BuildAsync(tokOp);
        Assert.NotNull(icsOp);
        Assert.Contains(plateHeadOffice, icsOp);          // kendi şubesinin aracı
        Assert.DoesNotContain(plateBranch2, icsOp!);     // BAŞKA ŞUBE SIZMAZ (yan kapı kapandı)
        Assert.DoesNotContain("Fatura", icsOp);        // finansal vade Operatöre yok

        var icsAdmin = await svc.BuildAsync(tokAdmin);
        Assert.Contains(plateHeadOffice, icsAdmin!);       // Admin tüm şubeler + fatura
        Assert.Contains(plateBranch2, icsAdmin!);
        Assert.Contains("Fatura", icsAdmin!);
    }

    [Fact]
    public async Task TokenService_app_conn_ile_yazar_owner_gerekmez() // denetim O3
    {
        Guid userId;
        await using (var db = Owner())
        {
            var t = new Tenant { Code = "cf" + Guid.NewGuid().ToString("N")[..10], Name = "CF-O3" };
            var u = new User { TenantId = t.Id, UserName = "tk" + Guid.NewGuid().ToString("N")[..8],
                DisplayName = "Token User", PasswordHash = "x" };
            db.Tenants.Add(t);
            db.Users.Add(u);
            await db.SaveChangesAsync();
            userId = u.Id;
        }

        // Cfg() YALNIZ Default (racar_app) içerir — Migrator/owner conn YOK: O3 fix bununla kanıtlanır
        // (users_update policy'si GUC=kullanıcının tenant'ı ile yazıma izin verir).
        var svc = new CalendarTokenService(Cfg());

        var t1 = await svc.EnsureAsync(userId);
        Assert.False(string.IsNullOrEmpty(t1));
        Assert.Equal(t1, await svc.EnsureAsync(userId));      // idempotent (mevcut token korunur)
        var t3 = await svc.RegenerateAsync(userId);
        Assert.NotEqual(t1, t3);                              // yenileme eskisini geçersiz kılar

        await using var check = Owner();
        Assert.Equal(t3, (await check.Users.AsNoTracking().FirstAsync(u => u.Id == userId)).CalendarToken);
    }
}
