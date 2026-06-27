using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Details;
using RentACar.Application.Finance;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

[Collection("postgres")]
public sealed class DetailTests(PostgresFixture fx)
{
    [Fact]
    public async Task Vehicle_detail_aggregates_rentals_services_penalties_damages()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        Guid vid;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var v = new Vehicle { Plaka = "34DET01", Durum = VehicleStatus.Musait };
            db.Vehicles.Add(v);
            vid = v.Id;
            db.Rentals.Add(new RentalContract { SozlesmeNo = "K-D1", VehicleId = vid, Durum = RentalStatus.Kirada,
                BasTar = DateTimeOffset.UtcNow, BitTar = DateTimeOffset.UtcNow.AddDays(2) });
            db.ServiceRecords.Add(new ServiceRecord { No = "SRV-D1", VehicleId = vid, Tip = ServisTipi.Periyodik, GirisKm = 100 });
            db.Penalties.Add(new Penalty { No = "CZ-D1", CezaTuru = "Hız", VehicleId = vid, Tutar = 500m });
            db.DamageFiles.Add(new DamageFile { No = "BAF-D1", VehicleId = vid });
            // Başka araca ait kayıt → bu detayda GÖRÜNMEMELİ.
            db.Penalties.Add(new Penalty { No = "CZ-X", CezaTuru = "Park", VehicleId = Guid.NewGuid(), Tutar = 9m });
            await db.SaveChangesAsync();
        }

        var d = await scope.ServiceProvider.GetRequiredService<DetailService>().GetVehicleAsync(vid);
        Assert.NotNull(d);
        Assert.Equal("34DET01", d!.Vehicle.Plaka);
        Assert.Single(d.Rentals);
        Assert.Single(d.Services);
        Assert.Single(d.Penalties);
        Assert.Equal("CZ-D1", d.Penalties[0].No);
        Assert.Single(d.Damages);
    }

    [Fact]
    public async Task Customer_detail_has_balance_rentals_and_recent_ledger()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var cari = Guid.NewGuid();

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Customers.Add(new Customer { Id = cari, Tip = CariType.Bireysel, Ad = "Det", Soyad = "Ay" });
            db.Rentals.Add(new RentalContract { SozlesmeNo = "K-C1", MusteriId = cari, VehicleId = Guid.NewGuid(),
                Durum = RentalStatus.Kirada, BasTar = DateTimeOffset.UtcNow, BitTar = DateTimeOffset.UtcNow.AddDays(1) });
            await db.SaveChangesAsync();
        }

        // İki tahsilat → cari alacaklı (−300).
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        await cash.CollectAsync(new CashInput { CariId = cari, Tutar = 100m });
        await cash.CollectAsync(new CashInput { CariId = cari, Tutar = 200m });

        var d = await scope.ServiceProvider.GetRequiredService<DetailService>().GetCustomerAsync(cari);
        Assert.NotNull(d);
        Assert.Equal("Det Ay", d!.Customer.DisplayName);
        Assert.Equal(-300m, d.Bakiye);            // iki tahsilat (Alacak Cari)
        Assert.Single(d.Rentals);
        Assert.Equal(2, d.RecentLedger.Count);
    }

    [Fact]
    public async Task Detail_returns_null_for_missing_and_is_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        Guid vid;
        using (var s1 = host.ScopeFor(t1))
        {
            var factory = s1.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            var v = new Vehicle { Plaka = "34ISO01", Durum = VehicleStatus.Musait };
            db.Vehicles.Add(v); vid = v.Id;
            await db.SaveChangesAsync();
        }

        // Başka tenant → araç görünmez (RLS) → null.
        using var s2 = host.ScopeFor(Guid.NewGuid());
        var det = s2.ServiceProvider.GetRequiredService<DetailService>();
        Assert.Null(await det.GetVehicleAsync(vid));
        Assert.Null(await det.GetCustomerAsync(Guid.NewGuid()));
    }
}
