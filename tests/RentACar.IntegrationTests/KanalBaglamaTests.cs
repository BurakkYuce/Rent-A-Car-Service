using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.RateMatrices;
using RentACar.Application.ReservationSources;
using RentACar.Application.VehicleGroups;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ3-A4 — Kaynak→Kanal bağlama: PriceAsync, BookingInput.Kaynak'ı YALNIZ aktif ReservationSource
/// (Kod/Ad, Trim+case-insensitive) eşleşirse kanal olarak geçirir; eşleşmezse null (yazım hatası /
/// spoof çiti — serbest metin kanal-özel tarife SEÇTİREMEZ). BAĞIMSIZ ORACLE (elle): matrisler
/// {WEB 900/gün, base 1000/gün}, 3 gün → Kaynak=WEB 2700; kaynaksız 3000 (base tercih); "webb"
/// 3000; pasif kaynak 3000; yalnız ACENTA matrisi + Kaynak=WEB + Otomatik → TEMİZ RED (yabancı
/// kanal matrisi elenir, sessiz 0 yok). Rezervasyona İLK KEZ kanal girilince reprice 3000→2700.
/// </summary>
[Collection("postgres")]
public sealed class KanalBaglamaTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(5);

    private static async Task<(Guid m, Guid v)> SeedAsync(
        IServiceProvider sp, bool webMatrix = true, bool isSourceActive = true)
    {
        await sp.GetRequiredService<VehicleGroupService>().CreateAsync(new VehicleGroupInput
        { Kod = "EKO", Ad = "Ekonomik", GunlukKmLimiti = 300, AsimKmUcreti = 5.00m });
        var rm = sp.GetRequiredService<RateMatrixService>();
        if (webMatrix)
        {
            await rm.CreateAsync(new RateMatrixInput
            {
                Kod = "EKO-WEB", Ad = "Eko Web", Kanal = "WEB", AracGrupKod = "EKO", ParaBirimi = "TRY",
                Gun1 = 900m, Gun2 = 900m, Gun3 = 900m, OnayDurumu = TariffApprovalStatus.Onayli, Onaylayan = "t"
            });
            await rm.CreateAsync(new RateMatrixInput
            {
                Kod = "EKO-BASE", Ad = "Eko Base", AracGrupKod = "EKO", ParaBirimi = "TRY",
                Gun1 = 1000m, Gun2 = 1000m, Gun3 = 1000m, OnayDurumu = TariffApprovalStatus.Onayli, Onaylayan = "t"
            });
        }
        else
        {
            await rm.CreateAsync(new RateMatrixInput
            {
                Kod = "EKO-ACENTA", Ad = "Eko Acenta", Kanal = "ACENTA", AracGrupKod = "EKO", ParaBirimi = "TRY",
                Gun1 = 800m, Gun2 = 800m, Gun3 = 800m, OnayDurumu = TariffApprovalStatus.Onayli, Onaylayan = "t"
            });
        }
        await sp.GetRequiredService<ReservationSourceService>().CreateAsync(new ReservationSourceInput
        { Kod = "WEB", Ad = "Web Sitesi", Aktif = isSourceActive });

        var v = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 KB 01", Grup = "EKO" });
        var m = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "KB", Soyad = "M" });
        return (m, v);
    }

    private static BookingInput Input(Guid m, Guid v, string? source) => new()
    { MusteriId = m, VehicleId = v, BasTar = Start, BitTar = Start.AddDays(3), FiyatTuru = "Otomatik", Kaynak = source };

    [Fact]
    public async Task Gecerli_kaynak_kanal_matrisini_secer_gecersiz_base_kalir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (m, v) = await SeedAsync(sp);
        var rentals = sp.GetRequiredService<RentalService>();

        // Kaynak=WEB (aktif tanım) → WEB matrisi: 3 × 900 = 2700 (elle).
        var rented = await rentals.CreateDirectAsync(Input(m, v, "WEB"));
        Assert.Equal(2700.00m, (await rentals.GetAsync(rented))!.Tutar);

        // Rezervasyonlar AYRI araçta (üstteki aktif kirayla tarih çakışmasın).
        var v2 = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 KB 99", Grup = "EKO" });

        // Kaynaksız → kanal-agnostik base tercih: 3 × 1000 = 3000 (mevcut davranış korunur).
        var res = sp.GetRequiredService<ReservationService>();
        var r1 = await res.CreateAsync(Input(m, v2, null));
        Assert.Equal(3000.00m, (await res.GetAsync(r1))!.Tutar);

        // Yazım hatası "webb": tanımlı kaynak DEĞİL → çit → base 3000 (kanal-özel tarife seçtiremez).
        var r2 = await res.CreateAsync(Input(m, v2, "webb"));
        Assert.Equal(3000.00m, (await res.GetAsync(r2))!.Tutar);
    }

    [Fact]
    public async Task Pasif_kaynak_kanal_sayilmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (m, v) = await SeedAsync(sp, isSourceActive: false);
        var res = sp.GetRequiredService<ReservationService>();
        var id = await res.CreateAsync(Input(m, v, "WEB"));
        Assert.Equal(3000.00m, (await res.GetAsync(id))!.Tutar);   // pasif tanım → base
    }

    [Fact]
    public async Task Yabanci_kanal_matrisi_elenir_otomatik_temiz_red()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (m, v) = await SeedAsync(sp, webMatrix: false);        // yalnız ACENTA matrisi

        // Kanal=WEB setliyken ACENTA matrisi ADAY DEĞİL → tarife çözülemez → Otomatik temiz red
        // (sessiz 0-TL sözleşme yok). DAVRANIŞ DEĞİŞİKLİĞİ: önceden kanal geçirilmediğinden ACENTA
        // matrisi "hepsini eşle" ile seçilirdi.
        await Assert.ThrowsAsync<ValidationException>(
            () => sp.GetRequiredService<RentalService>().CreateDirectAsync(Input(m, v, "WEB")));
    }

    [Fact]
    public async Task Rezervasyona_ilk_kez_kanal_girilince_reprice()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (m, v) = await SeedAsync(sp);
        var res = sp.GetRequiredService<ReservationService>();

        var id = await res.CreateAsync(Input(m, v, null));          // kanalsız → 3000
        Assert.Equal(3000.00m, (await res.GetAsync(id))!.Tutar);
        await res.UpdateAsync(id, Input(m, v, "WEB"));              // ilk kez kanal → reprice
        Assert.Equal(2700.00m, (await res.GetAsync(id))!.Tutar);
    }
}
