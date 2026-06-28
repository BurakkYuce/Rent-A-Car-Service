using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.RateMatrices;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Tarife Matrisi (XML Tarife) master — bağımsız oracle. CRUD + kod normalize/benzersizlik +
/// gün-fiyat/esneklik/tarih doğrulaması + aktif filtre + yetki + tenant izolasyon (racar_app).
/// Beklenen değerler senaryodan, koddan değil.
/// </summary>
[Collection("postgres")]
public sealed class RateMatrixTests(PostgresFixture fx)
{
    [Fact]
    public async Task Create_normalizes_and_roundtrips_full_matrix()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RateMatrixService>();

        var bas = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var bit = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);
        var id = await svc.CreateAsync(new RateMatrixInput
        {
            Kod = "web-eko-2026", Ad = "Web Ekonomik 2026",
            Kanal = "WEB", Sube = "Merkez", Lokasyon = "IST-AHL", AracGrupKod = "eko", ParaBirimi = "try",
            BasTar = bas, BitTar = bit,
            Gun1 = 1000.00m, Gun2 = 950.00m, Gun3 = 900.00m, Gun4 = 875.00m,
            Gun5 = 850.00m, Gun6 = 825.00m, Gun7 = 800.00m,
            MaxEsneklik = 15.00m, OnayDurumu = TarifeOnayDurumu.Onayli, Onaylayan = "umit"
        });

        var r = await svc.GetAsync(id);
        Assert.NotNull(r);
        Assert.Equal("WEB-EKO-2026", r!.Kod);     // kod büyük harfe normalize
        Assert.Equal("EKO", r.AracGrupKod);        // grup kodu büyük harfe normalize
        Assert.Equal("TRY", r.ParaBirimi);         // para birimi büyük harfe normalize
        Assert.Equal("WEB", r.Kanal);
        Assert.Equal(bas, r.BasTar);
        Assert.Equal(bit, r.BitTar);
        Assert.Equal(1000.00m, r.Gun1);
        Assert.Equal(800.00m, r.Gun7);
        Assert.Equal(15.00m, r.MaxEsneklik);
        Assert.Equal(TarifeOnayDurumu.Onayli, r.OnayDurumu);
        Assert.Equal("umit", r.Onaylayan);
        Assert.True(r.Aktif);
    }

    [Fact]
    public async Task Duplicate_kod_rejected_case_insensitive()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RateMatrixService>();

        await svc.CreateAsync(new RateMatrixInput { Kod = "T1", Ad = "Tarife 1" });
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new RateMatrixInput { Kod = "t1", Ad = "Başka" }));
    }

    [Fact]
    public async Task Validation_rejects_bad_inputs()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RateMatrixService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new RateMatrixInput { Kod = "", Ad = "A" }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new RateMatrixInput { Kod = "X", Ad = "  " }));
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new RateMatrixInput { Kod = "NEG", Ad = "Neg", Gun1 = -1m }));
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new RateMatrixInput { Kod = "ESN", Ad = "Esn", MaxEsneklik = 150m }));
        // Bitiş < Başlangıç
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new RateMatrixInput
            {
                Kod = "TAR", Ad = "Tar",
                BasTar = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
                BitTar = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero)
            }));
    }

    [Fact]
    public async Task ListActive_excludes_passive_but_list_keeps_all()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RateMatrixService>();

        var a = await svc.CreateAsync(new RateMatrixInput { Kod = "A", Ad = "A Tarife" });
        await svc.CreateAsync(new RateMatrixInput { Kod = "B", Ad = "B Tarife" });
        await svc.UpdateAsync(a, new RateMatrixInput { Kod = "A", Ad = "A Tarife", Aktif = false });

        var active = await svc.ListActiveAsync();
        Assert.Single(active);
        Assert.Equal("B", active[0].Kod);
        Assert.Equal(2, (await svc.ListAsync()).Count);
    }

    [Fact]
    public async Task Delete_removes_row()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RateMatrixService>();

        var id = await svc.CreateAsync(new RateMatrixInput { Kod = "DEL", Ad = "Silinecek" });
        Assert.True(await svc.DeleteAsync(id));
        Assert.Null(await svc.GetAsync(id));
    }

    [Fact]
    public async Task NonOperations_user_cannot_manage()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "muh", UserRole.Muhasebe);
        var svc = scope.ServiceProvider.GetRequiredService<RateMatrixService>();
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new RateMatrixInput { Kod = "X", Ad = "Yetkisiz" }));
    }

    [Fact]
    public async Task RateMatrices_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<RateMatrixService>()
                .CreateAsync(new RateMatrixInput { Kod = "T1", Ad = "Tenant1" });

        using var s2 = host.ScopeFor(t2);
        var svc2 = s2.ServiceProvider.GetRequiredService<RateMatrixService>();
        Assert.Empty(await svc2.ListAsync());
        // Aynı kod farklı tenant'ta serbest.
        await svc2.CreateAsync(new RateMatrixInput { Kod = "T1", Ad = "Tenant2" });
        Assert.Single(await svc2.ListAsync());
    }
}
