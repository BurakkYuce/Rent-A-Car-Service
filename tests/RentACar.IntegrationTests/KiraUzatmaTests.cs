using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap I1 — kira uzatma (ExtendAsync). BAĞIMSIZ ORACLE: 3 gün×100=300; +2 gün → 500; tekrar +2 → 700.
/// K1 fix: planlı uzatma BAZ kiradır (Gun+Tutar); UzatmaGun/Bedeli YALNIZ geç dönüş → BaseGross çift saymaz
/// (dönüş-öncesi fatura + ek-hizmet Recompute regresyon testleri aşağıda). Geriye/çakışan uzatma red.
/// </summary>
[Collection("postgres")]
public sealed class KiraUzatmaTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas = new(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

    private static async Task<(IServiceProvider sp, Guid rentalId)> SeedRental(TestHost host, IServiceScope scope, string plaka)
    {
        var sp = scope.ServiceProvider;
        var cust = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Uzatma" });
        var veh = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka, Durum = VehicleStatus.Musait });
        var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = cust, VehicleId = veh, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 100m, KmLimit = 0, FazlaKmUcret = 0m });
        return (sp, id);
    }

    [Fact]
    public async Task Uzatma_bedel_ve_kumulatif()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, id) = await SeedRental(host, scope, "34 UZ 01");
        var svc = sp.GetRequiredService<RentalService>();

        Assert.True(await svc.ExtendAsync(id, Bas.AddDays(5)));   // +2 gün
        var c1 = await svc.GetAsync(id);
        Assert.Equal(5, c1!.Gun);
        Assert.Equal(500m, c1.Tutar);              // baz kira: 5 × 100 (K1 — uzatma bazın parçası)
        Assert.Equal(0, c1.UzatmaGun);             // planlı uzatma UzatmaGun'a YAZILMAZ (yalnız geç dönüş)
        Assert.Equal(0m, c1.UzatmaBedeli);         // aksi hâlde BaseGross (Tutar+UzatmaBedeli) çift sayardı
        Assert.Equal(500m, c1.GenelToplam);        // 300 + 200
        Assert.Equal(500m, c1.Bakiye);

        Assert.True(await svc.ExtendAsync(id, Bas.AddDays(7)));   // tekrar +2
        var c2 = await svc.GetAsync(id);
        Assert.Equal(700m, c2!.Tutar);             // kümülatif: 7 × 100
        Assert.Equal(0m, c2.UzatmaBedeli);
        Assert.Equal(700m, c2.GenelToplam);
    }

    [Fact]
    public async Task Uzatma_sonrasi_fatura_cift_saymaz() // denetim K1 regresyonu
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, id) = await SeedRental(host, scope, "34 UZ 04");

        Assert.True(await sp.GetRequiredService<RentalService>().ExtendAsync(id, Bas.AddDays(5))); // 3→5 gün
        var invoices = sp.GetRequiredService<RentACar.Application.Finance.InvoiceService>();
        var inv = await invoices.GetAsync(await invoices.CreateFromRentalAsync(id));

        Assert.Equal(500m, inv!.GenelToplam); // BAĞIMSIZ ORACLE: 5×100 (eski bug 300+200+200=700 keserdi)
    }

    [Fact]
    public async Task Uzatma_sonrasi_ek_hizmet_recompute_cift_saymaz() // denetim K1 regresyonu
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, id) = await SeedRental(host, scope, "34 UZ 05");

        Assert.True(await sp.GetRequiredService<RentalService>().ExtendAsync(id, Bas.AddDays(5))); // toplam 500
        // Ek hizmet: net 100, %20 KDV → brüt 120 → GenelToplam 500+120=620 (eski bug: 700+120).
        var tanimId = await sp.GetRequiredService<RentACar.Application.EkHizmetler.AddOnDefinitionService>().CreateAsync(
            new RentACar.Application.EkHizmetler.EkHizmetTanimInput { Kod = "KLT", Ad = "Koltuk", BirimUcret = 100m, KdvOrani = 0.20m });
        await sp.GetRequiredService<RentACar.Application.RentalAddOns.RentalAddOnService>().AddAsync(id, tanimId, 1m);

        var c = await sp.GetRequiredService<RentalService>().GetAsync(id);
        Assert.Equal(620m, c!.GenelToplam); // BAĞIMSIZ ORACLE: 500 baz + 120 ek hizmet brüt
    }

    [Fact]
    public async Task Geriye_uzatma_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, id) = await SeedRental(host, scope, "34 UZ 02");
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => sp.GetRequiredService<RentalService>().ExtendAsync(id, Bas.AddDays(3))); // = mevcut bitiş
    }

    [Fact]
    public async Task Cakisan_uzatma_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, id) = await SeedRental(host, scope, "34 UZ 03");
        // Aynı araca, uzatma penceresine denk gelen ikinci aktif kira.
        var c = await sp.GetRequiredService<RentalService>().GetAsync(id);
        await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = await TestCari.YeniAsync(sp), VehicleId = c!.VehicleId, BasTar = Bas.AddDays(4), BitTar = Bas.AddDays(6), GunlukUcret = 100m });

        await Assert.ThrowsAsync<AvailabilityConflictException>(
            () => sp.GetRequiredService<RentalService>().ExtendAsync(id, Bas.AddDays(5))); // [bas,bas+5] ∩ [bas+4,bas+6]
    }
}
