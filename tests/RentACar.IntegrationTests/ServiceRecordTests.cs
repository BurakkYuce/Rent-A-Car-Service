using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.ServiceRecords;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

[Collection("postgres")]
public sealed class ServiceRecordTests(PostgresFixture fx)
{
    private static async Task<Guid> SeedVehicleAsync(IServiceScope scope, string plaka = "34SRV34", VehicleStatus durum = VehicleStatus.Musait)
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var v = new Vehicle { Plaka = plaka, Durum = durum };
        db.Vehicles.Add(v);
        await db.SaveChangesAsync();
        return v.Id;
    }

    private static async Task<VehicleStatus> VehicleStatusAsync(IServiceScope scope, Guid vehicleId)
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        return (await db.Vehicles.AsNoTracking().FirstAsync(v => v.Id == vehicleId)).Durum;
    }

    [Fact]
    public async Task Create_allocates_gapless_no_and_sums_lines()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<ServiceRecordService>();
        var vid = await SeedVehicleAsync(scope);

        var id = await svc.CreateAsync(new ServiceRecordInput
        {
            VehicleId = vid, Tip = ServisTipi.Periyodik, GirisKm = 50000,
            Lines = [new ServiceLineInput { Aciklama = "Yağ", Tutar = 800m }, new ServiceLineInput { Aciklama = "Filtre", Tutar = 200m }]
        });

        var rec = await svc.GetAsync(id);
        Assert.Equal("SRV-000001", rec!.No);
        Assert.Equal(ServisDurum.Acik, rec.Durum);
        Assert.Equal(1000m, rec.ToplamIscilik);
        Assert.Equal(2, rec.Lines.Count);
        // Kayıt açılınca araç henüz Serviste DEĞİL (Açık = bekliyor).
        Assert.Equal(VehicleStatus.Musait, await VehicleStatusAsync(scope, vid));
    }

    [Fact]
    public async Task Full_flow_couples_vehicle_status_and_records_km()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<ServiceRecordService>();
        var vid = await SeedVehicleAsync(scope);

        var id = await svc.CreateAsync(new ServiceRecordInput { VehicleId = vid, GirisKm = 50000 });

        Assert.True(await svc.BaslatAsync(id));
        Assert.Equal(VehicleStatus.Serviste, await VehicleStatusAsync(scope, vid));

        Assert.True(await svc.KalemEkleAsync(id, "İşçilik", 1500m));
        Assert.Equal(1500m, (await svc.GetAsync(id))!.ToplamIscilik);

        Assert.True(await svc.TamamlaAsync(id, cikisKm: 50050, sonrakiBakimKm: 60000));
        var done = await svc.GetAsync(id);
        Assert.Equal(ServisDurum.Tamamlandi, done!.Durum);
        Assert.Equal(50050, done.CikisKm);
        Assert.Equal(60000, done.SonrakiBakimKm);
        Assert.NotNull(done.CikisTarihi);
        // Tamamlanınca araç Musait'e döner.
        Assert.Equal(VehicleStatus.Musait, await VehicleStatusAsync(scope, vid));
    }

    [Fact]
    public async Task Invalid_transitions_and_km_are_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<ServiceRecordService>();
        var vid = await SeedVehicleAsync(scope);
        var id = await svc.CreateAsync(new ServiceRecordInput { VehicleId = vid, GirisKm = 50000 });

        // Açık'tan doğrudan tamamlanamaz.
        await Assert.ThrowsAsync<ValidationException>(() => svc.TamamlaAsync(id, 50100));

        await svc.BaslatAsync(id);
        // Çıkış KM < giriş KM reddedilir.
        await Assert.ThrowsAsync<ValidationException>(() => svc.TamamlaAsync(id, 49000));
        // İki kez başlatılamaz.
        await Assert.ThrowsAsync<ValidationException>(() => svc.BaslatAsync(id));

        await svc.TamamlaAsync(id, 50100);
        // Kapanmış servise kalem eklenemez.
        await Assert.ThrowsAsync<ValidationException>(() => svc.KalemEkleAsync(id, "geç", 10m));
        // Kapanmış servis iptal edilemez.
        await Assert.ThrowsAsync<ValidationException>(() => svc.IptalAsync(id));
    }

    [Fact]
    public async Task Cancel_from_in_service_returns_vehicle_to_available()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<ServiceRecordService>();
        var vid = await SeedVehicleAsync(scope);
        var id = await svc.CreateAsync(new ServiceRecordInput { VehicleId = vid, GirisKm = 1000 });

        await svc.BaslatAsync(id);
        Assert.Equal(VehicleStatus.Serviste, await VehicleStatusAsync(scope, vid));
        Assert.True(await svc.IptalAsync(id));
        Assert.Equal(ServisDurum.Iptal, (await svc.GetAsync(id))!.Durum);
        Assert.Equal(VehicleStatus.Musait, await VehicleStatusAsync(scope, vid));
    }

    [Fact]
    public async Task Validation_rejects_bad_input()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<ServiceRecordService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new ServiceRecordInput { GirisKm = 10 })); // araç yok
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new ServiceRecordInput { VehicleId = Guid.NewGuid(), GirisKm = -1 }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new ServiceRecordInput { VehicleId = Guid.NewGuid(), KusurOrani = 1.5m }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new ServiceRecordInput
        { VehicleId = Guid.NewGuid(), Lines = [new ServiceLineInput { Aciklama = "x", Tutar = -5m }] }));
    }

    [Fact]
    public async Task ServiceRecord_is_tenant_isolated_and_audited()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1, Guid.NewGuid(), "usta"))
        {
            var vid = await SeedVehicleAsync(s1, "34T1SV");
            await s1.ServiceProvider.GetRequiredService<ServiceRecordService>()
                .CreateAsync(new ServiceRecordInput { VehicleId = vid, GirisKm = 100 });
        }

        using (var s2 = host.ScopeFor(t2))
            Assert.Empty(await s2.ServiceProvider.GetRequiredService<ServiceRecordService>().ListAsync());

        using var s1b = host.ScopeFor(t1);
        var factory = s1b.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var audit = Assert.Single(await db.AuditLogs.Where(a => a.EntityName == "ServiceRecords").ToListAsync());
        Assert.Equal("usta", audit.UserName);
    }
}
