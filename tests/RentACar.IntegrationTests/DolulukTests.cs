using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Reporting;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Doluluk raporu — bağımsız oracle. Dönem 1–10 Haziran (10 gün). 2 araç → 20 araç-gün kapasite.
/// Kiralar elle kurulur; KiraGün ve % elle hesaplanır (koddan değil).
/// </summary>
[Collection("postgres")]
public sealed class DolulukTests(PostgresFixture fx)
{
    private static DateTimeOffset D(int gun) => new(2026, 6, gun, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Occupancy_is_rental_days_over_capacity()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            // 2 araç → kapasite = 2 × 10 gün = 20 araç-gün.
            db.Vehicles.Add(new Vehicle { Plaka = "34AAA1", Durum = VehicleStatus.Musait });
            db.Vehicles.Add(new Vehicle { Plaka = "34AAA2", Durum = VehicleStatus.Kirada });

            // Kira A: 3–7 Haziran kapsayıcı = 5 takvim günü (3,4,5,6,7). Tamamı dönem içinde.
            db.Rentals.Add(new RentalContract { SozlesmeNo = "KS-A", MusteriId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), Durum = RentalStatus.Tamamlandi, BasTar = D(3), BitTar = D(7), CreatedAtUtc = D(3) });
            // İptal kira: sayılmamalı.
            db.Rentals.Add(new RentalContract { SozlesmeNo = "KS-IPT", MusteriId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), Durum = RentalStatus.Iptal, BasTar = D(2), BitTar = D(9), CreatedAtUtc = D(1) });
            // Dönem dışı kira (15–18): kesişim yok, sayılmamalı.
            db.Rentals.Add(new RentalContract { SozlesmeNo = "KS-OUT", MusteriId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), Durum = RentalStatus.Kirada, BasTar = D(15), BitTar = D(18), CreatedAtUtc = D(15) });
            await db.SaveChangesAsync();
        }

        var svc = scope.ServiceProvider.GetRequiredService<ReportService>();
        var g = await svc.GetDolulukAsync(D(1), D(10));

        Assert.Equal(2, g.AracSayisi);
        Assert.Equal(10, g.DonemGun);
        Assert.Equal(20, g.AracGun);
        Assert.Equal(5, g.KiraGun);            // KS-A: 3..7 = 5 gün (İptal + dönem dışı hariç)
        Assert.Equal(25.00m, g.DolulukYuzde);  // 5 / 20 = %25
    }

    [Fact]
    public async Task Overlap_is_clipped_to_period_and_uses_actual_return()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Vehicles.Add(new Vehicle { Plaka = "06BBB1", Durum = VehicleStatus.Musait });

            // Kira dönem öncesinden başlıyor (28 May) ve gerçek dönüş 4 Haziran. Dönem 1–10.
            // Kesişim = 1..4 = 4 takvim günü. Planlı bitiş 20 Haziran AMA gerçek dönüş 4 → 4 kullanılır.
            db.Rentals.Add(new RentalContract { SozlesmeNo = "KS-B", MusteriId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), Durum = RentalStatus.Tamamlandi, BasTar = new DateTimeOffset(2026, 5, 28, 0, 0, 0, TimeSpan.Zero), BitTar = D(20), GercekDonusTar = D(4), CreatedAtUtc = new DateTimeOffset(2026, 5, 28, 0, 0, 0, TimeSpan.Zero) });
            await db.SaveChangesAsync();
        }

        var svc = scope.ServiceProvider.GetRequiredService<ReportService>();
        var g = await svc.GetDolulukAsync(D(1), D(10));

        Assert.Equal(1, g.AracSayisi);
        Assert.Equal(10, g.DonemGun);
        Assert.Equal(4, g.KiraGun);            // 1..4 (gerçek dönüş kıstası + dönem başına kırpma)
        Assert.Equal(40.00m, g.DolulukYuzde);  // 4 / 10 = %40
    }

    [Fact]
    public async Task Empty_fleet_is_zero_percent()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<ReportService>();
        var g = await svc.GetDolulukAsync(D(1), D(10));
        Assert.Equal(0, g.AracSayisi);
        Assert.Equal(0, g.AracGun);
        Assert.Equal(0m, g.DolulukYuzde);
    }

    [Fact]
    public async Task Tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
        {
            var factory = s1.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            db.Vehicles.Add(new Vehicle { Plaka = "34ZZZ1", Durum = VehicleStatus.Musait });
            db.Rentals.Add(new RentalContract { SozlesmeNo = "KS-T", MusteriId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), Durum = RentalStatus.Kirada, BasTar = D(3), BitTar = D(7), CreatedAtUtc = D(3) });
            await db.SaveChangesAsync();
        }

        using var s2 = host.ScopeFor(Guid.NewGuid());
        var g = await s2.ServiceProvider.GetRequiredService<ReportService>().GetDolulukAsync(D(1), D(10));
        Assert.Equal(0, g.AracSayisi);
        Assert.Equal(0, g.KiraGun);
    }
}
