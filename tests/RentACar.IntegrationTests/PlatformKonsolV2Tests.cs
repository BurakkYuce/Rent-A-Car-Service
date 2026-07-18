using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Identity;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Platform;

namespace RentACar.IntegrationTests;

/// <summary>
/// Platform konsolu v2: Kapat/Yeniden-Aç (Kapalı ≠ Pasif — damga + IsActive birlikte), bilgi
/// güncelleme, cross-tenant metrikler, son-giriş yazımı. BAĞIMSIZ ORACLE: beklenenler elle kurulan
/// senaryodan. Kemer testi (Kapanis set + IsActive elle TRUE) defense-in-depth'in gerçekten
/// çalıştığını pinler — normal yol testi bunu KAPSAMAZ.
/// </summary>
[Collection("postgres")]
public sealed class PlatformKonsolV2Tests(PostgresFixture fx)
{
    private static string UniqCode() => "k" + Guid.NewGuid().ToString("N")[..10];

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
        var login = new LoginService(factory, new PasswordHasher<User>(), NullLogger<LoginService>.Instance);
        return (svc, login);
    }

    /// <summary>Owner bağlantısıyla ham tenant güncellemesi (DB anomali simülasyonu için).</summary>
    private async Task OwnerUpdateAsync(Func<AppDbContext, Task> islem)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options;
        await using var db = new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance);
        await islem(db);
    }

    // ---- 1. Kapat → login engellenir + iki bayrak set; Yeniden Aç → login çalışır + temiz ----
    [Fact]
    public async Task Kapat_girisi_engeller_yeniden_ac_geri_alir()
    {
        var (svc, login) = Build();
        var code = UniqCode();
        await svc.CreateTenantAsync(code, "Kapat Testi", "admin", "sifre123", "op");
        var id = (await svc.ListTenantsAsync()).First(t => t.Code == code).Id;

        await svc.CloseAsync(id, "op");
        Assert.Null(await login.ValidateAsync(code, "admin", "sifre123")); // kapalı → giremez

        var d = await svc.GetTenantAsync(id);
        Assert.NotNull(d);
        Assert.False(d!.IsActive);                 // iki bayrak BİRLİKTE set (tek yaptırım yolu)
        Assert.NotNull(d.KapanisTarihiUtc);
        Assert.Equal("Kapalı", d.Durum);

        await svc.ReopenAsync(id, "op");
        Assert.NotNull(await login.ValidateAsync(code, "admin", "sifre123")); // geri açıldı
        d = await svc.GetTenantAsync(id);
        Assert.True(d!.IsActive);
        Assert.Null(d.KapanisTarihiUtc);
        Assert.Equal("Aktif", d.Durum);
    }

    // ---- 2. Kapalı tenant toggle-ile-açılamaz (Yeniden Aç zorunlu) ----
    [Fact]
    public async Task Kapali_tenant_toggle_ile_acilamaz()
    {
        var (svc, _) = Build();
        var code = UniqCode();
        await svc.CreateTenantAsync(code, "Toggle Testi", "admin", "sifre123", "op");
        var id = (await svc.ListTenantsAsync()).First(t => t.Code == code).Id;

        await svc.CloseAsync(id, "op");
        await Assert.ThrowsAsync<ValidationException>(() => svc.SetActiveAsync(id, true, "op"));
        Assert.Equal("Kapalı", (await svc.GetTenantAsync(id))!.Durum); // hâlâ kapalı
    }

    // ---- 3. KEMER (defense-in-depth): Kapanis set + IsActive elle TRUE → login YİNE engelli ----
    [Fact]
    public async Task Kemer_kapanis_damgasi_isactive_true_olsa_bile_girisi_engeller()
    {
        var (svc, login) = Build();
        var code = UniqCode();
        await svc.CreateTenantAsync(code, "Kemer Testi", "admin", "sifre123", "op");
        var id = (await svc.ListTenantsAsync()).First(t => t.Code == code).Id;
        await svc.CloseAsync(id, "op");

        // DB anomalisi: IsActive elle TRUE yapılır (Kapanis damgası durur).
        await OwnerUpdateAsync(db => db.Tenants.Where(t => t.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive, true)));

        Assert.Null(await login.ValidateAsync(code, "admin", "sifre123")); // kemer tuttu
        Assert.Equal("Kapalı", (await svc.GetTenantAsync(id))!.Durum);      // rozet önceliği de Kapalı
    }

    // ---- 4. Update round-trip + sınırlar + Code değişmez ----
    [Fact]
    public async Task Update_persist_eder_sinirlari_dogrular_code_degismez()
    {
        var (svc, _) = Build();
        var code = UniqCode();
        await svc.CreateTenantAsync(code, "Bilgi Testi", "admin", "sifre123", "op");
        var id = (await svc.ListTenantsAsync()).First(t => t.Code == code).Id;

        await svc.UpdateTenantAsync(id, "Yeni Ad", "Ahmet Yılmaz", "a@b.co", "05551112233",
            "Ödeme düzenli.", "Pro", "op");
        var d = await svc.GetTenantAsync(id);
        Assert.Equal("Yeni Ad", d!.Name);
        Assert.Equal("Ahmet Yılmaz", d.YetkiliAd);
        Assert.Equal("a@b.co", d.Eposta);
        Assert.Equal("05551112233", d.Telefon);
        Assert.Equal("Ödeme düzenli.", d.Notlar);
        Assert.Equal("Pro", d.Plan);
        Assert.Equal(code, d.Code);            // login anahtarı DEĞİŞMEZ
        Assert.NotNull(d.UpdatedAtUtc);

        // 2001-char not → anlamlı red (L1 deseni), kayıt değişmez.
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.UpdateTenantAsync(id, "Yeni Ad", null, null, null, new string('x', 2001), null, "op"));
        Assert.Equal("Ödeme düzenli.", (await svc.GetTenantAsync(id))!.Notlar);
    }

    // ---- 5. Metrikler doğru + cross-tenant karışmaz ----
    [Fact]
    public async Task Metrikler_dogru_ve_cross_tenant_karismaz()
    {
        var (svc, _) = Build();
        var codeA = UniqCode(); var codeB = UniqCode();
        await svc.CreateTenantAsync(codeA, "Tenant A", "admin", "sifre123", "op");
        await svc.CreateTenantAsync(codeB, "Tenant B", "admin", "sifre123", "op");
        var rows = await svc.ListTenantsAsync();
        var idA = rows.First(t => t.Code == codeA).Id;
        var idB = rows.First(t => t.Code == codeB).Id;

        // A: 2 araç + 1 Kirada kira + son-30-gün Gelir 500 − 100 iade = 400. B: 1 araç, 0 kira, 0 gelir.
        // Tohumlama APP tarafından tenant-scope'lu (FORCE-RLS: owner bile GUC'suz yazamaz — kanıtlandı).
        using var host = new TestHost(fx.AppConnectionString);
        using (var scopeA = host.ScopeFor(idA))
        {
            var factory = scopeA.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            db.Vehicles.AddRange(
                new Vehicle { Plaka = "34AAA111", Durum = VehicleStatus.Kirada },
                new Vehicle { Plaka = "34AAA222", Durum = VehicleStatus.Musait });
            db.Rentals.Add(new RentalContract
            {
                SozlesmeNo = "KS-PLT1", MusteriId = Guid.NewGuid(), VehicleId = Guid.NewGuid(),
                BasTar = DateTimeOffset.UtcNow.AddDays(-2), BitTar = DateTimeOffset.UtcNow.AddDays(1),
                Durum = RentalStatus.Kirada
            });
            db.AccountLedgerEntries.AddRange(
                new AccountLedgerEntry
                {
                    AccountType = LedgerAccountType.Gelir, Direction = LedgerDirection.Credit,
                    Amount = new Money(500m, "TRY", 1m), EntryDateUtc = DateTimeOffset.UtcNow.AddDays(-3),
                    SourceType = "Fatura", SourceId = Guid.NewGuid()
                },
                new AccountLedgerEntry // iade — Gelir Debit, netlenmeli
                {
                    AccountType = LedgerAccountType.Gelir, Direction = LedgerDirection.Debit,
                    Amount = new Money(100m, "TRY", 1m), EntryDateUtc = DateTimeOffset.UtcNow.AddDays(-1),
                    SourceType = "Fatura", SourceId = Guid.NewGuid()
                });
            await db.SaveChangesAsync();
        }
        using (var scopeB = host.ScopeFor(idB))
        {
            var factory = scopeB.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            db.Vehicles.Add(new Vehicle { Plaka = "34BBB111", Durum = VehicleStatus.Musait });
            await db.SaveChangesAsync();
        }

        var dA = await svc.GetTenantAsync(idA);
        Assert.Equal(2, dA!.AracSayisi);
        Assert.Equal(1, dA.AktifKira);
        Assert.Equal(1, dA.ToplamKira);
        Assert.Equal(400m, dA.Gelir30Gun);   // 500 − 100 (elle)

        var dB = await svc.GetTenantAsync(idB);
        Assert.Equal(1, dB!.AracSayisi);     // A'nın verisi B'ye SIZMAZ
        Assert.Equal(0, dB.AktifKira);
        Assert.Equal(0m, dB.Gelir30Gun);
    }

    // ---- 6. Başarılı login LastLoginAtUtc yazar; başarısız yazmaz ----
    [Fact]
    public async Task Basarili_login_son_girisi_yazar_basarisiz_yazmaz()
    {
        var (svc, login) = Build();
        var code = UniqCode();
        await svc.CreateTenantAsync(code, "Giriş Testi", "admin", "sifre123", "op");
        var id = (await svc.ListTenantsAsync()).First(t => t.Code == code).Id;

        Assert.Null((await svc.GetTenantAsync(id))!.SonGiris);              // başlangıçta boş

        await login.ValidateAsync(code, "admin", "YANLIS");                 // başarısız → yazmaz
        Assert.Null((await svc.GetTenantAsync(id))!.SonGiris);

        var once = DateTimeOffset.UtcNow.AddSeconds(-2);
        Assert.NotNull(await login.ValidateAsync(code, "admin", "sifre123"));
        var son = (await svc.GetTenantAsync(id))!.SonGiris;
        Assert.NotNull(son);                                                // başarılı → yazdı
        Assert.True(son > once && son <= DateTimeOffset.UtcNow.AddSeconds(2));
    }

    // ---- 7. Close, aktif-durum cache'ini invalidate eder (anlık kesme) ----
    [Fact]
    public async Task Close_status_cache_invalidate_eder()
    {
        var appOptions = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.AppConnectionString).Options;
        var factory = new ScopedAppDbContextFactory(appOptions, NullTenantContext.Instance, NullCurrentUser.Instance);
        var cache = new TenantStatusCache(new MemoryCache(new MemoryCacheOptions()), factory);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:Migrator"] = fx.OwnerConnectionString }).Build();
        var svc = new PlatformAdminService(config, new AspNetPasswordHasher(), cache,
            NullLogger<PlatformAdminService>.Instance);

        var code = UniqCode();
        await svc.CreateTenantAsync(code, "Cache Testi", "admin", "sifre123", "op");
        var id = (await svc.ListTenantsAsync()).First(t => t.Code == code).Id;

        Assert.True(await cache.IsActiveAsync(id));   // cache'e AKTİF yazıldı (60sn TTL)
        await svc.CloseAsync(id, "op");               // invalidate etmezse bayat true dönerdi
        Assert.False(await cache.IsActiveAsync(id));  // anlık kesme kanıtı
    }

    // ---- 8. Konsol satırı metrikleri (liste yüzeyi) ----
    [Fact]
    public async Task Liste_satiri_metrik_ve_durum_tasir()
    {
        var (svc, login) = Build();
        var code = UniqCode();
        await svc.CreateTenantAsync(code, "Satır Testi", "admin", "sifre123", "op");
        await login.ValidateAsync(code, "admin", "sifre123"); // son-giriş yaz

        var row = (await svc.ListTenantsAsync()).First(t => t.Code == code);
        Assert.Equal(1, row.UserCount);
        Assert.Equal(0, row.AracSayisi);
        Assert.Equal(0, row.AktifKira);
        Assert.NotNull(row.SonGiris);
        Assert.Equal("Aktif", row.Durum);
    }
}
