using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.DolulukFiyat;
using RentACar.Application.Pricing;
using RentACar.Application.RateMatrices;
using RentACar.Application.VehicleGroups;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// #368 adversarial incelemesinin probe'ları (kalıcı). BAĞIMSIZ ORACLE (elle kurulmuş senaryolar):
/// M1 — pasif grup {Kod EKO} + aktif grup {Kod B2, Ad "EKO"}: Grup="EKO" araç EKO tarifesinde kalır
///      (Gün3 = 100 → 3 gün 300), B2'ye (555 → 1.665) KAYMAZ.
/// M2a — A1/"SUV" varken SUV/"Büyük" (kod başka grubun adı) ya da tersi açılamaz / yeniden adlandırılamaz.
/// M2b — eski veride çakışma varsa bile bir araç TEK gruba sayılır (doluluk + sayaç): "SUV" yazan araç
///      kod SUV'un grubudur; A1'in doluluğuna girmez (A1 %0 → 1.000, SUV %100 → 1.100).
/// Çakışan eski veri servis çitini (M2a) aşmak için doğrudan repository ile yazılır.
/// </summary>
[Collection("postgres")]
public sealed class VehicleGroupMatchAdversarialTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Start = TestZaman.DaysLater(6);

    private static Task MatrixAsync(IServiceProvider sp, string code, string group, decimal day) =>
        sp.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
        {
            Kod = code, Ad = code, AracGrupKod = group, ParaBirimi = "TRY",
            Gun1 = day, Gun2 = day, Gun3 = day, Gun4 = day, Gun5 = day, OnayDurumu = TariffApprovalStatus.Onayli
        });

    [Fact]
    public async Task M1_inactive_group_code_does_not_fall_through_to_another_groups_name()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var repo = sp.GetRequiredService<IVehicleGroupRepository>();
        await repo.CreateAsync(new VehicleGroup { Kod = "EKO", Ad = "Eski Ekonomi", Aktif = false });
        await repo.CreateAsync(new VehicleGroup { Kod = "B2", Ad = "EKO" });
        await MatrixAsync(sp, "EKO-M", "EKO", 100m);
        await MatrixAsync(sp, "B2-M", "B2", 555m);
        var vehicle = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 GM 01", Grup = "EKO" });

        var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = await TestCustomer.NewAsync(sp), VehicleId = vehicle, BasTar = Start, BitTar = Start.AddDays(3),
            FiyatTuru = "Otomatik"
        });
        Assert.Equal(300m, (await sp.GetRequiredService<IBookingRepository>().FindRentalAsync(id))!.Tutar);
    }

    [Fact]
    public async Task M2a_group_name_cannot_equal_another_groups_code_and_vice_versa()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var groups = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();
        await groups.CreateAsync(new VehicleGroupInput { Kod = "A1", Ad = "SUV" });

        await Assert.ThrowsAsync<ValidationException>(() =>
            groups.CreateAsync(new VehicleGroupInput { Kod = "SUV", Ad = "Büyük" }));   // kod = başka grubun adı
        await Assert.ThrowsAsync<ValidationException>(() =>
            groups.CreateAsync(new VehicleGroupInput { Kod = "X9", Ad = "a1" }));       // ad = başka grubun kodu

        var other = await groups.CreateAsync(new VehicleGroupInput { Kod = "C3", Ad = "Orta" });
        await Assert.ThrowsAsync<ValidationException>(() =>
            groups.UpdateAsync(other, new VehicleGroupInput { Kod = "C3", Ad = "A1" }));
        await Assert.ThrowsAsync<ValidationException>(() =>
            groups.UpdateAsync(other, new VehicleGroupInput { Kod = "suv", Ad = "Orta" }));
        // Kendi kodu = kendi adı serbest (tek grup, çakışma yok).
        Assert.True(await groups.UpdateAsync(other, new VehicleGroupInput { Kod = "C3", Ad = "C3" }));
    }

    [Fact]
    public async Task M2b_vehicle_counts_in_exactly_one_group_for_occupancy_and_counter()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var repo = sp.GetRequiredService<IVehicleGroupRepository>();
        var a = new VehicleGroup { Kod = "A1", Ad = "SUV" };
        var b = new VehicleGroup { Kod = "SUV", Ad = "Büyük" };
        await repo.CreateAsync(a);
        await repo.CreateAsync(b);
        await MatrixAsync(sp, "A1-M", "A1", 1000m);
        await MatrixAsync(sp, "SUV-M", "SUV", 1000m);
        var vehicles = sp.GetRequiredService<VehicleService>();
        await vehicles.CreateAsync(new VehicleInput { Plaka = "34 GM 11", Grup = "A1" });   // A1 — boş
        var suv = await vehicles.CreateAsync(new VehicleInput { Plaka = "34 GM 12", Grup = "SUV" }); // kod SUV → B
        await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = await TestCustomer.NewAsync(sp), VehicleId = suv, BasTar = Start, BitTar = Start.AddDays(5), GunlukUcret = 100m });
        await sp.GetRequiredService<OccupancyPriceRuleService>().CreateAsync(
            new DolulukFiyatKuralInput { Kod = "D50", Ad = "Doluluk 50", EsikYuzde = 50, CarpanYuzde = 10m });

        var engine = sp.GetRequiredService<RentalQuoteEngine>();
        var qa = await engine.QuoteAsync(new QuoteRequest { AracGrupKod = "A1", BasTar = Start, BitTar = Start.AddDays(5) });
        var qb = await engine.QuoteAsync(new QuoteRequest { AracGrupKod = "SUV", BasTar = Start, BitTar = Start.AddDays(5) });
        Assert.Equal(1000m, qa.GunlukUcret);   // A1: 0/5 araç-gün → %0
        Assert.Equal(1100m, qb.GunlukUcret);   // SUV: 5/5 → %100 ≥ 50 → +%10

        var counts = await sp.GetRequiredService<VehicleGroupService>().VehicleCountsAsync();
        Assert.Equal(1, counts[a.Id]);
        Assert.Equal(1, counts[b.Id]);
    }
}
