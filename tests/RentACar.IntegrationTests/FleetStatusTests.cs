using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Fleet;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Araç Güncel Durum birleşik grid — bağımsız oracle. Araç + aktif kira + müşteri birleşimi,
/// filtreler (FiloDurum/Grup/arama/KiradaMi) ve tenant izolasyonu.
/// </summary>
[Collection("postgres")]
public sealed class FleetStatusTests(PostgresFixture fx)
{
    private static async Task SeedAsync(IServiceScope scope)
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();

        var musteri = new Customer { Tip = CariType.Bireysel, Ad = "Ali", Soyad = "Veli" };
        db.Customers.Add(musteri);

        // Araç A: kirada (Havuz, EKO) → aktif kira müşteri Ali Veli, bakiye 150.
        var a = new Vehicle { Plaka = "34AAA01", Marka = "Fiat", Grup = "EKO",
            Durum = VehicleStatus.Kirada, FiloDurum = FiloStatus.Havuz, Km = 1000 };
        // Araç B: boşta (SifirKmStok, SUV) → aktif kira yok.
        var b = new Vehicle { Plaka = "34BBB02", Marka = "Renault", Grup = "SUV",
            Durum = VehicleStatus.Musait, FiloDurum = FiloStatus.SifirKmStok, Km = 5 };
        db.Vehicles.Add(a);
        db.Vehicles.Add(b);

        db.Rentals.Add(new RentalContract
        {
            SozlesmeNo = "K-1", VehicleId = a.Id, MusteriId = musteri.Id, Durum = RentalStatus.Kirada,
            BasTar = DateTimeOffset.UtcNow.AddDays(-1), BitTar = DateTimeOffset.UtcNow.AddDays(3), Bakiye = 150m
        });
        // Tamamlanmış (eski) kira → AKTİF sayılmamalı.
        db.Rentals.Add(new RentalContract
        {
            SozlesmeNo = "K-0", VehicleId = b.Id, MusteriId = musteri.Id, Durum = RentalStatus.Tamamlandi,
            BasTar = DateTimeOffset.UtcNow.AddDays(-10), BitTar = DateTimeOffset.UtcNow.AddDays(-8)
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Joins_active_rental_and_lists_all_vehicles()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedAsync(scope);
        var svc = scope.ServiceProvider.GetRequiredService<FleetStatusService>();

        var rows = await svc.QueryAsync(new FleetStatusFilter());
        Assert.Equal(2, rows.Count);

        var a = rows.Single(r => r.Plaka == "34AAA01");
        Assert.True(a.Kirada);
        Assert.Equal("Ali Veli", a.MusteriAd);
        Assert.Equal(150m, a.KiraBakiye);
        Assert.Equal("K-1", a.KiraSozlesmeNo);

        var b = rows.Single(r => r.Plaka == "34BBB02");
        Assert.False(b.Kirada);            // tamamlanmış kira aktif değil
        Assert.Null(b.MusteriAd);
    }

    [Fact]
    public async Task Filters_by_filo_status_grup_and_query()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedAsync(scope);
        var svc = scope.ServiceProvider.GetRequiredService<FleetStatusService>();

        var havuz = await svc.QueryAsync(new FleetStatusFilter { FiloDurum = FiloStatus.Havuz });
        Assert.Single(havuz);
        Assert.Equal("34AAA01", havuz[0].Plaka);

        var suv = await svc.QueryAsync(new FleetStatusFilter { Grup = "SUV" });
        Assert.Single(suv);
        Assert.Equal("34BBB02", suv[0].Plaka);

        var ara = await svc.QueryAsync(new FleetStatusFilter { Query = "BBB" });
        Assert.Single(ara);
        Assert.Equal("34BBB02", ara[0].Plaka);
    }

    [Fact]
    public async Task KiradaMi_filter_splits_rented_and_idle()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedAsync(scope);
        var svc = scope.ServiceProvider.GetRequiredService<FleetStatusService>();

        var kirada = await svc.QueryAsync(new FleetStatusFilter { KiradaMi = true });
        Assert.Single(kirada);
        Assert.Equal("34AAA01", kirada[0].Plaka);

        var bosta = await svc.QueryAsync(new FleetStatusFilter { KiradaMi = false });
        Assert.Single(bosta);
        Assert.Equal("34BBB02", bosta[0].Plaka);
    }

    [Fact]
    public async Task Tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
            await SeedAsync(s1);

        using var s2 = host.ScopeFor(Guid.NewGuid());
        var rows = await s2.ServiceProvider.GetRequiredService<FleetStatusService>()
            .QueryAsync(new FleetStatusFilter());
        Assert.Empty(rows);
    }
}
