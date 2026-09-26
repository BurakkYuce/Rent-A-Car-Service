using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.BelgeSablon;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Personnel;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR3 — SozlesmeView (sözleşme çıktısının TEK projeksiyonu; HTML-print + QuestPDF aynı modeli tüketir).
/// BAĞIMSIZ ORACLE: kullanılan km = 10.400 − 10.000 = 400 (elle); ehliyet decrypt'li düz gelir.
/// </summary>
[Collection("postgres")]
public sealed class SozlesmeViewTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Start =
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-7).AddHours(9);

    [Fact]
    public async Task Sozlesme_view_tum_alanlar_ve_kullanilan_km()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        var account = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        {
            Tip = CustomerType.Bireysel, Ad = "Deneme", Soyad = "Musteri", CepTel = "05320000000",
            EhliyetNo = "35030", EhliyetSinifi = "B", EhliyetYeri = "BURDUR",
            DogumTarihi = new DateTimeOffset(1975, 4, 15, 0, 0, 0, TimeSpan.Zero)
        });
        var veh = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        { Plaka = "07 BOP 605", Marka = "Fiat", Tip = "Egea", Km = 10000 });
        var pid = await sp.GetRequiredService<PersonnelService>().CreateAsync(new PersonelInput
        { Kod = "P-SZ", Ad = "Onur", Soyad = "Yuce" });

        var rentals = sp.GetRequiredService<RentalService>();
        var rental = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = account, VehicleId = veh, BasTar = Start, BitTar = Start.AddDays(3), GunlukUcret = 100m, KmLimit = 300, FazlaKmUcret = 2m });
        await rentals.DeliverAsync(rental, pickupKm: 10000, pickupFuel: 8);
        await rentals.ReturnAsync(rental, returnKm: 10400, returnFuel: 6, Start.AddDays(3),
            freeKm: 50, endReason: "Normal", receivingStaffId: pid);

        var s = await sp.GetRequiredService<ContractService>().GetAsync(rental);

        Assert.NotNull(s);
        Assert.Equal("Deneme Musteri", s!.MusteriAd);
        Assert.Equal("35030", s.EhliyetNo);          // decrypt'li düz değer
        Assert.Equal("BURDUR", s.EhliyetYeri);
        Assert.Equal("07BOP605", s.Plaka);
        Assert.Equal(10000, s.CikisKm);
        Assert.Equal(10400, s.DonusKm);
        Assert.Equal(400, s.KullanilanKm);           // 10.400 − 10.000 (elle oracle)
        Assert.Equal(50, s.KmHediye);
        Assert.Equal("Normal", s.BitisSebebi);
        Assert.Equal("Onur Yuce", s.TeslimAlanAd);
        // Para dökümü sözleşmedekiyle birebir: fazla = 400−300−50=50 × 2 = 100; toplam 300+100=400.
        Assert.Equal(100m, s.FazlaKmBedeli);
        Assert.Equal(400m, s.GenelToplam);
    }

    [Fact]
    public async Task Sozlesme_view_belge_sablonu_metinlerini_ve_ek_kosul_fallback_tasir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        // Bağımsız oracle: metinleri BURADA kuruyoruz; SozlesmeView aynen taşımalı.
        var templateId = await sp.GetRequiredService<DocumentTemplateService>().CreateAsync(new BelgeSablonInput
        {
            BelgeTuru = BelgeTuru.KiraSozlesmesi, Ad = "Test",
            BelgeBasligi = "TEST-BASLIK", HukukiMetinSol = "TEST-SOL", EkKosullarVarsayilan = "SABLON-EK-KOSUL"
        });

        var account = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "A", Soyad = "B", CepTel = "05320000001" });
        var veh = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        { Plaka = "07 AA 001", Marka = "Fiat", Tip = "Egea", Km = 100 });

        var rentals = sp.GetRequiredService<RentalService>();
        var rental = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = account, VehicleId = veh, BasTar = Start, BitTar = Start.AddDays(2), GunlukUcret = 100m, BelgeSablonId = templateId });

        var s = await sp.GetRequiredService<ContractService>().GetAsync(rental);
        Assert.NotNull(s);
        Assert.Equal("TEST-BASLIK", s!.SablonBaslik);
        Assert.Equal("TEST-SOL", s.SablonHukukiSol);
        Assert.Null(s.SablonHukukiSag);                 // şablonda boş → renderer koddaki sabiti basar
        Assert.Equal("SABLON-EK-KOSUL", s.EkKosullar);  // kira-özel ek koşul yok → şablon varsayılanına düşer
    }

    [Fact]
    public async Task Olmayan_kira_null_doner()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        Assert.Null(await scope.ServiceProvider.GetRequiredService<ContractService>().GetAsync(Guid.NewGuid()));
    }
}
