using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Personnel;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR2 — dönüş ek alanları: KM Hediye (aşımdan düşülen bedava km — PARA), Bitiş Sebebi, Teslim Alan.
/// BAĞIMSIZ ORACLE: limit=300, kat edilen=500 → aşım 200; hediye=100 → fazla 100 × 2 TL = 200 TL (elle).
/// KM-aşım parasının tek otoritesi dönüş-zamanı ReturnMath (KURAL A).
/// </summary>
[Collection("postgres")]
public sealed class DonusAlanlariTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas =
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-10).AddHours(9);

    /// <summary>Kira: 3 gün × 100 = 300 TL baz; KmLimit=300, FazlaKmUcret=2 TL/km. Çıkış km 10.000.</summary>
    private static async Task<(Guid rental, RentalService rentals)> TeslimliKiraAsync(
        IServiceProvider sp, string plaka, int kmLimit = 300)
    {
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Donus", Soyad = "Musteri" });
        var veh = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka });
        var rentals = sp.GetRequiredService<RentalService>();
        var rental = await rentals.CreateDirectAsync(new BookingInput
        {
            MusteriId = cari, VehicleId = veh, BasTar = Bas, BitTar = Bas.AddDays(3),
            GunlukUcret = 100m, KmLimit = kmLimit, FazlaKmUcret = 2m
        });
        await rentals.DeliverAsync(rental, cikisKm: 10000, cikisYakit: 8);
        return (rental, rentals);
    }

    // ---------- KM Hediye (para) ----------
    [Fact]
    public async Task KmHediye_asimdan_dusulur_oracle()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (rental, rentals) = await TeslimliKiraAsync(scope.ServiceProvider, "34 DA 01");

        // kat edilen 500, limit 300 → aşım 200; hediye 100 → fazla 100 × 2 = 200 TL (elle oracle).
        await rentals.ReturnAsync(rental, donusKm: 10500, donusYakit: 8, Bas.AddDays(3), kmHediye: 100);

        var c = await rentals.GetAsync(rental);
        Assert.Equal(100, c!.FazlaKm);                 // 500 − 300 − 100
        Assert.Equal(200m, c.FazlaKmBedeli);           // 100 × 2
        Assert.Equal(100, c.KmHediye);
        Assert.Equal(300m + 200m, c.GenelToplam);      // baz 300 + fazla-km 200 (yakıt/uzatma yok)
        Assert.Equal(c.GenelToplam - c.Tahsilat, c.Bakiye);
    }

    [Fact]
    public async Task KmHediye_sifir_tam_asim_regresyon()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (rental, rentals) = await TeslimliKiraAsync(scope.ServiceProvider, "34 DA 02");
        await rentals.ReturnAsync(rental, donusKm: 10500, donusYakit: 8, Bas.AddDays(3)); // hediye yok
        var c = await rentals.GetAsync(rental);
        Assert.Equal(200, c!.FazlaKm);        // 500 − 300 (mevcut davranış)
        Assert.Equal(400m, c.FazlaKmBedeli);  // 200 × 2
        Assert.Null(c.KmHediye);              // 0 → null (girilmedi)
    }

    [Fact]
    public async Task KmHediye_asimdan_buyuk_fazla_sifir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (rental, rentals) = await TeslimliKiraAsync(scope.ServiceProvider, "34 DA 03");
        await rentals.ReturnAsync(rental, donusKm: 10500, donusYakit: 8, Bas.AddDays(3), kmHediye: 999);
        var c = await rentals.GetAsync(rental);
        Assert.Equal(0, c!.FazlaKm);         // Max(0, 200 − 999)
        Assert.Equal(0m, c.FazlaKmBedeli);
        Assert.Equal(300m, c.GenelToplam);   // yalnız baz
    }

    [Fact]
    public async Task KmHediye_negatif_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (rental, rentals) = await TeslimliKiraAsync(scope.ServiceProvider, "34 DA 04");
        // Negatif hediye = aşımı ŞİŞİRME hilesi olurdu (para) → erken red.
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => rentals.ReturnAsync(rental, 10500, 8, Bas.AddDays(3), kmHediye: -50));
        Assert.Contains("negatif", ex.Message);
    }

    [Fact]
    public async Task Limitsiz_kirada_hediye_etkisiz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (rental, rentals) = await TeslimliKiraAsync(scope.ServiceProvider, "34 DA 05", kmLimit: 0);
        await rentals.ReturnAsync(rental, donusKm: 15000, donusYakit: 8, Bas.AddDays(3), kmHediye: 100);
        var c = await rentals.GetAsync(rental);
        Assert.Equal(0, c!.FazlaKm); // KmLimit=0 = sınırsız → fazla yok (regresyon)
        Assert.Equal(300m, c.GenelToplam);
    }

    // ---------- Bitiş Sebebi + Teslim Alan ----------
    [Fact]
    public async Task BitisSebebi_ve_TeslimAlan_roundtrip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var pid = await sp.GetRequiredService<PersonelService>().CreateAsync(new PersonelInput
        { Kod = "P-01", Ad = "Onur", Soyad = "Yuce" });
        var (rental, rentals) = await TeslimliKiraAsync(sp, "34 DA 06");

        await rentals.ReturnAsync(rental, 10200, 8, Bas.AddDays(3),
            bitisSebebi: "Erken İade", teslimAlanPersonelId: pid);

        var c = await rentals.GetAsync(rental);
        Assert.Equal("Erken İade", c!.BitisSebebi);
        Assert.Equal(pid, c.TeslimAlanPersonelId);
    }

    // ---------- Adversarial regresyonları ----------
    [Fact]
    public async Task KmHediye_int_max_tasma_uretmez_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (rental, rentals) = await TeslimliKiraAsync(scope.ServiceProvider, "34 DA 08");
        // BULGU 1 (Kritik): int.MaxValue hediye, limit-altı kirada int wrap ile 4,29 MİLYAR TL hayalet
        // borç üretiyordu (deftere kadar). Artık üst sınır reddi + ReturnMath long aritmetik.
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => rentals.ReturnAsync(rental, 10100, 8, Bas.AddDays(3), kmHediye: int.MaxValue));
        Assert.Contains("gerçekçi değil", ex.Message);
        Assert.Equal(RentalStatus.Kirada, (await rentals.GetAsync(rental))!.Durum); // yarım yazım yok
    }

    [Fact]
    public async Task BitisSebebi_uzun_metin_temiz_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (rental, rentals) = await TeslimliKiraAsync(scope.ServiceProvider, "34 DA 09");
        // BULGU 2 (Low): 65+ karakter varchar(64)'e çakılıp 500 veriyordu → temiz ValidationException.
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => rentals.ReturnAsync(rental, 10100, 8, Bas.AddDays(3), bitisSebebi: new string('x', 65)));
        Assert.Contains("64 karakter", ex.Message);
    }

    [Fact]
    public async Task Kesirli_ucrette_bedel_iki_haneye_yuvarlanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        // BULGU 3 (Low): 0.3333 × 3 km = 0.9999 sözleşmede kalıp fatura RoundGross(301.00) ile 0,0001
        // ıraksıyordu → satır-bazlı 2 hane yuvarlama: bedel 1.00, GenelToplam 301.00 (elle oracle).
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Kesir", Soyad = "Musteri" });
        var veh = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 DA 10" });
        var rentals = sp.GetRequiredService<RentalService>();
        var rental = await rentals.CreateDirectAsync(new BookingInput
        {
            MusteriId = cari, VehicleId = veh, BasTar = Bas, BitTar = Bas.AddDays(3),
            GunlukUcret = 100m, KmLimit = 300, FazlaKmUcret = 0.3333m
        });
        await rentals.DeliverAsync(rental, cikisKm: 10000, cikisYakit: 8);
        await rentals.ReturnAsync(rental, donusKm: 10303, donusYakit: 8, Bas.AddDays(3)); // aşım 3 km

        var c = await rentals.GetAsync(rental);
        Assert.Equal(1.00m, c!.FazlaKmBedeli);   // Round(0.9999, 2) = 1.00
        Assert.Equal(301.00m, c.GenelToplam);    // 300 + 1.00 — fatura brütüyle birebir
    }

    [Fact]
    public async Task Olmayan_teslim_alan_personel_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (rental, rentals) = await TeslimliKiraAsync(scope.ServiceProvider, "34 DA 07");
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => rentals.ReturnAsync(rental, 10200, 8, Bas.AddDays(3), teslimAlanPersonelId: Guid.NewGuid()));
        Assert.Contains("personel bulunamadı", ex.Message);
        // Red sonrası kira hâlâ Kirada (yarım dönüş yok).
        Assert.Equal(RentalStatus.Kirada, (await rentals.GetAsync(rental))!.Durum);
    }
}
