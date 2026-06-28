using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Reporting;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Günlük faaliyet raporu — bağımsız oracle. Seçilen güne (15 Haziran) kasıtlı kayıtlar + komşu
/// günlere (14/16) gürültü; sayaç/tutarlar elle hesaplanır. Ters tahsilat ve İptal fatura hariç.
/// </summary>
[Collection("postgres")]
public sealed class GunlukFaaliyetTests(PostgresFixture fx)
{
    private static DateTimeOffset At(int d, int h) => new(2026, 6, d, h, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Counts_and_totals_for_the_day_only()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            // GÜN = 15 Haziran. Yeni rezervasyon: 2 (15), 1 gürültü (14).
            db.Reservations.Add(new Reservation { ReservationNo = "RZ-1", MusteriId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), BasTar = At(20, 9), BitTar = At(22, 9), CreatedAtUtc = At(15, 10) });
            db.Reservations.Add(new Reservation { ReservationNo = "RZ-2", MusteriId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), BasTar = At(21, 9), BitTar = At(23, 9), CreatedAtUtc = At(15, 14) });
            db.Reservations.Add(new Reservation { ReservationNo = "RZ-3", MusteriId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), BasTar = At(21, 9), BitTar = At(23, 9), CreatedAtUtc = At(14, 14) }); // gürültü

            // Yeni kira: 1 (oluşturma 15). Çıkış (BasTar 15): bu kira + 1 İptal (sayılmaz).
            db.Rentals.Add(new RentalContract { SozlesmeNo = "KS-1", MusteriId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), Durum = RentalStatus.Kirada, BasTar = At(15, 9), BitTar = At(18, 9), CreatedAtUtc = At(15, 9) });
            db.Rentals.Add(new RentalContract { SozlesmeNo = "KS-IPT", MusteriId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), Durum = RentalStatus.Iptal, BasTar = At(15, 11), BitTar = At(18, 11), CreatedAtUtc = At(10, 9) }); // çıkış sayılmaz
            // Dönüş (GercekDonusTar 15): 1; komşu güne dönüş gürültü.
            db.Rentals.Add(new RentalContract { SozlesmeNo = "KS-D1", MusteriId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), Durum = RentalStatus.Tamamlandi, BasTar = At(10, 9), BitTar = At(15, 9), GercekDonusTar = At(15, 17), CreatedAtUtc = At(10, 9) });
            db.Rentals.Add(new RentalContract { SozlesmeNo = "KS-D2", MusteriId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), Durum = RentalStatus.Tamamlandi, BasTar = At(10, 9), BitTar = At(16, 9), GercekDonusTar = At(16, 17), CreatedAtUtc = At(10, 9) }); // gürültü

            // Tahsilat: 2 gerçek (100 + 250) + 1 ters (hariç) + 1 komşu gün (hariç).
            db.CashTransactions.Add(new CashTransaction { No = "NT-1", Tip = CashTransactionType.Tahsilat, CariId = Guid.NewGuid(), Tarih = At(15, 10), Amount = new Money(100m, "TRY", 1m) });
            db.CashTransactions.Add(new CashTransaction { No = "NT-2", Tip = CashTransactionType.Tahsilat, CariId = Guid.NewGuid(), Tarih = At(15, 12), Amount = new Money(250m, "TRY", 1m) });
            db.CashTransactions.Add(new CashTransaction { No = "NT-T", Tip = CashTransactionType.Tahsilat, CariId = Guid.NewGuid(), Tarih = At(15, 13), Amount = new Money(999m, "TRY", 1m), TersKayitMi = true }); // hariç
            db.CashTransactions.Add(new CashTransaction { No = "NT-3", Tip = CashTransactionType.Tahsilat, CariId = Guid.NewGuid(), Tarih = At(16, 10), Amount = new Money(500m, "TRY", 1m) }); // gürültü

            // Fatura: 1 (400) + 1 İptal (hariç) + 1 komşu gün (hariç).
            db.Invoices.Add(new Invoice { No = "FT-1", Durum = InvoiceStatus.Kesildi, CariId = Guid.NewGuid(), Tarih = At(15, 15), NetTutar = 333.33m, KdvTutar = 66.67m, GenelToplam = 400m, Currency = "TRY", Kur = 1m });
            db.Invoices.Add(new Invoice { No = "FT-IPT", Durum = InvoiceStatus.Iptal, CariId = Guid.NewGuid(), Tarih = At(15, 16), NetTutar = 1m, KdvTutar = 0m, GenelToplam = 1m, Currency = "TRY", Kur = 1m }); // hariç
            db.Invoices.Add(new Invoice { No = "FT-2", Durum = InvoiceStatus.Kesildi, CariId = Guid.NewGuid(), Tarih = At(14, 15), NetTutar = 1m, KdvTutar = 0m, GenelToplam = 700m, Currency = "TRY", Kur = 1m }); // gürültü
            await db.SaveChangesAsync();
        }

        var svc = scope.ServiceProvider.GetRequiredService<ReportService>();
        var g = await svc.GetGunlukFaaliyetAsync(At(15, 0));

        Assert.Equal(2, g.YeniRezervasyon);
        Assert.Equal(1, g.YeniKira);
        Assert.Equal(1, g.Cikis);            // KS-1 (İptal hariç)
        Assert.Equal(1, g.Donus);            // KS-D1 (16'daki hariç)
        Assert.Equal(2, g.TahsilatAdet);     // ters hariç
        Assert.Equal(350m, g.TahsilatTutar); // 100 + 250
        Assert.Equal(1, g.FaturaAdet);       // İptal hariç
        Assert.Equal(400m, g.FaturaTutar);
    }

    [Fact]
    public async Task Tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
        {
            var factory = s1.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            db.Rentals.Add(new RentalContract { SozlesmeNo = "KS-T1", MusteriId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), Durum = RentalStatus.Kirada, BasTar = At(15, 9), BitTar = At(18, 9), CreatedAtUtc = At(15, 9) });
            await db.SaveChangesAsync();
        }

        using var s2 = host.ScopeFor(Guid.NewGuid());
        var g = await s2.ServiceProvider.GetRequiredService<ReportService>().GetGunlukFaaliyetAsync(At(15, 0));
        Assert.Equal(0, g.YeniKira);
        Assert.Equal(0, g.Cikis);
    }
}
