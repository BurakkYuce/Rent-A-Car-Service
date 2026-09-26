using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Pricing;
using RentACar.Application.RateMatrices;
using RentACar.Application.VehicleGroups;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ3-A1 — Uzun-dönem kademeleri: RateMatrix.GunHaftalik (8-29 gün) + GunAylik (30+).
/// BAĞIMSIZ ORACLE (elle): Gun7=700 / Haftalık=650 / Aylık=500 → 7g=4900 (haftalık DEĞİL — kademe
/// 8'de başlar), 10g=6500, 30g=15000, 29g=18850 (+ "30 güne uzat" TERS-DÖNME notu: 30×500=15000 &lt;
/// 29×650=18850 — sektör gerçeği, otomatik düzeltme YOK); kademesiz matriste 10g=7000 (Gun7-clamp
/// geriye-uyum + not); ComputeGun sınırı 29g23s → 30 gün → aylık. Rezervasyon güncellemesinde
/// kademe atlama: Otomatik 7g=4900 → 10g'e uzatınca reprice 6500 (facade tek yol).
/// </summary>
[Collection("postgres")]
public sealed class UzunDonemKademeTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(5);

    private static async Task SeedMatrixAsync(IServiceProvider sp, decimal? weekly, decimal? monthly)
    {
        await sp.GetRequiredService<VehicleGroupService>().CreateAsync(new VehicleGroupInput
        { Kod = "EKO", Ad = "Ekonomik", GunlukKmLimiti = 300, AsimKmUcreti = 5.00m });
        await sp.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
        {
            Kod = "EKO-STD", Ad = "Eko Standart", AracGrupKod = "EKO", ParaBirimi = "TRY",
            Gun7 = 700m, GunHaftalik = weekly, GunAylik = monthly,
            OnayDurumu = TariffApprovalStatus.Onayli, Onaylayan = "t"
        });
    }

    private static Task<QuoteResult> QuoteAsync(IServiceProvider sp, DateTimeOffset bit) =>
        sp.GetRequiredService<RentalQuoteEngine>().QuoteAsync(new QuoteRequest
        { AracGrupKod = "EKO", BasTar = Start, BitTar = bit });

    [Fact]
    public async Task Kademeler_elle_oracle()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedMatrixAsync(sp, weekly: 650m, monthly: 500m);

        var g7 = await QuoteAsync(sp, Start.AddDays(7));      // 7g: haftalık DEĞİL → Gun7 700 → 4900
        Assert.Equal(700.00m, g7.GunlukUcret);
        Assert.Equal(4900.00m, g7.GenelToplam);

        var g10 = await QuoteAsync(sp, Start.AddDays(10));    // 10g: haftalık 650 → 6500
        Assert.Equal(650.00m, g10.GunlukUcret);
        Assert.Equal(6500.00m, g10.GenelToplam);

        var g30 = await QuoteAsync(sp, Start.AddDays(30));    // 30g: aylık 500 → 15000
        Assert.Equal(500.00m, g30.GunlukUcret);
        Assert.Equal(15000.00m, g30.GenelToplam);

        // 29g: 650×29 = 18850 > 30×500 = 15000 → TERS-DÖNME bilgisi notu (otomatik düzeltme yok).
        var g29 = await QuoteAsync(sp, Start.AddDays(29));
        Assert.Equal(18850.00m, g29.GenelToplam);
        Assert.Contains(g29.Notlar, n => n.Contains("30 güne uzatmak toplamda daha ucuz"));

        // ComputeGun sınırı: 29 gün 23 saat → 30 gün sayılır → aylık kademe.
        var limit = await QuoteAsync(sp, Start.AddDays(29).AddHours(23));
        Assert.Equal(30, limit.Gun);
        Assert.Equal(15000.00m, limit.GenelToplam);
    }

    [Fact]
    public async Task Kademesiz_matris_gun7_clamp_ve_not()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedMatrixAsync(sp, weekly: null, monthly: null);

        // Geriye uyum: uzun-dönem kolonları null → bugünkü Gun7-clamp davranışı + açık not.
        var g10 = await QuoteAsync(sp, Start.AddDays(10));
        Assert.Equal(7000.00m, g10.GenelToplam);             // 10 × 700 (elle)
        Assert.Contains(g10.Notlar, n => n.Contains("uzun-dönem kademesi"));
    }

    [Fact]
    public async Task Rezervasyon_guncelleme_kademe_atlar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedMatrixAsync(sp, weekly: 650m, monthly: 500m);
        var v = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 UD 01", Grup = "EKO" });
        var m = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "UD", Soyad = "M" });

        BookingInput Input(int day) => new()
        { MusteriId = m, VehicleId = v, BasTar = Start, BitTar = Start.AddDays(day), FiyatTuru = "Otomatik" };

        var res = sp.GetRequiredService<ReservationService>();
        var id = await res.CreateAsync(Input(7));
        Assert.Equal(4900.00m, (await res.GetAsync(id))!.Tutar);   // 7 × 700 (elle)

        // 10 güne uzat: reprice kademe atlar → 10 × 650 = 6500 (7×700 kalıntısı DEĞİL).
        await res.UpdateAsync(id, Input(10));
        var g = (await res.GetAsync(id))!;
        Assert.Equal(6500.00m, g.Tutar);
        Assert.Equal(650.00m, g.GunlukUcret);
    }

    [Fact]
    public async Task Negatif_kademe_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await Assert.ThrowsAsync<ValidationException>(() =>
            sp.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
            { Kod = "NEG", Ad = "Negatif", GunHaftalik = -1m }));
    }
}
