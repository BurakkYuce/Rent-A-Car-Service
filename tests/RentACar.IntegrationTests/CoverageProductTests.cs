using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.CoverageProducts;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Sigorta & ek hizmet ürün kataloğu master — bağımsız oracle. CRUD + kod normalize/benzersizlik +
/// ücret/KDV/maxgün doğrulaması + aktif filtre + yetki + tenant izolasyon (racar_app).
/// Beklenen değerler senaryodan, koddan değil.
/// </summary>
[Collection("postgres")]
public sealed class CoverageProductTests(PostgresFixture fx)
{
    [Fact]
    public async Task Create_normalizes_and_roundtrips()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CoverageProductService>();

        var id = await svc.CreateAsync(new CoverageProductInput
        {
            Kod = "scdw", Ad = "Süper Hasar Muafiyeti", AdEn = "Super Damage Waiver",
            Tur = CoverageProductType.Scdw, GunlukUcret = 120.00m, KdvOrani = 20.00m,
            MaxGun = 30, Doviz = "try", Zorunlu = true, Aciklama = "Muafiyetsiz hasar"
        });

        var r = await svc.GetAsync(id);
        Assert.NotNull(r);
        Assert.Equal("SCDW", r!.Kod);          // kod büyük harfe normalize
        Assert.Equal("TRY", r.Doviz);          // döviz büyük harfe normalize
        Assert.Equal("Super Damage Waiver", r.AdEn);
        Assert.Equal(CoverageProductType.Scdw, r.Tur);
        Assert.Equal(120.00m, r.GunlukUcret);
        Assert.Equal(20.00m, r.KdvOrani);
        Assert.Equal(30, r.MaxGun);
        Assert.True(r.Zorunlu);
        Assert.True(r.Aktif);
    }

    [Fact]
    public async Task Duplicate_kod_rejected_case_insensitive()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CoverageProductService>();

        await svc.CreateAsync(new CoverageProductInput { Kod = "IMM", Ad = "İhtiyari Mali Mesuliyet", Tur = CoverageProductType.Imm });
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new CoverageProductInput { Kod = "imm", Ad = "Başka" }));
    }

    [Fact]
    public async Task Validation_rejects_bad_inputs()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CoverageProductService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new CoverageProductInput { Kod = "", Ad = "A" }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new CoverageProductInput { Kod = "X", Ad = "  " }));
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new CoverageProductInput { Kod = "NEG", Ad = "Neg", GunlukUcret = -1m }));
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new CoverageProductInput { Kod = "KDV", Ad = "Kdv", KdvOrani = 150m }));
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new CoverageProductInput { Kod = "MG", Ad = "MaxGun", MaxGun = -5 }));
    }

    [Fact]
    public async Task ListActive_excludes_passive_but_list_keeps_all()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<CoverageProductService>();

        var a = await svc.CreateAsync(new CoverageProductInput { Kod = "A", Ad = "A Ürün" });
        await svc.CreateAsync(new CoverageProductInput { Kod = "B", Ad = "B Ürün" });
        await svc.UpdateAsync(a, new CoverageProductInput { Kod = "A", Ad = "A Ürün", Aktif = false });

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
        var svc = scope.ServiceProvider.GetRequiredService<CoverageProductService>();
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new CoverageProductInput { Kod = "X", Ad = "Yetkisiz" }));
    }

    [Fact]
    public async Task CoverageProducts_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<CoverageProductService>()
                .CreateAsync(new CoverageProductInput { Kod = "T1", Ad = "Tenant1" });

        using var s2 = host.ScopeFor(t2);
        var svc2 = s2.ServiceProvider.GetRequiredService<CoverageProductService>();
        Assert.Empty(await svc2.ListAsync());
        await svc2.CreateAsync(new CoverageProductInput { Kod = "T1", Ad = "Tenant2" });
        Assert.Single(await svc2.ListAsync());
    }
}
