using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.Pricing;
using RentACar.Application.RateMatrices;
using RentACar.Application.RentalRules;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR4b — "Otomatik" tam teklif persist (hediye gün / iskonto / hafta sonu Tutar'a yansır + döküm alanları).
/// BAĞIMSIZ ORACLE: matris Gün3=240; hediye 1 gün → faturalanan 2 × 240 = 480; iskonto %10 → 48; Tutar 432.
/// KURAL A: Tutar net brütü içerir → BaseGross/ReturnMath/fatura yalnız Tutar okur → çift-sayım YOK;
/// KmAsim (create-zamanı tahmin) hiç persist edilmez, aşım parası yalnız dönüşte.
/// </summary>
[Collection("postgres")]
public sealed class TamTeklifPersistTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(5);

    private static async Task<(Guid m, Guid v)> SeedAsync(
        IServiceProvider sp, string plaka, int? hediyeGun = null, decimal? iskonto = null, decimal? hsOran = null)
    {
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka, Grup = "B" });
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "TT", Soyad = "M" });
        await sp.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
        {
            Kod = "TT-B", Ad = "TT B", AracGrupKod = "B", ParaBirimi = "TRY",
            Gun1 = 300m, Gun2 = 280m, Gun3 = 240m, OnayDurumu = TarifeOnayDurumu.Onayli, Onaylayan = "t"
        });
        if (hediyeGun is not null || iskonto is not null || hsOran is not null)
            await sp.GetRequiredService<RentalRuleService>().CreateAsync(new RentalRuleInput
            { Kod = "TT-R", Ad = "TT Kural", AracGrupKod = "B", HediyeGun = hediyeGun, Iskonto = iskonto, HaftaSonuFarkOran = hsOran });
        return (m, v);
    }

    private static BookingInput Otomatik(Guid m, Guid v, int gun) => new()
    { MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(gun), FiyatTuru = "Otomatik", KmLimit = 300, FazlaKmUcret = 2m };

    [Fact]
    public async Task Otomatik_kuralsiz_bilesen_yok_tutar_baz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (m, v) = await SeedAsync(sp, "34 TT 01"); // kural yok
        var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(Otomatik(m, v, 3));
        var c = await sp.GetRequiredService<RentalService>().GetAsync(id);
        Assert.Equal(720m, c!.Tutar);       // 3 × 240
        Assert.Null(c.HediyeGun);           // kural yok → bileşen null
        Assert.Null(c.IskontoTutar);
        Assert.Null(c.HaftaSonuFark);
    }

    [Fact]
    public async Task Otomatik_hediye_ve_iskonto_tutara_yansir_dokum_dolu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (m, v) = await SeedAsync(sp, "34 TT 02", hediyeGun: 1, iskonto: 10m);
        var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(Otomatik(m, v, 3));
        var c = await sp.GetRequiredService<RentalService>().GetAsync(id);
        // faturalanan 2 gün × 240 = 480; iskonto %10 → 48; Tutar = 432 (elle oracle).
        Assert.Equal(432m, c!.Tutar);
        Assert.Equal(1, c.HediyeGun);
        Assert.Equal(2, c.FaturalananGun);
        Assert.Equal(48m, c.IskontoTutar);
        Assert.Equal(432m, c.GenelToplam);  // Tutar = GenelToplam (ek hizmet/dönüş yok)
    }

    [Fact]
    public async Task Iskontolu_kira_fatura_defterle_birebir_cift_sayim_yok()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (m, v) = await SeedAsync(sp, "34 TT 03", hediyeGun: 1, iskonto: 10m);
        var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(Otomatik(m, v, 3));
        // Fatura kes → defter brütü sözleşme Tutar'ı (432) ile birebir; iskonto ÇİFT sayılmaz.
        await sp.GetRequiredService<InvoiceService>().CreateFromRentalAsync(id);
        // Cari borç = 432 (iskonto zaten Tutar'da; ayrı satır/çifte yok).
        Assert.Equal(432m, await sp.GetRequiredService<CashService>().GetCariBalanceAsync(m));
    }

    [Fact]
    public async Task KuralA_iskonto_create_km_asim_donuste_ayri()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();
        var (m, v) = await SeedAsync(sp, "34 TT 04", iskonto: 10m); // hediye yok, iskonto %10
        var id = await rentals.CreateDirectAsync(Otomatik(m, v, 3)); // 3×240=720, iskonto 72 → Tutar 648
        var c0 = await rentals.GetAsync(id);
        Assert.Equal(648m, c0!.Tutar);

        // Dönüş: limit 300, kat edilen 500 → fazla 200 × 2 = 400 (dönüş-zamanı, create'teki tahminle ilgisiz).
        await rentals.DeliverAsync(id, cikisKm: 1000, cikisYakit: 8);
        await rentals.ReturnAsync(id, donusKm: 1500, donusYakit: 8, Bas.AddDays(3));
        var c = await rentals.GetAsync(id);
        Assert.Equal(400m, c!.FazlaKmBedeli);           // dönüş-zamanı gerçek aşım
        Assert.Equal(648m + 400m, c.GenelToplam);       // iskontolu baz (648) + gerçek fazla (400) — çift-sayım yok
    }

    [Fact]
    public async Task HaftaSonu_farki_tutara_yansir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        // Cumartesi başlat: 3 günde en az 1 hafta sonu günü olsun (Cmt/Pzr). %50 fark.
        var cmt = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(5);
        while (cmt.DayOfWeek != DayOfWeek.Saturday) cmt = cmt.AddDays(1);
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 TT 05", Grup = "B" });
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "HS", Soyad = "M" });
        await sp.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
        { Kod = "HS-B", Ad = "HS", AracGrupKod = "B", ParaBirimi = "TRY", Gun1 = 100m, Gun2 = 100m, Gun3 = 100m, OnayDurumu = TarifeOnayDurumu.Onayli, Onaylayan = "t" });
        await sp.GetRequiredService<RentalRuleService>().CreateAsync(new RentalRuleInput
        { Kod = "HS-R", Ad = "HS", AracGrupKod = "B", HaftaSonuFarkOran = 50m });

        var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = m, VehicleId = v, BasTar = cmt.AddHours(9), BitTar = cmt.AddDays(3).AddHours(9), FiyatTuru = "Otomatik" });
        var c = await sp.GetRequiredService<RentalService>().GetAsync(id);
        Assert.NotNull(c!.HaftaSonuFark);
        Assert.True(c.HaftaSonuFark > 0m);                   // hafta sonu farkı hesaplandı
        Assert.Equal(300m + c.HaftaSonuFark, c.Tutar);       // baz 300 + hafta sonu farkı (iskonto yok)
    }
}
