using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Locations;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;
using static RentACar.IntegrationTests.SystemApiTestKit;

namespace RentACar.IntegrationTests;

public sealed partial class UiSystemSecurityTests
{
    // Doğrulama turu HIGH — Blazor /lokasyonlar/update|delete servis yolu şube kapsamı uygulamıyordu. Kural artık
    // LocationService'te: Blazor ve /api/ui aynı yoldan geçer.
    [Fact]
    public async Task Location_service_enforces_branch_scope_for_every_caller()
    {
        var e = await _kit.SetupAsync();
        var (branchA, branchB) = await BranchesAsync(e);
        var office = new Location { Kod = "OTB", Ad = "Otogar B", Sube = "SubeB", SubeId = branchB, Aktif = true };
        await _kit.WriteAsync(e.TenantId, db => db.Locations.Add(office));

        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(e.TenantId, e.UserIds[Who.OperatorA], "opA", UserRole.Operator, "SubeA", branchA);
        var svc = scope.ServiceProvider.GetRequiredService<LocationService>();

        var move = new LocationInput { Kod = "OTB", Ad = "Otogar B", Sube = "SubeA", Aktif = true };
        await Assert.ThrowsAsync<YetkiYokException>(() => svc.UpdateAsync(office.Id, move));
        await Assert.ThrowsAsync<YetkiYokException>(() => svc.UpdateAsync(office.Id, move, "1"));
        await Assert.ThrowsAsync<YetkiYokException>(() => svc.DeleteAsync(office.Id));
        await Assert.ThrowsAsync<YetkiYokException>(() => svc.CreateAsync(new LocationInput { Kod = "NEW", Ad = "Yeni B", Sube = "SubeB", Aktif = true }));

        var row = await _kit.ReadAsync(e.TenantId, db => db.Locations.AsNoTracking().SingleAsync(x => x.Id == office.Id));
        Assert.Equal("SubeB", row.Sube);
        Assert.Equal(branchB, row.SubeId);
        Assert.Equal(1, await _kit.ReadAsync(e.TenantId, db => db.Locations.AsNoTracking().CountAsync()));

        // Kendi şubesinde çalışır.
        var mine = await svc.CreateAsync(new LocationInput { Kod = "OTA", Ad = "Otogar A", Sube = "SubeA", Aktif = true });
        Assert.True(await svc.DeleteAsync(mine));
    }
}
