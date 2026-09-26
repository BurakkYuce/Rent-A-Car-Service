using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Branches;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.EkHizmetler;
using RentACar.Application.Locations;
using RentACar.Application.RentalAddOns;
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
    private static readonly DateTimeOffset Start =
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-2).AddHours(9);

    /// <summary>B1("Merkez"; ofisler "Merkez"+"Havalimanı") + B2("Ankara"; ofis "Ankara Ofis") kur;
    /// üç ofiste birer kira aç. Dönüş: (B1, kiraM, kiraH, kiraA, arac).</summary>
    private static async Task<(Guid b1, Guid kiraM, Guid kiraH, Guid kiraA, Guid arac)> ExchangeRateAsync(IServiceProvider sp)
    {
        var branches = sp.GetRequiredService<BranchService>();
        var b1 = await branches.CreateAsync(new BranchInput { Kod = "MRK", Ad = "Merkez" });
        await branches.CreateAsync(new BranchInput { Kod = "ANK", Ad = "Ankara" });
        var locs = sp.GetRequiredService<LocationService>();
        await locs.CreateAsync(new LocationInput { Kod = "L1", Ad = "Merkez", Sube = "Merkez" });
        await locs.CreateAsync(new LocationInput { Kod = "L2", Ad = "Havalimanı", Sube = "Merkez" });
        await locs.CreateAsync(new LocationInput { Kod = "L3", Ad = "Ankara Ofis", Sube = "Ankara" });

        var vehicle = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 BF 01" });
        var account = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Ofis", Soyad = "Cari" });
        var rentals = sp.GetRequiredService<RentalService>();
        async Task<Guid> Rental(string office, int dayOffset) => await rentals.CreateDirectAsync(new BookingInput
        {
            MusteriId = account, VehicleId = vehicle, CikisOfisi = office,
            BasTar = Start.AddDays(dayOffset * 5), BitTar = Start.AddDays(dayOffset * 5 + 2), GunlukUcret = 100m
        });
        var rentalM = await Rental("Merkez", 0);
        var rentalH = await Rental("Havalimanı", 1);
        var rentalA = await Rental("Ankara Ofis", 2);
        return (b1, kiraM: rentalM, kiraH: rentalH, kiraA: rentalA, arac: vehicle);
    }

    [Fact]
    public async Task Interceptor_cikis_sube_fk_turetir_eslenmemis_null()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (b1, rentalM, rentalH, _, vehicle) = await ExchangeRateAsync(sp);
        var rentals = sp.GetRequiredService<RentalService>();

        Assert.Equal(b1, (await rentals.GetAsync(rentalM))!.CikisSubeId);   // "Merkez" → L1 → B1
        Assert.Equal(b1, (await rentals.GetAsync(rentalH))!.CikisSubeId);   // "Havalimanı" → L2 → B1

        // Eşlenmemiş ofis (Location master'da yok) → FK null (salt-metin davranış).
        var account = (await rentals.GetAsync(rentalM))!.MusteriId;
        var free = await rentals.CreateDirectAsync(new BookingInput
        {
            MusteriId = account, VehicleId = vehicle, CikisOfisi = "Depo",
            BasTar = Start.AddDays(20), BitTar = Start.AddDays(22), GunlukUcret = 100m
        });
        Assert.Null((await rentals.GetAsync(free))!.CikisSubeId);
    }

    [Fact]
    public async Task Operator_subesinin_TUM_ofislerini_gorur_digerini_goremez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid b1, rentalA;
        using (var seed = host.ScopeFor(tenant))
            (b1, _, _, rentalA, _) = await ExchangeRateAsync(seed.ServiceProvider);

        // Operatör@B1 (metin "Merkez" + FK): önce yalnız adı-eşit "Merkez" ofisini görürdü;
        // C4 GENİŞLETMESİ: B1'in "Havalimanı" ofisi de kapsamda. Ankara asla.
        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator,
            assignedBranch: "Merkez", assignedBranchId: b1);
        var rentals = op.ServiceProvider.GetRequiredService<RentalService>();

        var list = await rentals.ListAsync();
        Assert.Equal(2, list.Count);                                       // Merkez + Havalimanı (önce 1)
        Assert.All(list, r => Assert.Equal(b1, r.CikisSubeId));

        await Assert.ThrowsAsync<NoPermissionException>(() => rentals.GetAsync(rentalA)); // çapraz-şube probe red
    }

    [Fact]
    public async Task Ofis_tasima_hedefin_turetilmis_subesiyle_denetlenir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid b1, rentalM;
        using (var seed = host.ScopeFor(tenant))
            (b1, rentalM, _, _, _) = await ExchangeRateAsync(seed.ServiceProvider);

        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator,
            assignedBranch: "Merkez", assignedBranchId: b1);
        var rentals = op.ServiceProvider.GetRequiredService<RentalService>();

        // Kendi şubesinin DİĞER ofisine taşıma: hedef "Havalimanı" → B1 → GEÇER
        // (önce metin-eşitsizlikten RED olurdu — C4 genişletmesi).
        Assert.True(await rentals.UpdateOpenAsync(rentalM, new RentalUpdateInput { CikisOfisi = "Havalimanı" }));
        Assert.Equal("Havalimanı", (await rentals.GetAsync(rentalM))!.CikisOfisi);

        // Kapsam DIŞI şubenin ofisine taşıma: hedef "Ankara Ofis" → B2 → RED.
        await Assert.ThrowsAsync<NoPermissionException>(() =>
            rentals.UpdateOpenAsync(rentalM, new RentalUpdateInput { CikisOfisi = "Ankara Ofis" }));
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
            (b1, _, _, _, _) = await ExchangeRateAsync(sp);
            var account = await sp.GetRequiredService<CustomerService>()
                .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Rez", Soyad = "Cari" });
            var vehicle2 = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 BF 02" });
            var resStart = DateTimeOffset.UtcNow.AddDays(3);
            var res = sp.GetRequiredService<ReservationService>();
            await res.CreateAsync(new BookingInput
            { MusteriId = account, VehicleId = vehicle2, CikisOfisi = "Havalimanı", BasTar = resStart, BitTar = resStart.AddDays(2), GunlukUcret = 100m });
            await res.CreateAsync(new BookingInput
            { MusteriId = account, VehicleId = vehicle2, CikisOfisi = "Ankara Ofis", BasTar = resStart.AddDays(5), BitTar = resStart.AddDays(7), GunlukUcret = 100m });
        }

        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator,
            assignedBranch: "Merkez", assignedBranchId: b1);
        var reservations = await op.ServiceProvider.GetRequiredService<ReservationService>().ListAsync();
        var tek = Assert.Single(reservations);                                   // yalnız Havalimanı (B1)
        Assert.Equal(b1, tek.CikisSubeId);
    }

    [Fact]
    public async Task Ek_hizmet_kalemleri_sube_kapsamli()
    {
        // C4 adversarial Bulgu 1 (önceden var olan Medium — probe'un kalıcı hali): ek hizmet ekleme/silme
        // kapsam-guard'sız TEK booking-mutasyon yüzeyiydi; operatör GÖREMEDİĞİ çapraz-şube kiranın
        // parasını değiştirebiliyordu. Kapanış: red. Kendi şubesinin diğer ofisi guard'larla aynı kural.
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid b1, rentalH, rentalA, definition, foreignItem;
        using (var seed = host.ScopeFor(tenant))
        {
            var sp = seed.ServiceProvider;
            (b1, _, rentalH, rentalA, _) = await ExchangeRateAsync(sp);
            definition = await sp.GetRequiredService<AddOnDefinitionService>()
                .CreateAsync(new EkHizmetTanimInput { Kod = "BEBEK", Ad = "Bebek Koltuğu", BirimUcret = 100m, KdvOrani = 0.20m });
            foreignItem = await sp.GetRequiredService<RentalAddOnService>().AddAsync(rentalA, definition, 1m);
        }

        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator,
            assignedBranch: "Merkez", assignedBranchId: b1);
        var items = op.ServiceProvider.GetRequiredService<RentalAddOnService>();

        await Assert.ThrowsAsync<NoPermissionException>(() => items.AddAsync(rentalA, definition, 1m));    // çapraz-şube ekleme RED
        await Assert.ThrowsAsync<NoPermissionException>(() => items.RemoveAsync(foreignItem));     // çapraz-şube silme RED

        var item = await items.AddAsync(rentalH, definition, 2m);             // B1'in diğer ofisi → GEÇER
        Assert.True(await items.RemoveAsync(item));
    }

    [Fact]
    public async Task Teklif_tekil_islemleri_kapsamli()
    {
        // C4 adversarial Bulgu 2 (önceden var olan Low, Expense-F1 sınıfı): liste kapsamlıyken tekil
        // oku/gönder/kabul guard'sızdı — Id-probe çapraz-şube teklifi işletirdi. Kapanış + genişletme paritesi.
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid b1, tekA, tekH;
        using (var seed = host.ScopeFor(tenant))
        {
            var sp = seed.ServiceProvider;
            (b1, _, _, _, _) = await ExchangeRateAsync(sp);
            var account = await sp.GetRequiredService<CustomerService>()
                .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Teklif", Soyad = "Cari" });
            var vehicle = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 TF 01" });
            var quotations = sp.GetRequiredService<QuotationService>();
            var start = DateTimeOffset.UtcNow.AddDays(3);
            async Task<Guid> Quotation(string office, int offset) => await quotations.CreateAsync(new QuotationInput
            {
                MusteriId = account, VehicleId = vehicle, CikisOfisi = office,
                BasTar = start.AddDays(offset * 5), BitTar = start.AddDays(offset * 5 + 2), GunlukUcret = 100m
            });
            tekA = await Quotation("Ankara Ofis", 0);
            tekH = await Quotation("Havalimanı", 1);
        }

        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator,
            assignedBranch: "Merkez", assignedBranchId: b1);
        var svc = op.ServiceProvider.GetRequiredService<QuotationService>();

        await Assert.ThrowsAsync<NoPermissionException>(() => svc.GetAsync(tekA));     // Id-probe RED
        await Assert.ThrowsAsync<NoPermissionException>(() => svc.SendAsync(tekA));    // durum geçişi RED
        await Assert.ThrowsAsync<NoPermissionException>(() => svc.AcceptAsync(tekA));  // kabul RED

        // Kendi şubesinin diğer ofisi ("Havalimanı"→B1): kabul GEÇER, doğan rezervasyon kapsamda.
        var resId = await svc.AcceptAsync(tekH);
        Assert.NotNull(await op.ServiceProvider.GetRequiredService<ReservationService>().GetAsync(resId));
    }
}
