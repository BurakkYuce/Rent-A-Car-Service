using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.RentalRules;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Kiralama kuralı (promosyon/şart) master — bağımsız oracle. CRUD + kod normalize/benzersizlik +
/// gün/iskonto/tarih doğrulaması + aktif filtre + yetki + tenant izolasyon (racar_app).
/// Beklenen değerler senaryodan, koddan değil.
/// </summary>
[Collection("postgres")]
public sealed class RentalRuleTests(PostgresFixture fx)
{
    [Fact]
    public async Task Create_normalizes_and_roundtrips()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RentalRuleService>();

        var bas = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var bit = new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero);
        var id = await svc.CreateAsync(new RentalRuleInput
        {
            Kod = "yaz-kamp", Ad = "Yaz Kampanyası", Kanal = "WEB", Sube = "Merkez", AracGrupKod = "eko",
            MinGun = 3, MaxGun = 30, Iskonto = 12.50m, SonraOdeOran = 40.00m, HediyeGun = 1,
            KampanyaMi = true, KampanyaKodu = "YAZ2026", GecerlilikBas = bas, GecerlilikBit = bit,
            SartMetni = "Min 3 gün, iade yok"
        });

        var r = await svc.GetAsync(id);
        Assert.NotNull(r);
        Assert.Equal("YAZ-KAMP", r!.Kod);   // kod normalize
        Assert.Equal("EKO", r.AracGrupKod);  // grup kodu normalize
        Assert.Equal(3, r.MinGun);
        Assert.Equal(30, r.MaxGun);
        Assert.Equal(12.50m, r.Iskonto);
        Assert.Equal(40.00m, r.SonraOdeOran);
        Assert.Equal(1, r.HediyeGun);
        Assert.True(r.KampanyaMi);
        Assert.Equal("YAZ2026", r.KampanyaKodu);
        Assert.Equal(bas, r.GecerlilikBas);
        Assert.Equal("Min 3 gün, iade yok", r.SartMetni);
        Assert.True(r.Aktif);
    }

    [Fact]
    public async Task Duplicate_kod_rejected_case_insensitive()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RentalRuleService>();

        await svc.CreateAsync(new RentalRuleInput { Kod = "K1", Ad = "Kural 1" });
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new RentalRuleInput { Kod = "k1", Ad = "Başka" }));
    }

    [Fact]
    public async Task Validation_rejects_bad_inputs()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RentalRuleService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new RentalRuleInput { Kod = "", Ad = "A" }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new RentalRuleInput { Kod = "X", Ad = "  " }));
        // Max < Min
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new RentalRuleInput { Kod = "MM", Ad = "MM", MinGun = 10, MaxGun = 5 }));
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new RentalRuleInput { Kod = "ISK", Ad = "Isk", Iskonto = 150m }));
        // Geçerlilik bitiş < başlangıç
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new RentalRuleInput
            {
                Kod = "GEC", Ad = "Gec",
                GecerlilikBas = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
                GecerlilikBit = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero)
            }));
    }

    [Fact]
    public async Task ListActive_excludes_passive_but_list_keeps_all()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RentalRuleService>();

        var a = await svc.CreateAsync(new RentalRuleInput { Kod = "A", Ad = "A Kural" });
        await svc.CreateAsync(new RentalRuleInput { Kod = "B", Ad = "B Kural" });
        await svc.UpdateAsync(a, new RentalRuleInput { Kod = "A", Ad = "A Kural", Aktif = false });

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
        var svc = scope.ServiceProvider.GetRequiredService<RentalRuleService>();
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new RentalRuleInput { Kod = "X", Ad = "Yetkisiz" }));
    }

    [Fact]
    public async Task RentalRules_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<RentalRuleService>()
                .CreateAsync(new RentalRuleInput { Kod = "T1", Ad = "Tenant1" });

        using var s2 = host.ScopeFor(t2);
        var svc2 = s2.ServiceProvider.GetRequiredService<RentalRuleService>();
        Assert.Empty(await svc2.ListAsync());
        await svc2.CreateAsync(new RentalRuleInput { Kod = "T1", Ad = "Tenant2" });
        Assert.Single(await svc2.ListAsync());
    }
}
