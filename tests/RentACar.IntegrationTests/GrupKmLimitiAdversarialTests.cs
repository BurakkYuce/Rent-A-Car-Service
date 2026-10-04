using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.RateMatrices;
using RentACar.Application.ReservationSources;
using RentACar.Application.VehicleGroups;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// #366 adversarial bulguları — probe'lar kalıcı test. Grup "LUKS2" 250 km/gün + 5 TL, kira 3 × 1.000 (elle oracle).
/// H1: tarife/grup kaynaklı km hakkı GÜNLÜKTÜR — uzatma ve geç dönüşte büyür. M1: tarife kademesi km'si gruptan
/// önce gelir. M2: "0" boş demektir; sınırsız yalnız açık bayrakla. L1/L2: önizleme kaynak/kayıtlı limit/döviz notu.
/// </summary>
[Collection("postgres")]
public sealed class GrupKmLimitiAdversarialTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Start = TestZaman.DaysLater(-10);

    private static async Task<(Guid Customer, Guid Vehicle)> PartiesAsync(
        IServiceProvider sp, string plate, string group = "LUKS2", int daily = 250, decimal fee = 5m)
    {
        var groups = sp.GetRequiredService<VehicleGroupService>();
        if (!(await groups.ListAsync()).Any(g => g.Kod == group))
            await groups.CreateAsync(new VehicleGroupInput { Kod = group, Ad = group, GunlukKmLimiti = daily, AsimKmUcreti = fee });
        var vehicle = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plate, Grup = group });
        return (await TestCustomer.NewAsync(sp), vehicle);
    }

    private static async Task<(Guid Id, RentalService Rentals)> RentalAsync(IServiceProvider sp, string plate, Action<BookingInput>? edit = null)
    {
        var (customer, vehicle) = await PartiesAsync(sp, plate);
        var input = new BookingInput
        {
            MusteriId = customer, VehicleId = vehicle, BasTar = Start, BitTar = Start.AddDays(3), GunlukUcret = 1000m,
        };
        edit?.Invoke(input);
        var rentals = sp.GetRequiredService<RentalService>();
        return (await rentals.CreateDirectAsync(input), rentals);
    }

    /// <summary>H1 — 3 → 6 gün uzatma: hak 250 × 6 = 1.500; 1.100 km → aşım 0 (eski: 1.100 − 750 = 350 × 5 = 1.750).</summary>
    [Fact]
    public async Task Uzatmada_gunluk_km_hakki_buyur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (id, rentals) = await RentalAsync(scope.ServiceProvider, "34 GA 01");
        await rentals.ExtendAsync(id, Start.AddDays(6));
        Assert.Equal(1500, (await rentals.GetAsync(id))!.KmLimit);

        await rentals.DeliverAsync(id, 10_000, 8);
        await rentals.ReturnAsync(id, 11_100, 8, Start.AddDays(6));
        var c = (await rentals.GetAsync(id))!;
        Assert.Equal(0, c.FazlaKm);
        Assert.Equal(6000m, c.GenelToplam);
    }

    /// <summary>H1 — tam 2 gün geç dönüş: hak 250 × (3 + 2) = 1.250; 1.200 km → aşım 0; uzatma 2 × 1.000.</summary>
    [Fact]
    public async Task Gec_donuste_gunluk_km_hakki_buyur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (id, rentals) = await RentalAsync(scope.ServiceProvider, "34 GA 02");
        await rentals.DeliverAsync(id, 10_000, 8);
        var preview = await rentals.PreviewReturnAsync(id, 11_200, 8, Start.AddDays(5));
        Assert.Equal(0, preview.FazlaKm);
        Assert.Equal(2000m, preview.UzatmaBedeli);
        // 1.300 km → 1.300 − 1.250 = 50 × 5 = 250.
        Assert.Equal(250m, (await rentals.PreviewReturnAsync(id, 11_300, 8, Start.AddDays(5))).FazlaKmBedeli);
    }

    /// <summary>H1 sınırı — elle girilen TOPLAM limit uzatmada büyümez (mevcut davranış).</summary>
    [Fact]
    public async Task Elle_toplam_limit_uzatmada_degismez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (id, rentals) = await RentalAsync(scope.ServiceProvider, "34 GA 03", i => i.KmLimit = 1000);
        await rentals.ExtendAsync(id, Start.AddDays(6));
        var c = (await rentals.GetAsync(id))!;
        Assert.Equal(1000, c.KmLimit);
        Assert.Null(c.KmLimitGunluk);
    }

    /// <summary>M1 — tarife 3. kademe 300 km/gün 2 TL, grup 200/5 → kira 3 × 300 = 900 km, 2 TL.</summary>
    [Fact]
    public async Task Tarife_kademe_km_si_gruptan_once_gelir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (customer, vehicle) = await PartiesAsync(sp, "34 GA 04", group: "TRF", daily: 200, fee: 5m);
        await sp.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
        {
            Kod = "TRF-1", Ad = "Tarife", AracGrupKod = "TRF",
            Gun1 = 900m, Gun2 = 900m, Gun3 = 900m, Gun4 = 900m, Gun5 = 900m, Gun6 = 900m, Gun7 = 900m,
            Km3 = 300, Km3Ucret = 2m, OnayDurumu = TariffApprovalStatus.Onayli,
        });
        var rentals = sp.GetRequiredService<RentalService>();
        var c = (await rentals.GetAsync(await rentals.CreateDirectAsync(new BookingInput
        {
            MusteriId = customer, VehicleId = vehicle, BasTar = Start, BitTar = Start.AddDays(3), GunlukUcret = 1000m,
        })))!;
        Assert.Equal(900, c.KmLimit);
        Assert.Equal(2m, c.FazlaKmUcret);
        Assert.Equal(300, c.KmLimitGunluk);
    }

    /// <summary>M2 — sınırsız yalnız bayrakla; açık kira düzenlemesi aynı anlam (0 = boş → grup, bayrak → 0).</summary>
    [Fact]
    public async Task Km_sinirsiz_bayragi_ve_acik_kira_duzenlemesi()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (id, rentals) = await RentalAsync(scope.ServiceProvider, "34 GA 05", i => i.KmSinirsiz = true);
        var c = (await rentals.GetAsync(id))!;
        Assert.True(c.KmSinirsiz);
        Assert.Equal(0, c.KmLimit);
        Assert.Equal(0m, c.FazlaKmUcret);

        // Bayrak kaldırılıp limit BOŞ gönderilir → grup limiti (750, 5 TL) uygulanır.
        await rentals.UpdateOpenAsync(id, new RentalUpdateInput { KmSinirsiz = false, KmLimit = 0 });
        c = (await rentals.GetAsync(id))!;
        Assert.False(c.KmSinirsiz);
        Assert.Equal(750, c.KmLimit);
        Assert.Equal(5m, c.FazlaKmUcret);
        Assert.Equal(250, c.KmLimitGunluk);

        // Uzatma sonrası form kayıtlı limiti AYNEN geri gönderir → günlük snapshot korunur (hak 6 × 250).
        await rentals.ExtendAsync(id, Start.AddDays(6));
        await rentals.UpdateOpenAsync(id, new RentalUpdateInput { KmLimit = 1500, FazlaKmUcret = 5m });
        c = (await rentals.GetAsync(id))!;
        Assert.Equal(1500, c.KmLimit);
        Assert.Equal(250, c.KmLimitGunluk);

        // Bayrak yeniden açılır → limit 0, hak uygulanmaz.
        await rentals.UpdateOpenAsync(id, new RentalUpdateInput { KmSinirsiz = true, KmLimit = 1500, FazlaKmUcret = 5m });
        c = (await rentals.GetAsync(id))!;
        Assert.True(c.KmSinirsiz);
        Assert.Equal(0, c.KmLimit);
        Assert.Null(c.KmLimitGunluk);
    }

    /// <summary>L1 — önizleme kaynağı (KmSinirsiz) ve kayıtlı kirada sözleşme hakkını bilir; L2 — dövizli not.</summary>
    [Fact]
    public async Task Onizleme_kaynak_kayitli_limit_ve_doviz_notu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<ReservationSourceService>().CreateAsync(
            new ReservationSourceInput { Kod = "SINIRSIZ2", Ad = "Sınırsız2", KmSinirsiz = true });
        var (id, rentals) = await RentalAsync(sp, "34 GA 06", i => i.KmLimit = 1000);
        var vehicle = (await rentals.GetAsync(id))!.VehicleId;
        var calc = sp.GetRequiredService<RentalCalculationService>();
        KiraHesapIstek Req(string? doviz = null, string? kaynak = null, Guid? rentalId = null) => new(
            VehicleId: vehicle, BasTar: Start, BitTar: Start.AddDays(3), GunlukUcret: 1000m, FiyatTuru: null,
            Doviz: doviz, CikisOfisi: null, EkHizmetler: [], RentalId: rentalId, Kaynak: kaynak);

        var unlimited = await calc.CalculateAsync(Req(kaynak: "SINIRSIZ2"));
        Assert.True(unlimited.KmSinirsiz);
        Assert.Null(unlimited.KmLimit);

        var stored = await calc.CalculateAsync(Req(rentalId: id));
        Assert.Equal(1000, stored.KmLimit);

        var foreign = await calc.CalculateAsync(Req(doviz: "EUR"));
        Assert.Equal(750, foreign.KmLimit);
        Assert.Null(foreign.FazlaKmUcret);
        Assert.Contains(GroupKmPolicy.FxFeeNote, foreign.Notlar ?? []);
    }
}
