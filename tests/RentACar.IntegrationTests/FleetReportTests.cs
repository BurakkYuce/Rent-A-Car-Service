using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Reporting;
using RentACar.Application.ServiceRecords;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

[Collection("postgres")]
public sealed class FleetReportTests(PostgresFixture fx)
{
    private static async Task SeedVehiclesAsync(IServiceScope scope, params VehicleStatus[] durumlar)
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var i = 0;
        foreach (var d in durumlar)
            db.Vehicles.Add(new Vehicle { Plaka = $"34FL{i++:D3}", Durum = d });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> SeedVehicleAsync(IServiceScope scope, string plaka)
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var v = new Vehicle { Plaka = plaka, Durum = VehicleStatus.Musait };
        db.Vehicles.Add(v);
        await db.SaveChangesAsync();
        return v.Id;
    }

    [Fact]
    public async Task Fleet_utilization_counts_by_status()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedVehiclesAsync(scope,
            VehicleStatus.Musait, VehicleStatus.Musait, VehicleStatus.Kirada,
            VehicleStatus.Serviste, VehicleStatus.Satildi);
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();

        var f = await reports.GetFleetUtilizationAsync();
        Assert.Equal(5, f.Toplam);
        Assert.Equal(2, f.Musait);
        Assert.Equal(1, f.Kirada);
        Assert.Equal(1, f.Serviste);
        Assert.Equal(1, f.Satildi);
        Assert.Equal(0, f.Pasif);
    }

    [Fact]
    public async Task Service_cost_summary_groups_completed_only()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<ServiceRecordService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var vid = await SeedVehicleAsync(scope, "34SC01");

        // Tamamlanmış servis: 800 + 200 = 1000 işçilik.
        var done = await svc.CreateAsync(new ServiceRecordInput
        {
            VehicleId = vid, Tip = ServisTipi.Periyodik, GirisKm = 1000,
            Lines = [new ServiceLineInput { Aciklama = "Yağ", Tutar = 800m }, new ServiceLineInput { Aciklama = "Filtre", Tutar = 200m }]
        });
        await svc.BaslatAsync(done);
        await svc.TamamlaAsync(done, cikisKm: 1010);

        // Açık (tamamlanmamış) servis → özete GİRMEMELİ.
        var acik = await svc.CreateAsync(new ServiceRecordInput
        { VehicleId = vid, Tip = ServisTipi.Ariza, GirisKm = 1010, Lines = [new ServiceLineInput { Aciklama = "X", Tutar = 5000m }] });
        await svc.BaslatAsync(acik);

        var summary = await reports.GetServiceCostSummaryAsync();
        var row = Assert.Single(summary);
        Assert.Equal("34SC01", row.Plaka);
        Assert.Equal(ServisTipi.Periyodik, row.Tip);
        Assert.Equal(1000m, row.Toplam);
        Assert.Equal(1, row.Adet);
    }

    [Fact]
    public async Task Fleet_and_service_reports_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await SeedVehiclesAsync(s1, VehicleStatus.Musait, VehicleStatus.Kirada);

        using var s2 = host.ScopeFor(t2);
        var reports = s2.ServiceProvider.GetRequiredService<ReportService>();
        var f = await reports.GetFleetUtilizationAsync();
        Assert.Equal(0, f.Toplam);
        Assert.Empty(await reports.GetServiceCostSummaryAsync());
    }
}
