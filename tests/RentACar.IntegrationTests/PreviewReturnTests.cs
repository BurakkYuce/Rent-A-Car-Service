using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.RentalAddOns;
using RentACar.Application.EkHizmetler;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Kira mega-form PR-D: PreviewReturnAsync (GET /kiralar/donus-hesapla arkası) — dönüş canlı önizlemesi
/// GERÇEK motordan (ReturnMath) ve PERSIST ETMEZ. BAĞIMSIZ ORACLE: limit 500, çıkış 10.000, dönüş 10.600,
/// hediye 100 → fazla 0; hediye 0 → 100×2=200; geç dönüş 2 gün × 120 = 240 (hepsi elle).
/// </summary>
[Collection("postgres")]
public sealed class PreviewReturnTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas =
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-10).AddHours(9);

    /// <summary>3 gün × 120 = 360 baz; KmLimit=500, FazlaKmUcret=2; teslim çıkış 10.000 / yakıt 8.</summary>
    private static async Task<(Guid rental, RentalService rentals, IServiceProvider sp)> TeslimliKiraAsync(
        IServiceProvider sp, string plaka)
    {
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Onizleme", Soyad = "Musteri" });
        var veh = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka });
        var rentals = sp.GetRequiredService<RentalService>();
        var rental = await rentals.CreateDirectAsync(new BookingInput
        {
            MusteriId = cari, VehicleId = veh, BasTar = Bas, BitTar = Bas.AddDays(3),
            GunlukUcret = 120m, KmLimit = 500, FazlaKmUcret = 2m
        });
        await rentals.DeliverAsync(rental, cikisKm: 10000, cikisYakit: 8);
        return (rental, rentals, sp);
    }

    [Fact]
    public async Task Hediye_asimdan_dusulur_fazla_sifir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (rental, rentals, _) = await TeslimliKiraAsync(scope.ServiceProvider, "34 PR 01");
        // kat edilen 600 − limit 500 − hediye 100 = 0 (elle)
        var p = await rentals.PreviewReturnAsync(rental, 10600, 8, Bas.AddDays(3), kmHediye: 100);
        Assert.True(p.Ok);
        Assert.Equal(600, p.KullanilanKm);
        Assert.Equal(0, p.FazlaKm);
        Assert.Equal(0m, p.FazlaKmBedeli);
        Assert.Equal(360m, p.YeniGenelToplam); // yalnız baz (3×120)
    }

    [Fact]
    public async Task Hediyesiz_asim_200_ve_gec_donus_240()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (rental, rentals, _) = await TeslimliKiraAsync(scope.ServiceProvider, "34 PR 02");
        // aşım: 600−500=100 × 2 = 200 (elle)
        var p1 = await rentals.PreviewReturnAsync(rental, 10600, 8, Bas.AddDays(3));
        Assert.Equal(100, p1.FazlaKm);
        Assert.Equal(200m, p1.FazlaKmBedeli);
        Assert.Equal(360m + 200m, p1.YeniGenelToplam);
        // geç dönüş: +2 gün × 120 = 240 (elle); aşım yok senaryosu için dönüş 10.400 (limit altı)
        var p2 = await rentals.PreviewReturnAsync(rental, 10400, 8, Bas.AddDays(5));
        Assert.Equal(2, p2.UzatmaGun);
        Assert.Equal(240m, p2.UzatmaBedeli);
        Assert.Equal(360m + 240m, p2.YeniGenelToplam);
    }

    [Fact]
    public async Task EkHizmet_dahil_ve_tahsilat_dusulmus_kalan()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (rental, rentals, _) = await TeslimliKiraAsync(sp, "34 PR 03");
        var tanim = await sp.GetRequiredService<EkHizmetTanimService>().CreateAsync(
            new EkHizmetTanimInput { Kod = "NAV", Ad = "Navigasyon", BirimUcret = 50m, KdvOrani = 0.20m });
        await sp.GetRequiredService<RentalAddOnService>().AddAsync(rental, tanim, 2m); // 2×50 net → 120 brüt (elle)

        var p = await rentals.PreviewReturnAsync(rental, 10400, 8, Bas.AddDays(3));
        Assert.Equal(120m, p.EkHizmetToplam);
        Assert.Equal(360m + 120m, p.YeniGenelToplam); // ReturnAsync ile aynı formül
        Assert.Equal(480m, p.Kalan);                  // tahsilat 0
    }

    [Fact]
    public async Task Onizleme_persist_etmez_nazik_hatalar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (rental, rentals, _) = await TeslimliKiraAsync(scope.ServiceProvider, "34 PR 04");

        await rentals.PreviewReturnAsync(rental, 10600, 8, Bas.AddDays(5), kmHediye: 50);
        var c = await rentals.GetAsync(rental);
        // PERSIST YOK: durum hâlâ Kirada, dönüş alanları boş, toplam baz (elle 360)
        Assert.Equal(RentalStatus.Kirada, c!.Durum);
        Assert.Null(c.DonusKm);
        Assert.Equal(360m, c.GenelToplam);

        // Nazik hatalar (exception değil — panelde mesaj):
        Assert.False((await rentals.PreviewReturnAsync(rental, 9999, 8, Bas.AddDays(3))).Ok);              // dönüş < çıkış
        Assert.False((await rentals.PreviewReturnAsync(rental, 10600, 8, Bas.AddDays(-11))).Ok);           // başlangıçtan önce
        Assert.False((await rentals.PreviewReturnAsync(rental, 10600, 8, Bas.AddDays(3), -5)).Ok);         // negatif hediye
        Assert.False((await rentals.PreviewReturnAsync(Guid.NewGuid(), 10600, 8, Bas.AddDays(3))).Ok);     // olmayan kira
    }

    [Fact]
    public async Task Onizleme_sube_kapsami_operator_red()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid rental;
        using (var seed = host.ScopeFor(tenant))
        {
            var sp = seed.ServiceProvider;
            var cari = await sp.GetRequiredService<CustomerService>()
                .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Ankara", Soyad = "Musteri" });
            var veh = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 PR 05" });
            var rentals = sp.GetRequiredService<RentalService>();
            rental = await rentals.CreateDirectAsync(new BookingInput
            { MusteriId = cari, VehicleId = veh, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 120m, CikisOfisi = "Ankara" });
            await rentals.DeliverAsync(rental, 10000, 8);
        }
        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator, assignedBranch: "Merkez");
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => op.ServiceProvider.GetRequiredService<RentalService>()
                .PreviewReturnAsync(rental, 10600, 8, Bas.AddDays(3)));
    }
}
