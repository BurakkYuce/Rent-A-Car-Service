using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.DamageFiles;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

[Collection("postgres")]
public sealed class DamageFileTests(PostgresFixture fx)
{
    [Fact]
    public async Task Create_allocates_gapless_no_and_opens_file()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<DamageFileService>();

        var id = await svc.CreateAsync(new DamageFileInput { VehicleId = Guid.NewGuid(), TahminiTutar = 1500m });
        var f = await svc.GetAsync(id);
        BelgeNoOracle.BeklenenlerdenBiri(11, 1, f!.No);   // 11 = HasarDosyasi
        Assert.Equal(DamageStatus.Acik, f.Durum);

        var id2 = await svc.CreateAsync(new DamageFileInput { VehicleId = Guid.NewGuid() });
        BelgeNoOracle.BeklenenlerdenBiri(11, 2, (await svc.GetAsync(id2))!.No);
    }

    [Fact]
    public async Task Approval_workflow_transitions_in_order()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<DamageFileService>();
        var id = await svc.CreateAsync(new DamageFileInput { VehicleId = Guid.NewGuid() });

        Assert.True(await svc.SendForApprovalAsync(id));
        Assert.Equal(DamageStatus.Onayda, (await svc.GetAsync(id))!.Durum);

        Assert.True(await svc.ApproveAsync(id, "uygun"));
        var approved = await svc.GetAsync(id);
        Assert.Equal(DamageStatus.Onaylandi, approved!.Durum);
        Assert.Equal("uygun", approved.OnayNotu);

        Assert.True(await svc.CloseAsync(id));
        Assert.Equal(DamageStatus.Kapali, (await svc.GetAsync(id))!.Durum);
    }

    [Fact]
    public async Task Invalid_transitions_are_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<DamageFileService>();
        var id = await svc.CreateAsync(new DamageFileInput { VehicleId = Guid.NewGuid() });

        // Açık'tan doğrudan onaylanamaz (önce Onayda olmalı).
        await Assert.ThrowsAsync<ValidationException>(() => svc.ApproveAsync(id));
        // Açık'tan doğrudan kapatılamaz.
        await Assert.ThrowsAsync<ValidationException>(() => svc.CloseAsync(id));

        await svc.SendForApprovalAsync(id);
        await svc.RejectAsync(id, "yetersiz");
        Assert.Equal(DamageStatus.Reddedildi, (await svc.GetAsync(id))!.Durum);
        // Reddedilmiş tekrar onaya gönderilemez.
        await Assert.ThrowsAsync<ValidationException>(() => svc.SendForApprovalAsync(id));
    }

    [Fact]
    public async Task DamageFile_is_tenant_isolated_and_audited()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1, Guid.NewGuid(), "eksper"))
            await s1.ServiceProvider.GetRequiredService<DamageFileService>()
                .CreateAsync(new DamageFileInput { VehicleId = Guid.NewGuid() });

        using (var s2 = host.ScopeFor(t2))
            Assert.Empty(await s2.ServiceProvider.GetRequiredService<DamageFileService>().ListAsync());

        using var s1b = host.ScopeFor(t1);
        var factory = s1b.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var audit = Assert.Single(await db.AuditLogs.Where(a => a.EntityName == "DamageFiles").ToListAsync());
        Assert.Equal("eksper", audit.UserName);
    }
}
