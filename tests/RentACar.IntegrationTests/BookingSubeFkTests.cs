using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Branches;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Locations;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ 5-C4 — booking belgelerinde türetilmiş çıkış-şube FK'sı (CikisOfisi→Location→SubeId;
/// OfficeBranchInterceptor). DEĞER-KANITI (bilinçli genişletme): B1 şubesine atanmış operatör artık
/// B1'in TÜM ofislerini görür (önce yalnız adı-eşit ofis). Eşlenmemiş ofis → FK null → salt-metin
/// (kilitlenme yok). UpdateOpen ofis-taşıma HEDEFİN türetilmiş şubesiyle denetlenir.
/// </summary>
[Collection("postgres")]
public sealed class BookingSubeFkTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas =
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-2).AddHours(9);

    /// <summary>B1("Merkez"; ofisler "Merkez"+"Havalimanı") + B2("Ankara"; ofis "Ankara Ofis") kur;
    /// üç ofiste birer kira aç. Dönüş: (B1, kiraM, kiraH, kiraA, arac).</summary>
    private static async Task<(Guid b1, Guid kiraM, Guid kiraH, Guid kiraA, Guid arac)> KurAsync(IServiceProvider sp)
    {
        var branches = sp.GetRequiredService<BranchService>();
        var b1 = await branches.CreateAsync(new BranchInput { Kod = "MRK", Ad = "Merkez" });
        await branches.CreateAsync(new BranchInput { Kod = "ANK", Ad = "Ankara" });
        var locs = sp.GetRequiredService<LocationService>();
        await locs.CreateAsync(new LocationInput { Kod = "L1", Ad = "Merkez", Sube = "Merkez" });
        await locs.CreateAsync(new LocationInput { Kod = "L2", Ad = "Havalimanı", Sube = "Merkez" });
        await locs.CreateAsync(new LocationInput { Kod = "L3", Ad = "Ankara Ofis", Sube = "Ankara" });

        var arac = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 BF 01" });
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Ofis", Soyad = "Cari" });
        var rentals = sp.GetRequiredService<RentalService>();
        async Task<Guid> Kira(string ofis, int gunOfset) => await rentals.CreateDirectAsync(new BookingInput
        {
            MusteriId = cari, VehicleId = arac, CikisOfisi = ofis,
            BasTar = Bas.AddDays(gunOfset * 5), BitTar = Bas.AddDays(gunOfset * 5 + 2), GunlukUcret = 100m
        });
        var kiraM = await Kira("Merkez", 0);
        var kiraH = await Kira("Havalimanı", 1);
        var kiraA = await Kira("Ankara Ofis", 2);
        return (b1, kiraM, kiraH, kiraA, arac);
    }

    [Fact]
    public async Task Interceptor_cikis_sube_fk_turetir_eslenmemis_null()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (b1, kiraM, kiraH, _, arac) = await KurAsync(sp);
        var rentals = sp.GetRequiredService<RentalService>();

        Assert.Equal(b1, (await rentals.GetAsync(kiraM))!.CikisSubeId);   // "Merkez" → L1 → B1
        Assert.Equal(b1, (await rentals.GetAsync(kiraH))!.CikisSubeId);   // "Havalimanı" → L2 → B1

        // Eşlenmemiş ofis (Location master'da yok) → FK null (salt-metin davranış).
        var cari = (await rentals.GetAsync(kiraM))!.MusteriId;
        var serbest = await rentals.CreateDirectAsync(new BookingInput
        {
            MusteriId = cari, VehicleId = arac, CikisOfisi = "Depo",
            BasTar = Bas.AddDays(20), BitTar = Bas.AddDays(22), GunlukUcret = 100m
        });
        Assert.Null((await rentals.GetAsync(serbest))!.CikisSubeId);
    }

    [Fact]
    public async Task Operator_subesinin_TUM_ofislerini_gorur_digerini_goremez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid b1, kiraA;
        using (var seed = host.ScopeFor(tenant))
            (b1, _, _, kiraA, _) = await KurAsync(seed.ServiceProvider);

        // Operatör@B1 (metin "Merkez" + FK): önce yalnız adı-eşit "Merkez" ofisini görürdü;
        // C4 GENİŞLETMESİ: B1'in "Havalimanı" ofisi de kapsamda. Ankara asla.
        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator,
            assignedBranch: "Merkez", assignedBranchId: b1);
        var rentals = op.ServiceProvider.GetRequiredService<RentalService>();

        var liste = await rentals.ListAsync();
        Assert.Equal(2, liste.Count);                                       // Merkez + Havalimanı (önce 1)
        Assert.All(liste, r => Assert.Equal(b1, r.CikisSubeId));

        await Assert.ThrowsAsync<ValidationException>(() => rentals.GetAsync(kiraA)); // çapraz-şube probe red
    }

    [Fact]
    public async Task Ofis_tasima_hedefin_turetilmis_subesiyle_denetlenir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid b1, kiraM;
        using (var seed = host.ScopeFor(tenant))
            (b1, kiraM, _, _, _) = await KurAsync(seed.ServiceProvider);

        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator,
            assignedBranch: "Merkez", assignedBranchId: b1);
        var rentals = op.ServiceProvider.GetRequiredService<RentalService>();

        // Kendi şubesinin DİĞER ofisine taşıma: hedef "Havalimanı" → B1 → GEÇER
        // (önce metin-eşitsizlikten RED olurdu — C4 genişletmesi).
        Assert.True(await rentals.UpdateOpenAsync(kiraM, new RentalUpdateInput { CikisOfisi = "Havalimanı" }));
        Assert.Equal("Havalimanı", (await rentals.GetAsync(kiraM))!.CikisOfisi);

        // Kapsam DIŞI şubenin ofisine taşıma: hedef "Ankara Ofis" → B2 → RED.
        await Assert.ThrowsAsync<ValidationException>(() =>
            rentals.UpdateOpenAsync(kiraM, new RentalUpdateInput { CikisOfisi = "Ankara Ofis" }));
    }

    [Fact]
    public async Task Rezervasyon_ve_teklif_listeleri_de_fk_kapsamli()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid b1;
        using (var seed = host.ScopeFor(tenant))
        {
            var sp = seed.ServiceProvider;
            (b1, _, _, _, _) = await KurAsync(sp);
            var cari = await sp.GetRequiredService<CustomerService>()
                .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Rez", Soyad = "Cari" });
            var arac2 = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 BF 02" });
            var rezBas = DateTimeOffset.UtcNow.AddDays(3);
            var rez = sp.GetRequiredService<ReservationService>();
            await rez.CreateAsync(new BookingInput
            { MusteriId = cari, VehicleId = arac2, CikisOfisi = "Havalimanı", BasTar = rezBas, BitTar = rezBas.AddDays(2), GunlukUcret = 100m });
            await rez.CreateAsync(new BookingInput
            { MusteriId = cari, VehicleId = arac2, CikisOfisi = "Ankara Ofis", BasTar = rezBas.AddDays(5), BitTar = rezBas.AddDays(7), GunlukUcret = 100m });
        }

        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator,
            assignedBranch: "Merkez", assignedBranchId: b1);
        var rezler = await op.ServiceProvider.GetRequiredService<ReservationService>().ListAsync();
        var tek = Assert.Single(rezler);                                   // yalnız Havalimanı (B1)
        Assert.Equal(b1, tek.CikisSubeId);
    }
}
