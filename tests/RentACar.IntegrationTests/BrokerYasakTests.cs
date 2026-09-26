using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.BrokerYasaklari;
using RentACar.Application.Common;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Broker/kaynak satış yasağı master — bağımsız oracle. CRUD + kod/grup normalize + benzersizlik +
/// kısıt doğrulaması (en az bir kısıt; min gün negatif; tarih sırası) + aktif filtre + yetki +
/// tenant izolasyon (racar_app). Beklenen değerler senaryodan, koddan değil.
/// </summary>
[Collection("postgres")]
public sealed class BrokerYasakTests(PostgresFixture fx)
{
    [Fact]
    public async Task Create_normalizes_and_roundtrips()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BrokerBanService>();

        var start = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var bit = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
        var id = await svc.CreateAsync(new BrokerYasakInput
        {
            Kod = "brk-x-min3", Ad = "Broker X — min 3 gün", Aciklama = "Kısa kiralama yasak",
            Kaynak = "BrokerX", AracGrupKod = "eko", Bolge = "Antalya",
            MinGun = 3, TumSatisKapali = false, GecerlilikBas = start, GecerlilikBit = bit
        });

        var r = await svc.GetAsync(id);
        Assert.NotNull(r);
        Assert.Equal("BRK-X-MIN3", r!.Kod);   // kod normalize (upper)
        Assert.Equal("EKO", r.AracGrupKod);     // grup kodu normalize (upper)
        Assert.Equal("BrokerX", r.Kaynak);      // kaynak trim-only (upper DEĞİL)
        Assert.Equal("Antalya", r.Bolge);
        Assert.Equal(3, r.MinGun);
        Assert.False(r.TumSatisKapali);
        Assert.Equal(start, r.GecerlilikBas);
        Assert.True(r.Aktif);
    }

    [Fact]
    public async Task Duplicate_kod_rejected_case_insensitive()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BrokerBanService>();

        await svc.CreateAsync(new BrokerYasakInput { Kod = "Y1", Ad = "Yasak 1", TumSatisKapali = true });
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new BrokerYasakInput { Kod = "y1", Ad = "Başka", TumSatisKapali = true }));
    }

    [Fact]
    public async Task At_least_one_restriction_required()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BrokerBanService>();

        // Ne MinGun ne TumSatisKapali → kural anlamsız, reddedilir.
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new BrokerYasakInput { Kod = "BOS", Ad = "Kısıtsız", MinGun = null, TumSatisKapali = false }));
        // Yalnız TumSatisKapali yeterli.
        var id = await svc.CreateAsync(new BrokerYasakInput { Kod = "KAPALI", Ad = "Tümü kapalı", TumSatisKapali = true });
        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task Invalid_restriction_and_date_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BrokerBanService>();

        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new BrokerYasakInput { Kod = "NEG", Ad = "Negatif", MinGun = -1 }));
        // L1: MinGun=0 "0 gün altı yasak" no-op → reddedilir (kısıt olarak sayılmaz).
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new BrokerYasakInput { Kod = "SIFIR", Ad = "Sıfır gün", MinGun = 0 }));
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new BrokerYasakInput
            {
                Kod = "TARIH", Ad = "Ters tarih", TumSatisKapali = true,
                GecerlilikBas = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
                GecerlilikBit = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)
            }));
    }

    [Fact]
    public async Task ListActive_excludes_passive_but_list_keeps_all()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BrokerBanService>();

        var a = await svc.CreateAsync(new BrokerYasakInput { Kod = "A", Ad = "A", TumSatisKapali = true });
        await svc.CreateAsync(new BrokerYasakInput { Kod = "B", Ad = "B", TumSatisKapali = true });
        await svc.UpdateAsync(a, new BrokerYasakInput { Kod = "A", Ad = "A", TumSatisKapali = true, Aktif = false });

        var active = await svc.ListActiveAsync();
        Assert.Single(active);
        Assert.Equal("B", active[0].Kod);
        Assert.Equal(2, (await svc.ListAsync()).Count);
    }

    [Fact]
    public async Task NonOperations_user_cannot_manage()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "muh", UserRole.Muhasebe);
        var svc = scope.ServiceProvider.GetRequiredService<BrokerBanService>();
        await Assert.ThrowsAsync<NoPermissionException>(
            () => svc.CreateAsync(new BrokerYasakInput { Kod = "X", Ad = "Yetkisiz", TumSatisKapali = true }));
    }

    [Fact]
    public async Task BrokerYasaklari_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<BrokerBanService>()
                .CreateAsync(new BrokerYasakInput { Kod = "T1", Ad = "Tenant1", TumSatisKapali = true });

        using var s2 = host.ScopeFor(t2);
        var svc2 = s2.ServiceProvider.GetRequiredService<BrokerBanService>();
        Assert.Empty(await svc2.ListAsync());
        await svc2.CreateAsync(new BrokerYasakInput { Kod = "T1", Ad = "Tenant2", TumSatisKapali = true });
        Assert.Single(await svc2.ListAsync());
    }
}
