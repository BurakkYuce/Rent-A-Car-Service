using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Branches;
using RentACar.Application.Common;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Şube master modülü — bağımsız oracle. Beklenen değerler giriş senaryosundan türetilir
/// (servis kodundan değil). CRUD + kod benzersizliği + normalize + aktif filtre + yetki +
/// tenant izolasyon.
/// </summary>
[Collection("postgres")]
public sealed class BranchTests(PostgresFixture fx)
{
    [Fact]
    public async Task Create_and_list_roundtrip_with_kod_uppercased()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BranchService>();

        var id = await svc.CreateAsync(new BranchInput { Kod = "merkez", Ad = "Merkez Ofis", Telefon = "0212" });

        var got = await svc.GetAsync(id);
        Assert.NotNull(got);
        Assert.Equal("MERKEZ", got!.Kod);   // normalize: büyük harf
        Assert.Equal("Merkez Ofis", got.Ad);
        Assert.True(got.Aktif);

        var all = await svc.ListAsync();
        Assert.Single(all);
    }

    [Fact]
    public async Task Duplicate_kod_rejected_case_insensitive()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BranchService>();

        await svc.CreateAsync(new BranchInput { Kod = "ESB", Ad = "Esenyurt" });
        // "esb" normalize → "ESB" → çakışır.
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new BranchInput { Kod = "esb", Ad = "Başka" }));
    }

    [Fact]
    public async Task Update_changes_fields_and_active_filter_excludes_passive()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BranchService>();

        var a = await svc.CreateAsync(new BranchInput { Kod = "A", Ad = "A Şube" });
        await svc.CreateAsync(new BranchInput { Kod = "B", Ad = "B Şube" });

        // A'yı pasifleştir → ListActive yalnız B döner.
        await svc.UpdateAsync(a, new BranchInput { Kod = "A", Ad = "A Şube (güncel)", Aktif = false });

        var active = await svc.ListActiveAsync();
        Assert.Single(active);
        Assert.Equal("B", active[0].Kod);

        var got = await svc.GetAsync(a);
        Assert.Equal("A Şube (güncel)", got!.Ad);
        Assert.False(got.Aktif);

        // Tümü hâlâ 2 (pasif silinmedi, korunur).
        Assert.Equal(2, (await svc.ListAsync()).Count);
    }

    [Fact]
    public async Task Validation_requires_kod_and_ad()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BranchService>();

        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new BranchInput { Kod = "", Ad = "Ad var" }));
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new BranchInput { Kod = "X", Ad = "  " }));
    }

    [Fact]
    public async Task Delete_removes_branch()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<BranchService>();

        var id = await svc.CreateAsync(new BranchInput { Kod = "DEL", Ad = "Silinecek" });
        Assert.True(await svc.DeleteAsync(id));
        Assert.Null(await svc.GetAsync(id));
        Assert.Empty(await svc.ListAsync());
    }

    [Fact]
    public async Task NonAdmin_cannot_manage_branches()
    {
        using var host = new TestHost(fx.AppConnectionString);
        // Yönetici operasyon/finans yazabilir ama ManageUsers yok → şube yönetemez.
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "yon", UserRole.Yonetici);
        var svc = scope.ServiceProvider.GetRequiredService<BranchService>();
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new BranchInput { Kod = "X", Ad = "Yetkisiz" }));
    }

    [Fact]
    public async Task Branches_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<BranchService>()
                .CreateAsync(new BranchInput { Kod = "T1", Ad = "Tenant1" });

        using var s2 = host.ScopeFor(t2);
        var svc2 = s2.ServiceProvider.GetRequiredService<BranchService>();
        Assert.Empty(await svc2.ListAsync());
        // Aynı kod farklı tenant'ta serbest (izolasyon).
        await svc2.CreateAsync(new BranchInput { Kod = "T1", Ad = "Tenant2-aynı-kod" });
        Assert.Single(await svc2.ListAsync());
    }
}
