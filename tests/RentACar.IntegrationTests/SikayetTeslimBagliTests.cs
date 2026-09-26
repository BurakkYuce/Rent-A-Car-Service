using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Crm;
using RentACar.Application.Customers;
using RentACar.Application.Personnel;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-43 — Şikayetin teslim/dönüş sürecine bağlanması + filtreli liste.
///
/// <para><b>Plaka SNAPSHOT DEĞİL:</b> şikayette plaka kolonu yok; sözleşme→araç bağından her
/// istekte okunuyor. Aşağıdaki test bunu kanıtlıyor — sözleşmenin aracı değiştirilince şikayet
/// listesindeki plaka da değişiyor. Snapshot alsaydık liste zamanla yalan söylerdi.</para>
///
/// <para><b>Sözleşmesiz şikayet DÜŞMEZ:</b> mevcut genel şikayet-bileti davranışı korunuyor
/// (LEFT JOIN); yeni alanlar tamamen opsiyonel.</para>
///
/// <para>Bağımsız oracle: alanlar ve beklenen alt kümeler testte elle yazılır.</para>
/// </summary>
[Collection("postgres")]
public sealed class SikayetTeslimBagliTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset T0 = TestZaman.Simdi().AddDays(-5);

    private static async Task<Guid> CariAsync(IServiceProvider sp, string unvan, string? tel = null)
        => await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Kurumsal, Unvan = unvan, CepTel = tel });

    private static async Task<(Guid kira, Guid arac)> KiraAsync(
        IServiceProvider sp, Guid cari, string plaka, string? ofis = null)
    {
        var arac = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka });
        var kira = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = cari, VehicleId = arac, BasTar = T0, BitTar = T0.AddDays(3),
            GunlukUcret = 1000m, CikisOfisi = ofis
        });
        return (kira, arac);
    }

    [Fact]
    public async Task Yeni_alanlar_round_trip_ve_sozlesme_bilgisi_COZULUR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var cari = await CariAsync(sp, "Alfa A.Ş.", "05551112233");
        var (kira, _) = await KiraAsync(sp, cari, "34 SK 01", "Merkez Ofis");
        var personel = sp.GetRequiredService<PersonnelService>();
        var alan = await personel.CreateAsync(new PersonelInput { Kod = "P1", Ad = "Ayşe", Soyad = "Yılmaz" });
        var eden = await personel.CreateAsync(new PersonelInput { Kod = "P2", Ad = "Mehmet", Soyad = "Kaya" });

        var svc = sp.GetRequiredService<ComplaintService>();
        var id = await svc.CreateAsync(new SikayetInput
        {
            CariId = cari, Konu = "Araç kirliydi", Detay = "Teslimde temizlik yapılmamış",
            RentalId = kira, TeslimAlanPersonelId = alan, TeslimEdenPersonelId = eden,
            Puan = 2, SikayetKanali = " Telefon ", SikayetYeri = ComplaintLocation.Kira,
            CikisOfisi = " Merkez Ofis "
        });

        var r = Assert.Single(await svc.SearchAsync());
        Assert.Equal(id, r.Sikayet.Id);
        Assert.Equal(kira, r.Sikayet.RentalId);
        Assert.Equal(2, r.Sikayet.Puan);
        Assert.Equal("Telefon", r.Sikayet.SikayetKanali);       // trim
        Assert.Equal(ComplaintLocation.Kira, r.Sikayet.SikayetYeri);
        Assert.Equal("Merkez Ofis", r.Sikayet.CikisOfisi);

        // Sözleşmeden ÇÖZÜLEN alanlar
        Assert.Equal("34SK01", r.Plaka);                        // plaka DB'de normalize
        Assert.False(string.IsNullOrWhiteSpace(r.SozlesmeNo));
        Assert.Equal("Alfa A.Ş.", r.MusteriAd);
        Assert.Equal("05551112233", r.MusteriTel);
        Assert.Equal("Ayşe Yılmaz", r.TeslimAlanAd);
        Assert.Equal("Mehmet Kaya", r.TeslimEdenAd);
    }

    [Fact]
    public async Task Plaka_SNAPSHOT_DEGIL_arac_plakasi_degisince_liste_de_degisir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var cari = await CariAsync(sp, "Beta");
        var (kira, arac) = await KiraAsync(sp, cari, "34 ES 01");
        await sp.GetRequiredService<ComplaintService>().CreateAsync(new SikayetInput
        { CariId = cari, Konu = "Test", RentalId = kira });

        Assert.Equal("34ES01", Assert.Single(await sp.GetRequiredService<ComplaintService>().SearchAsync()).Plaka);

        // Aracın plakası değişirse (devir/yeni tescil) liste de değişmeli — kopya tutulmuyor.
        Assert.True(await sp.GetRequiredService<VehicleService>()
            .UpdateAsync(arac, new VehicleInput { Plaka = "06 YN 02" }));

        Assert.Equal("06YN02", Assert.Single(await sp.GetRequiredService<ComplaintService>().SearchAsync()).Plaka);
    }

    [Fact]
    public async Task Sozlesmesiz_genel_sikayet_ESKI_davranis_korunur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<ComplaintService>();

        // Hiçbir yeni alan verilmeden eski çağrı biçimi çalışmalı.
        await svc.CreateAsync(new SikayetInput { Konu = "Genel şikayet" });

        var r = Assert.Single(await svc.SearchAsync());
        Assert.Null(r.Sikayet.RentalId);
        Assert.Null(r.Plaka);          // sözleşme yok → LEFT JOIN'de DÜŞMEDİ
        Assert.Null(r.SozlesmeNo);
        Assert.Null(r.Sikayet.SikayetYeri);
        Assert.Equal("Genel şikayet", r.Sikayet.Konu);
        Assert.Equal(ComplaintStatus.Acik, r.Sikayet.Durum);
    }

    [Fact]
    public async Task Filtreler_ELLE_BEKLENEN_alt_kumeyi_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<ComplaintService>();
        var a = await CariAsync(sp, "Alfa Lojistik");
        var b = await CariAsync(sp, "Beta Turizm");
        var (kiraA, _) = await KiraAsync(sp, a, "34 FL 01", "Merkez");

        await svc.CreateAsync(new SikayetInput
        { CariId = a, Konu = "Gecikme", RentalId = kiraA, CikisOfisi = "Merkez",
          SikayetKanali = "Telefon", SikayetYeri = ComplaintLocation.Kira });
        await svc.CreateAsync(new SikayetInput
        { CariId = b, Konu = "Yanlış araç", CikisOfisi = "Şube2",
          SikayetKanali = "Web", SikayetYeri = ComplaintLocation.Rezervasyon, Durum = ComplaintStatus.Kapali });
        await svc.CreateAsync(new SikayetInput
        { CariId = b, Konu = "Fiyat farkı", CikisOfisi = "Merkez", SikayetKanali = "Telefon" });

        // ELLE: 3 şikayet.
        Assert.Equal(3, (await svc.SearchAsync()).Count);
        Assert.Equal(3, (await svc.SearchAsync(new SikayetFilter())).Count);

        Assert.Single(await svc.SearchAsync(new SikayetFilter { CariId = a }));
        Assert.Equal(2, (await svc.SearchAsync(new SikayetFilter { CariId = b })).Count);
        Assert.Equal(2, (await svc.SearchAsync(new SikayetFilter { Ofis = "Merkez" })).Count);
        Assert.Equal(2, (await svc.SearchAsync(new SikayetFilter { Kanal = "telefon" })).Count);   // harf duyarsız
        Assert.Single(await svc.SearchAsync(new SikayetFilter { Yer = ComplaintLocation.Rezervasyon }));
        Assert.Single(await svc.SearchAsync(new SikayetFilter { Durum = ComplaintStatus.Kapali }));

        // Metin araması: konu / sözleşme no / plaka (boşluklu giriş)
        Assert.Equal("Gecikme", Assert.Single(await svc.SearchAsync(new SikayetFilter { Ara = "gecik" })).Sikayet.Konu);
        Assert.Equal("Gecikme", Assert.Single(await svc.SearchAsync(new SikayetFilter { Ara = "34 FL" })).Sikayet.Konu);
        Assert.Empty(await svc.SearchAsync(new SikayetFilter { Ara = "yok-boyle-bir-sey" }));

        // Birleşik: Merkez + Telefon → 2
        Assert.Equal(2, (await svc.SearchAsync(new SikayetFilter { Ofis = "Merkez", Kanal = "Telefon" })).Count);
    }

    [Fact]
    public async Task Sikayet_tenant_izolasyonlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
        {
            var sp = s1.ServiceProvider;
            var cari = await CariAsync(sp, "Gizli");
            var (kira, _) = await KiraAsync(sp, cari, "34 GZ 01");
            await sp.GetRequiredService<ComplaintService>().CreateAsync(new SikayetInput
            { CariId = cari, Konu = "Gizli şikayet", RentalId = kira, SikayetKanali = "Telefon" });
        }

        using var s2 = host.ScopeFor(Guid.NewGuid());
        var svc = s2.ServiceProvider.GetRequiredService<ComplaintService>();
        Assert.Empty(await svc.SearchAsync());
        Assert.Empty(await svc.SearchAsync(new SikayetFilter { Kanal = "Telefon" }));
    }
}
