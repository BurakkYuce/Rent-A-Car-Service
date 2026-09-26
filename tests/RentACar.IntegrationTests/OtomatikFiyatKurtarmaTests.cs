using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Pricing;
using RentACar.Application.RateMatrices;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// "Otomatik" fiyat türü artık tarife YOKKEN hata vermek yerine, kullanıcının girdiği günlük ücreti
/// NET kabul edip üzerine KDV ekler ("fiyat + KDV").
///
/// <para><b>Neden:</b> eski davranış önce manuel ücreti siliyor (tarife tek gerçek kaynak olsun diye),
/// sonra tarife bulamayınca "manuel fiyat girin" diye reddediyordu — kullanıcı fiyatı ZATEN yazmış
/// olsa bile. Tarife tanımlamamış bir tenant'ta bu, her kirada karşılaşılan bir duvar demekti.</para>
///
/// <para><b>Korunanlar:</b> tarife varsa hâlâ tarife kazanır (kurtarma yalnız tarife yokken);
/// kampanya kodu manuel kurtarmada sessizce yutulmaz (gürültülü red); ne tarife ne ücret varsa
/// hâlâ temiz red (sessiz 0 TL sözleşme oluşmaz).</para>
///
/// Bağımsız oracle elle: 1.000 net @ %20 → 1.200 brüt/gün, 3 gün → 3.600.
/// </summary>
[Collection("postgres")]
public sealed class OtomatikFiyatKurtarmaTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(5);

    private static async Task<(Guid m, Guid v)> SeedAsync(IServiceProvider sp, string plate, string? group = null)
    {
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(
            new VehicleInput { Plaka = plate, Grup = group, GrupBilincliBos = group is null });
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "Oto", Soyad = "Kurtarma" });
        return (m, v);
    }

    [Fact]
    public async Task Tarife_yokken_Otomatik_HATA_VERMEZ_girilen_ucrete_KDV_ekler()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (m, v) = await SeedAsync(sp, "34 OT 01");

        var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = m, VehicleId = v, BasTar = Start, BitTar = Start.AddDays(3),
            GunlukUcret = 1000m, FiyatTuru = "Otomatik",
        });

        var c = (await sp.GetRequiredService<RentalService>().GetAsync(id))!;
        Assert.Equal(1200m, c.GunlukUcret);   // 1.000 × 1,20 (elle)
        Assert.Equal(3600m, c.Tutar);         // 3 × 1.200 (elle)
        // Kullanıcının seçtiği mod kayda AYNEN geçer — kurtarma bir fiyatlama kararı, kayıt değil.
        Assert.Equal("Otomatik", c.FiyatTuru);
        Assert.Equal(0.20m, c.KdvOranSnapshot);
    }

    [Fact]
    public async Task Ucret_de_yoksa_HALA_temiz_red_sessiz_sifir_TL_sozlesme_olusmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (m, v) = await SeedAsync(sp, "34 OT 02");

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
            {
                MusteriId = m, VehicleId = v, BasTar = Start, BitTar = Start.AddDays(3),
                GunlukUcret = 0m, FiyatTuru = "Otomatik",
            }));
        Assert.Contains("Otomatik tarife bulunamadı", ex.Message);
    }

    [Fact]
    public async Task Kampanya_kodu_manuel_kurtarmada_SESSIZCE_yutulmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (m, v) = await SeedAsync(sp, "34 OT 03");

        // Kod + ücret var ama tarife yok: kurtarma kodu UYGULAYAMAZ → indirimsiz fiyatla sessizce
        // kaydetmek "kod geçti" yanılsaması üretirdi.
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
            {
                MusteriId = m, VehicleId = v, BasTar = Start, BitTar = Start.AddDays(3),
                GunlukUcret = 1000m, FiyatTuru = "Otomatik", KampanyaKodu = "YAZ25",
            }));
        Assert.Contains("Kampanya kodu", ex.Message);
    }

    [Fact]
    public async Task Tarife_VARSA_tarife_kazanir_girilen_ucret_yok_sayilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (m, v) = await SeedAsync(sp, "34 OT 04", group: "EKO");

        // Onaylı tarife: günlük 500 TRY. Kullanıcı 9.999 yazsa bile Otomatik'te tarife kazanmalı.
        await sp.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
        {
            Kod = "OT-TARIFE", Ad = "Oto tarife", AracGrupKod = "EKO", ParaBirimi = "TRY",
            Gun1 = 500m, Gun2 = 500m, Gun3 = 500m, Gun4 = 500m, Gun5 = 500m,
            OnayDurumu = TariffApprovalStatus.Onayli, Onaylayan = "t",
        });

        var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = m, VehicleId = v, BasTar = Start, BitTar = Start.AddDays(3),
            GunlukUcret = 9999m, FiyatTuru = "Otomatik",
        });

        var c = (await sp.GetRequiredService<RentalService>().GetAsync(id))!;
        Assert.Equal(500m, c.GunlukUcret);    // tarifeden — 9.999 yok sayıldı
        Assert.Equal(1500m, c.Tutar);         // 3 × 500 (elle)
    }

    [Fact]
    public async Task Onizleme_kayitla_AYNI_tutari_verir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (m, v) = await SeedAsync(sp, "34 OT 05");

        // Canlı hesap ucu (mega-formun sağ paneli) eskiden burada ok:false + hata mesajı dönüyordu.
        var preview = await sp.GetRequiredService<RentalCalculationService>().CalculateAsync(new KiraHesapIstek(
            VehicleId: v, BasTar: Start, BitTar: Start.AddDays(3), GunlukUcret: 1000m,
            FiyatTuru: "Otomatik", Doviz: "TL", CikisOfisi: null, EkHizmetler: [], MusteriId: m));

        Assert.True(preview.Ok);
        Assert.Equal(3600m, preview.GenelToplam);
        Assert.Equal(3000m, preview.Net);
        Assert.Equal(600m, preview.Kdv);

        var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = m, VehicleId = v, BasTar = Start, BitTar = Start.AddDays(3),
            GunlukUcret = 1000m, FiyatTuru = "Otomatik",
        });
        Assert.Equal(preview.GenelToplam, (await sp.GetRequiredService<RentalService>().GetAsync(id))!.Tutar);
    }
}
