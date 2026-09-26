using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.AracKredileri;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Regulation;
using RentACar.Application.VehicleSales;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-28 — Detaylı araç listesi (konsolide grid).
///
/// <para><b>Bu fazın en önemli kuralı:</b> aktif kira bilgisi araçta DEPOLANMAZ, her istekte kira
/// tablosundan CANLI çözülür. Aracın üzerindeki <c>Kiralayan</c>/<c>KiraBitTar</c> alanları ELLE
/// girilen notlardır ve kaynak değildir. Aşağıdaki test ikisini bilerek ÇELİŞTİRİR: not "Eski
/// Kiracı" derken gerçek kira başka bir müşteriye aittir — liste gerçek olanı göstermelidir.</para>
///
/// <para>Bağımsız oracle: alan değerleri ve JOIN sonuçları testte elle kurulur; en-yakın muayene /
/// tipe göre sigorta gibi seçimler elle bilinen tarihlerle karşılaştırılır.</para>
/// </summary>
[Collection("postgres")]
public sealed class AracDetayListesiTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset T0 = TestZaman.Now();

    [Fact]
    public async Task Yeni_detay_alanlari_round_trip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<VehicleService>();

        var id = await svc.CreateAsync(new VehicleInput
        {
            Plaka = "34 DT 01",
            BelgeNo = "BLG-1", RuhsatSahibi = "Yüce Filo A.Ş.", SozNo = "SZ-9", AraciAlan = "Ahmet",
            OdemeSekli = "Kredi", AssistanFirma = "Yol Yardım A.Ş.", HgsFirma = "PTT",
            DisKmLimit = 2500, TsbKodu = "TSB-77", TsbKaskoDegeri = 850000m,
            AlisEuro = true, AlisEuroFiyat = 21000m, SatisEuroFiyat = 18000m,
            PasifSebep = "Kaza", SonDurum = "Serviste bekliyor",
            SonTeslimKm = 45000, SonTeslimTarihi = T0.AddDays(-3),
            Kiralayan = "Not: Beta Ltd.", KiraGun = 7, KiraFiyat = 1200m,
            KiraBitTar = T0.AddDays(4), KiraBekTar = T0.AddDays(3)
        });

        var v = await svc.GetAsync(id);
        Assert.NotNull(v);
        Assert.Equal("BLG-1", v!.BelgeNo);
        Assert.Equal("Yüce Filo A.Ş.", v.RuhsatSahibi);
        Assert.Equal("SZ-9", v.SozNo);
        Assert.Equal("Ahmet", v.AraciAlan);
        Assert.Equal("Kredi", v.OdemeSekli);
        Assert.Equal("Yol Yardım A.Ş.", v.AssistanFirma);
        Assert.Equal("PTT", v.HgsFirma);
        Assert.Equal(2500, v.DisKmLimit);
        Assert.Equal("TSB-77", v.TsbKodu);
        Assert.Equal(850000m, v.TsbKaskoDegeri);
        Assert.True(v.AlisEuro);
        Assert.Equal(21000m, v.AlisEuroFiyat);
        Assert.Equal(18000m, v.SatisEuroFiyat);
        Assert.Equal("Kaza", v.PasifSebep);
        Assert.Equal("Serviste bekliyor", v.SonDurum);
        Assert.Equal(45000, v.SonTeslimKm);
        Assert.Equal(7, v.KiraGun);
        Assert.Equal(1200m, v.KiraFiyat);
    }

    [Fact]
    public async Task Kredi_muayene_ve_TIPE_GORE_sigorta_dogru_cozulur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<VehicleService>();
        var vehicle = await svc.CreateAsync(new VehicleInput { Plaka = "34 DT 10" });

        await sp.GetRequiredService<VehicleLoanService>().CreateAsync(new AracKrediInput
        { VehicleId = vehicle, BankaAdi = "X Bankası", KrediTutari = 100000m, TaksitSayisi = 12 });

        var reg = sp.GetRequiredService<RegulationService>();
        // İKİ muayene: eskisi ve yenisi. Listede YÜRÜRLÜKTEKİ (en geç biten) görünmeli.
        await reg.AddInspectionAsync(vehicle, T0.AddDays(-400), T0.AddDays(-35), 500m);
        await reg.AddInspectionAsync(vehicle, T0.AddDays(-30), T0.AddDays(330), 600m);
        // Kasko ve Trafik AYRI poliçeler — tek kolonda karışmamalı.
        await reg.AddInsuranceAsync(vehicle, InsuranceType.Kasko, T0.AddDays(-10), T0.AddDays(355), 9000m, "K1", "Sig", null);
        await reg.AddInsuranceAsync(vehicle, InsuranceType.Trafik, T0.AddDays(-10), T0.AddDays(200), 3000m, "T1", "Sig", null);

        var row = Assert.Single(await svc.ListDetailAsync());
        Assert.Equal("X Bankası", row.KrediBanka);
        Assert.Equal(T0.AddDays(330), row.MuayeneBitis);   // en geç biten
        Assert.Equal(T0.AddDays(355), row.KaskoBitis);
        Assert.Equal(T0.AddDays(200), row.TrafikBitis);
    }

    [Fact]
    public async Task AKTIF_KIRA_canli_cozulur_araçtaki_NOT_ile_celisse_bile()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<VehicleService>();

        // Araca BİLEREK yanlış bir "Kiralayan" notu yazılıyor.
        var vehicle = await svc.CreateAsync(new VehicleInput
        { Plaka = "34 DT 20", Kiralayan = "Eski Kiracı (not)", KiraBitTar = T0.AddDays(99) });

        var realCustomer = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Kurumsal, Unvan = "Gerçek Kiracı A.Ş." });
        var rental = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = realCustomer, VehicleId = vehicle,
            BasTar = T0.AddDays(-2), BitTar = T0.AddDays(3), GunlukUcret = 1000m
        });

        var row = Assert.Single(await svc.ListDetailAsync());
        // Liste GERÇEK kirayı göstermeli — nottaki değeri değil.
        Assert.Equal("Gerçek Kiracı A.Ş.", row.AktifKiraMusteri);
        Assert.Equal(T0.AddDays(3), row.AktifKiraBitis);
        Assert.False(string.IsNullOrWhiteSpace(row.AktifKiraSozlesmeNo));
        // Not alanı olduğu gibi duruyor (silinmiyor) ama kaynak DEĞİL.
        Assert.Equal("Eski Kiracı (not)", row.Arac.Kiralayan);
        Assert.NotEqual(row.Arac.KiraBitTar, row.AktifKiraBitis);

        // Kira kapanınca aktif kira alanları BOŞALMALI (depolanmadığının kanıtı).
        await sp.GetRequiredService<RentalService>().CancelAsync(rental);
        var after = Assert.Single(await svc.ListDetailAsync());
        Assert.Null(after.AktifKiraMusteri);
        Assert.Null(after.AktifKiraBitis);
        Assert.Equal("Eski Kiracı (not)", after.Arac.Kiralayan);   // not hâlâ duruyor
    }

    [Fact]
    public async Task Satis_ihale_bilgileri_listede_gorunur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<VehicleService>();
        var vehicle = await svc.CreateAsync(new VehicleInput { Plaka = "34 DT 30" });
        var recipient = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Kurumsal, Unvan = "Alıcı A.Ş." });

        await sp.GetRequiredService<VehicleSaleService>().CreateAsync(new VehicleSaleInput
        {
            VehicleId = vehicle, AliciCariId = recipient, SatisNet = 500000m, KdvOrani = 0.20m,
            HedefFiyat = 550000m,
            IhaleTarihi = T0.AddDays(-5), IhaleFirmasi = "Oto İhale A.Ş.", NoterSatisTarihi = T0.AddDays(-1)
        });

        var row = Assert.Single(await svc.ListDetailAsync());
        Assert.Equal(550000m, row.SatisHedefFiyat);
        Assert.Equal(T0.AddDays(-5), row.IhaleTarihi);
        Assert.Equal("Oto İhale A.Ş.", row.IhaleFirmasi);
        Assert.Equal(T0.AddDays(-1), row.NoterSatisTarihi);
    }

    [Fact]
    public async Task Filtreler_ELLE_BEKLENEN_alt_kumeyi_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<VehicleService>();

        await svc.CreateAsync(new VehicleInput
        { Plaka = "34 AL 01", Marka = "Fiat", Sube = "Merkez", BelgeNo = "BLG-A", Durum = VehicleStatus.Musait });
        await svc.CreateAsync(new VehicleInput
        { Plaka = "06 BT 02", Marka = "Renault", Sube = "Şube2", BelgeNo = "BLG-B", Durum = VehicleStatus.Serviste });
        await svc.CreateAsync(new VehicleInput
        { Plaka = "35 CC 03", Marka = "Fiat", Sube = "Merkez", Durum = VehicleStatus.Musait });

        Assert.Equal(3, (await svc.ListDetailAsync()).Count);
        Assert.Equal(3, (await svc.ListDetailAsync(new VehicleDetayFilter())).Count);

        // Plaka BOŞLUKLU girişle de bulunmalı (DB'de normalize).
        Assert.Equal("34AL01", Assert.Single(await svc.ListDetailAsync(
            new VehicleDetayFilter { Ara = "34 AL" })).Arac.Plaka);
        // Marka / belge no
        Assert.Equal(2, (await svc.ListDetailAsync(new VehicleDetayFilter { Ara = "fiat" })).Count);
        Assert.Single(await svc.ListDetailAsync(new VehicleDetayFilter { Ara = "BLG-B" }));

        // Şube + durum
        Assert.Equal(2, (await svc.ListDetailAsync(new VehicleDetayFilter { Sube = "Merkez" })).Count);
        Assert.Single(await svc.ListDetailAsync(new VehicleDetayFilter { Durum = VehicleStatus.Serviste }));
        Assert.Empty(await svc.ListDetailAsync(new VehicleDetayFilter { Sube = "YokBoyle" }));
    }

    [Fact]
    public async Task Detay_listesi_tenant_izolasyonlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
            await s1.ServiceProvider.GetRequiredService<VehicleService>()
                .CreateAsync(new VehicleInput { Plaka = "34 GZ 01", BelgeNo = "GIZLI" });

        using var s2 = host.ScopeFor(Guid.NewGuid());
        var svc = s2.ServiceProvider.GetRequiredService<VehicleService>();
        Assert.Empty(await svc.ListDetailAsync());
        Assert.Empty(await svc.ListDetailAsync(new VehicleDetayFilter { Ara = "GIZLI" }));
    }
}
