using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Kira listesi filtreleri — bağımsız oracle. Üç kira (Kirada/Tamamlandı+faturalı/İptal) seed edilir;
/// Durum/Fatura/Ofis/tarih/arama filtreleri ve müşteri-araç-fatura birleşimi doğrulanır.
/// </summary>
[Collection("postgres")]
public sealed class RentalListFilterTests(PostgresFixture fx)
{
    private static DateTimeOffset D(int y, int m, int d) => new(y, m, d, 0, 0, 0, TimeSpan.Zero);

    private static async Task<(Guid r1, Guid r2, Guid r3, string no1)> SeedAsync(IServiceScope scope)
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();

        var cust = new Customer { Tip = CariType.Bireysel, Ad = "Ali", Soyad = "Veli" };
        var veh = new Vehicle { Plaka = "34AAA01", Durum = VehicleStatus.Kirada };
        db.Customers.Add(cust);
        db.Vehicles.Add(veh);

        var r1 = new RentalContract { SozlesmeNo = "KS-F01", MusteriId = cust.Id, VehicleId = veh.Id,
            Durum = RentalStatus.Kirada, BasTar = D(2026, 6, 1), BitTar = D(2026, 6, 4), Gun = 3,
            Tutar = 300m, GenelToplam = 300m, Bakiye = 300m, CikisOfisi = "Merkez" };
        var r2 = new RentalContract { SozlesmeNo = "KS-F02", MusteriId = cust.Id, VehicleId = veh.Id,
            Durum = RentalStatus.Tamamlandi, BasTar = D(2026, 6, 10), BitTar = D(2026, 6, 12), Gun = 2,
            Tutar = 200m, GenelToplam = 200m, Bakiye = 0m, CikisOfisi = "Sube2" };
        var r3 = new RentalContract { SozlesmeNo = "KS-F03", MusteriId = cust.Id, VehicleId = veh.Id,
            Durum = RentalStatus.Iptal, BasTar = D(2026, 5, 1), BitTar = D(2026, 5, 3), Gun = 2,
            Tutar = 200m, GenelToplam = 200m, Bakiye = 200m, CikisOfisi = "Merkez" };
        db.Rentals.Add(r1);
        db.Rentals.Add(r2);
        db.Rentals.Add(r3);

        // R2 faturalı.
        db.Invoices.Add(new Invoice { No = "FT-F02", Durum = InvoiceStatus.Kesildi, CariId = cust.Id,
            RentalId = r2.Id, Tarih = D(2026, 6, 12), NetTutar = 166.67m, KdvTutar = 33.33m,
            GenelToplam = 200m, Currency = "TRY", Kur = 1m });

        await db.SaveChangesAsync();
        return (r1.Id, r2.Id, r3.Id, r1.SozlesmeNo);
    }

    [Fact]
    public async Task No_filter_returns_all_with_joins()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedAsync(scope);
        var svc = scope.ServiceProvider.GetRequiredService<RentalService>();

        var rows = await svc.SearchAsync(new RentalFilter());
        Assert.Equal(3, rows.Count);
        var r2row = rows.Single(r => r.SozlesmeNo == "KS-F02");
        Assert.Equal("Ali Veli", r2row.MusteriAd);
        Assert.Equal("34AAA01", r2row.Plaka);
        Assert.True(r2row.Faturali);
        Assert.False(rows.Single(r => r.SozlesmeNo == "KS-F01").Faturali);
    }

    [Fact]
    public async Task Filter_by_durum()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedAsync(scope);
        var svc = scope.ServiceProvider.GetRequiredService<RentalService>();

        var kirada = await svc.SearchAsync(new RentalFilter { Durum = RentalStatus.Kirada });
        Assert.Single(kirada);
        Assert.Equal("KS-F01", kirada[0].SozlesmeNo);
    }

    [Fact]
    public async Task Filter_by_fatura_durumu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedAsync(scope);
        var svc = scope.ServiceProvider.GetRequiredService<RentalService>();

        var faturali = await svc.SearchAsync(new RentalFilter { Faturali = true });
        Assert.Single(faturali);
        Assert.Equal("KS-F02", faturali[0].SozlesmeNo);

        var faturasiz = await svc.SearchAsync(new RentalFilter { Faturali = false });
        Assert.Equal(2, faturasiz.Count);
        Assert.DoesNotContain(faturasiz, r => r.SozlesmeNo == "KS-F02");
    }

    [Fact]
    public async Task Filter_by_ofis_and_date_range()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedAsync(scope);
        var svc = scope.ServiceProvider.GetRequiredService<RentalService>();

        var sube2 = await svc.SearchAsync(new RentalFilter { Ofis = "Sube2" });
        Assert.Single(sube2);
        Assert.Equal("KS-F02", sube2[0].SozlesmeNo);

        // Başlangıç ≥ 2026-06-05 → yalnız R2 (06-10); R1 (06-01) ve R3 (05-01) hariç.
        var sonra = await svc.SearchAsync(new RentalFilter { BaslangicMin = D(2026, 6, 5) });
        Assert.Single(sonra);
        Assert.Equal("KS-F02", sonra[0].SozlesmeNo);
    }

    [Fact]
    public async Task Search_by_text_matches_sozlesme_musteri_plaka()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (_, _, _, no1) = await SeedAsync(scope);
        var svc = scope.ServiceProvider.GetRequiredService<RentalService>();

        Assert.Single(await svc.SearchAsync(new RentalFilter { Query = no1 }));       // sözleşme no
        Assert.Equal(3, (await svc.SearchAsync(new RentalFilter { Query = "ali" })).Count);   // müşteri (case-insensitive)
        Assert.Equal(3, (await svc.SearchAsync(new RentalFilter { Query = "34aaa" })).Count); // plaka
        Assert.Empty(await svc.SearchAsync(new RentalFilter { Query = "YOK" }));
    }

    [Fact]
    public async Task Tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid())) await SeedAsync(s1);
        using var s2 = host.ScopeFor(Guid.NewGuid());
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<RentalService>().SearchAsync(new RentalFilter()));
    }
}
