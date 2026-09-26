using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Reporting;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Ek hizmet satış raporu — bağımsız oracle. GPS (2 kirada: 1×120 + 2×240) + Koltuk (1 kirada 55);
/// İptal kiranın ek hizmeti ve dönem-dışı kalem hariç. Beklenen toplamlar ELLE hesaplanır.
/// </summary>
[Collection("postgres")]
public sealed class EkHizmetRaporuTests(PostgresFixture fx)
{
    private static DateTimeOffset D(int y, int m, int d) => new(y, m, d, 0, 0, 0, TimeSpan.Zero);

    private static RentalContract Rental(string no, RentalStatus status)
        => new() { SozlesmeNo = no, MusteriId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), Durum = status,
                   BasTar = D(2026, 6, 1), BitTar = D(2026, 6, 5) };

    private static RentalAddOn AddOn(Guid rentalId, string name, decimal quantity, decimal net, decimal vat, DateTimeOffset creation)
        => new() { RentalId = rentalId, EkHizmetTanimId = Guid.NewGuid(), Ad = name, Miktar = quantity,
                   BirimNetFiyat = net / quantity, KdvOrani = 0.20m, NetTutar = net, KdvTutar = vat, Toplam = net + vat,
                   CreatedAtUtc = creation };

    [Fact]
    public async Task Groups_by_name_excludes_cancelled_and_out_of_range()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var k1 = Rental("KS-E1", RentalStatus.Kirada);
            var k2 = Rental("KS-E2", RentalStatus.Tamamlandi);
            var kCancel = Rental("KS-E3", RentalStatus.Iptal);
            db.Rentals.AddRange(k1, k2, kCancel);

            // Dönem içi (Haziran):
            db.RentalAddOns.Add(AddOn(k1.Id, "GPS", 1m, 100m, 20m, D(2026, 6, 10)));   // GPS 120
            db.RentalAddOns.Add(AddOn(k2.Id, "GPS", 2m, 200m, 40m, D(2026, 6, 12)));   // GPS 240 (farklı kira)
            db.RentalAddOns.Add(AddOn(k1.Id, "Bebek Koltuğu", 1m, 50m, 5m, D(2026, 6, 11))); // 55
            // İPTAL kiranın ek hizmeti → HARİÇ
            db.RentalAddOns.Add(AddOn(kCancel.Id, "GPS", 1m, 999m, 199.8m, D(2026, 6, 13)));
            // Dönem DIŞI (Mayıs) → HARİÇ
            db.RentalAddOns.Add(AddOn(k1.Id, "GPS", 1m, 1000m, 200m, D(2026, 5, 1)));
            await db.SaveChangesAsync();
        }

        var svc = scope.ServiceProvider.GetRequiredService<ReportService>();
        var report = await svc.GetAddOnReportAsync(D(2026, 6, 1), D(2026, 6, 30).AddDays(1).AddTicks(-1));

        Assert.Equal(2, report.Satirlar.Count); // GPS + Bebek Koltuğu

        var gps = report.Satirlar.Single(s => s.Ad == "GPS");
        Assert.Equal(3m, gps.ToplamMiktar);  // 1 + 2
        Assert.Equal(300m, gps.Net);         // 100 + 200
        Assert.Equal(60m, gps.Kdv);          // 20 + 40
        Assert.Equal(360m, gps.Brut);        // 120 + 240
        Assert.Equal(2, gps.KiraAdet);       // k1 + k2

        var seat = report.Satirlar.Single(s => s.Ad == "Bebek Koltuğu");
        Assert.Equal(50m, seat.Net);
        Assert.Equal(55m, seat.Brut);
        Assert.Equal(1, seat.KiraAdet);

        Assert.Equal(350m, report.ToplamNet);  // 300 + 50
        Assert.Equal(65m, report.ToplamKdv);   // 60 + 5
        Assert.Equal(415m, report.ToplamBrut);
        Assert.Equal(2, report.KiraAdet);      // distinct k1, k2 (kIptal hariç)
    }

    [Fact]
    public async Task Tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
        {
            var factory = s1.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            var k = Rental("KS-T1", RentalStatus.Kirada);
            db.Rentals.Add(k);
            db.RentalAddOns.Add(AddOn(k.Id, "GPS", 1m, 100m, 20m, D(2026, 6, 10)));
            await db.SaveChangesAsync();
        }

        using var s2 = host.ScopeFor(Guid.NewGuid());
        var report = await s2.ServiceProvider.GetRequiredService<ReportService>()
            .GetAddOnReportAsync(D(2026, 6, 1), D(2026, 6, 30));
        Assert.Empty(report.Satirlar);
        Assert.Equal(0, report.KiraAdet);
    }
}
