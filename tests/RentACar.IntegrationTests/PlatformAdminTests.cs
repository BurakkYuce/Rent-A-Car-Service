using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RentACar.Application.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Identity;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Platform;

namespace RentACar.IntegrationTests;

/// <summary>
/// Platform süper-admin konsolu (cross-tenant + erişim aç/kapa). BAĞIMSIZ ORACLE: elle verilen firma
/// kodu/parola ile oluşturulan tenant o kimlikle GİRER; kapatınca giriş ENGELLENİR; owner tüm tenant'ları
/// (+ kullanıcı sayısı) görür; çift-kod ve zayıf-parola reddedilir. PlatformAdminService owner (Migrator)
/// bağlantısıyla yalnız Tenants/Users platform tablolarına dokunur; LoginService app (racar_app) ile okur.
/// </summary>
[Collection("postgres")]
public sealed class PlatformAdminTests(PostgresFixture fx)
{
    private static string UniqCode() => "t" + Guid.NewGuid().ToString("N")[..10];

    private (PlatformAdminService Svc, LoginService Login) Build()
    {
        var appOptions = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.AppConnectionString).Options;
        var factory = new ScopedAppDbContextFactory(appOptions, NullTenantContext.Instance, NullCurrentUser.Instance);
        var statusCache = new TenantStatusCache(new MemoryCache(new MemoryCacheOptions()), factory);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Migrator"] = fx.OwnerConnectionString,
            }).Build();
        var svc = new PlatformAdminService(config, new AspNetPasswordHasher(), statusCache,
            NullLogger<PlatformAdminService>.Instance);
        var login = new LoginService(factory, new PasswordHasher<User>());
        return (svc, login);
    }

    [Fact]
    public async Task Olustur_sonra_giris_calisir()
    {
        var (svc, login) = Build();
        var code = UniqCode();
        await svc.CreateTenantAsync(code, "Test Co", "admin", "sifre123", "op");

        Assert.NotNull(await login.ValidateAsync(code, "admin", "sifre123")); // oracle: elle verilen kimlik
        Assert.Null(await login.ValidateAsync(code, "admin", "yanlis"));      // yanlış parola
        Assert.Null(await login.ValidateAsync(code, "yok", "sifre123"));      // olmayan kullanıcı
    }

    [Fact]
    public async Task Kapat_girisi_engeller_ac_geri_alir()
    {
        var (svc, login) = Build();
        var code = UniqCode();
        await svc.CreateTenantAsync(code, "Test Co", "admin", "sifre123", "op");
        var id = (await svc.ListTenantsAsync()).First(t => t.Code == code).Id;

        Assert.NotNull(await login.ValidateAsync(code, "admin", "sifre123")); // açık → girer
        await svc.SetActiveAsync(id, false, "op");
        Assert.Null(await login.ValidateAsync(code, "admin", "sifre123"));    // KAPALI → giriş engellendi
        await svc.SetActiveAsync(id, true, "op");
        Assert.NotNull(await login.ValidateAsync(code, "admin", "sifre123")); // tekrar açık
    }

    [Fact]
    public async Task Liste_tenantlari_ve_kullanici_sayilarini_cross_tenant_gosterir()
    {
        var (svc, _) = Build();
        var c1 = UniqCode();
        var c2 = UniqCode();
        await svc.CreateTenantAsync(c1, "A", "admin", "sifre123", "op");
        await svc.CreateTenantAsync(c2, "B", "admin", "sifre123", "op");
        await EkKullaniciAsync(c1); // c1'e 2. kullanıcı → sayı 2 olmalı

        var rows = await svc.ListTenantsAsync();
        Assert.Contains(rows, r => r.Code == c1); // owner cross-tenant tümünü görür
        Assert.Contains(rows, r => r.Code == c2);
        Assert.Equal(2, rows.First(r => r.Code == c1).UserCount); // oracle: admin + ek = 2
        Assert.Equal(1, rows.First(r => r.Code == c2).UserCount); // yalnız admin
    }

    [Fact]
    public async Task Cift_kod_ve_zayif_parola_reddedilir()
    {
        var (svc, _) = Build();
        var code = UniqCode();
        await svc.CreateTenantAsync(code, "A", "admin", "sifre123", "op");
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateTenantAsync(code, "A2", "admin2", "sifre123", "op")); // çift kod
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateTenantAsync(UniqCode(), "B", "admin", "123", "op"));   // parola < 6
    }

    private async Task EkKullaniciAsync(string code)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options;
        await using var db = new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance);
        var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Code == code);
        var u = new User { TenantId = tenant.Id, UserName = "op2", DisplayName = "op2", Rol = UserRole.Operator };
        u.PasswordHash = new PasswordHasher<User>().HashPassword(u, "sifre123");
        db.Users.Add(u);
        await db.SaveChangesAsync();
    }
}
