using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Fleet;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Araç Güncel Durum birleşik grid — bağımsız oracle. Araç + aktif kira + müşteri birleşimi,
/// filtreler (FiloDurum/Grup/arama/KiradaMi) ve tenant izolasyonu.
/// </summary>
[Collection("postgres")]
public sealed class FleetStatusTests(PostgresFixture fx)
{
    private static async Task SeedAsync(IServiceScope scope)
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();

        var musteri = new Customer { Tip = CustomerType.Bireysel, Ad = "Ali", Soyad = "Veli" };
        db.Customers.Add(musteri);

        // Araç A: kirada (Havuz, EKO) → aktif kira müşteri Ali Veli, bakiye 150.
        var a = new Vehicle { Plaka = "34AAA01", Marka = "Fiat", Grup = "EKO",
            Durum = VehicleStatus.Kirada, FiloDurum = FleetLifecycleStatus.Havuz, Km = 1000 };
        // Araç B: boşta (SifirKmStok, SUV) → aktif kira yok.
        var b = new Vehicle { Plaka = "34BBB02", Marka = "Renault", Grup = "SUV",
            Durum = VehicleStatus.Musait, FiloDurum = FleetLifecycleStatus.SifirKmStok, Km = 5 };
        db.Vehicles.Add(a);
        db.Vehicles.Add(b);

        db.Rentals.Add(new RentalContract
        {
            SozlesmeNo = "K-1", VehicleId = a.Id, MusteriId = musteri.Id, Durum = RentalStatus.Kirada,
            BasTar = DateTimeOffset.UtcNow.AddDays(-1), BitTar = DateTimeOffset.UtcNow.AddDays(3), Bakiye = 150m
        });
        // Tamamlanmış (eski) kira → AKTİF sayılmamalı.
        db.Rentals.Add(new RentalContract
        {
            SozlesmeNo = "K-0", VehicleId = b.Id, MusteriId = musteri.Id, Durum = RentalStatus.Tamamlandi,
            BasTar = DateTimeOffset.UtcNow.AddDays(-10), BitTar = DateTimeOffset.UtcNow.AddDays(-8)
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Joins_active_rental_and_lists_all_vehicles()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedAsync(scope);
        var svc = scope.ServiceProvider.GetRequiredService<FleetStatusService>();

        var rows = await svc.QueryAsync(new FleetStatusFilter());
        Assert.Equal(2, rows.Count);

        var a = rows.Single(r => r.Plaka == "34AAA01");
        Assert.True(a.Kirada);
        Assert.Equal("Ali Veli", a.MusteriAd);
        Assert.Equal(150m, a.KiraBakiye);
        Assert.Equal("K-1", a.KiraSozlesmeNo);

        var b = rows.Single(r => r.Plaka == "34BBB02");
        Assert.False(b.Kirada);            // tamamlanmış kira aktif değil
        Assert.Null(b.MusteriAd);
    }

    [Fact]
    public async Task Filters_by_filo_status_grup_and_query()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedAsync(scope);
        var svc = scope.ServiceProvider.GetRequiredService<FleetStatusService>();

        var havuz = await svc.QueryAsync(new FleetStatusFilter { FiloDurum = FleetLifecycleStatus.Havuz });
        Assert.Single(havuz);
        Assert.Equal("34AAA01", havuz[0].Plaka);

        var suv = await svc.QueryAsync(new FleetStatusFilter { Grup = "SUV" });
        Assert.Single(suv);
        Assert.Equal("34BBB02", suv[0].Plaka);

        var ara = await svc.QueryAsync(new FleetStatusFilter { Query = "BBB" });
        Assert.Single(ara);
        Assert.Equal("34BBB02", ara[0].Plaka);
    }

    [Fact]
    public async Task KiradaMi_filter_splits_rented_and_idle()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedAsync(scope);
        var svc = scope.ServiceProvider.GetRequiredService<FleetStatusService>();

        var kirada = await svc.QueryAsync(new FleetStatusFilter { KiradaMi = true });
        Assert.Single(kirada);
        Assert.Equal("34AAA01", kirada[0].Plaka);

        var bosta = await svc.QueryAsync(new FleetStatusFilter { KiradaMi = false });
        Assert.Single(bosta);
        Assert.Equal("34BBB02", bosta[0].Plaka);
    }

    // ---- FAZ-11: aksiyon konsolu kolonları + araç künyesi filtreleri ----

    /// <summary>
    /// FAZ-11 bağımsız oracle: 3 araç kurulur, YALNIZ BİRİNİN pasif sebebi "Kaza"dır →
    /// filtre uygulanınca beklenen sonuç sabit 1'dir (servis kodundan türetilmedi).
    /// </summary>
    [Fact]
    public async Task Filters_by_pasif_sebep()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Vehicles.Add(new Vehicle { Plaka = "34PAS01", Durum = VehicleStatus.Pasif, PasifSebep = "Kaza" });
            db.Vehicles.Add(new Vehicle { Plaka = "34PAS02", Durum = VehicleStatus.Pasif, PasifSebep = "Hurda" });
            db.Vehicles.Add(new Vehicle { Plaka = "34PAS03", Durum = VehicleStatus.Musait });
            await db.SaveChangesAsync();
        }
        var svc = scope.ServiceProvider.GetRequiredService<FleetStatusService>();

        var kaza = await svc.QueryAsync(new FleetStatusFilter { PasifSebep = "Kaza" });
        Assert.Single(kaza);
        Assert.Equal("34PAS01", kaza[0].Plaka);

        // Filtresiz: üçü de görünür (filtre kalıcı bir daraltma yapmıyor).
        Assert.Equal(3, (await svc.QueryAsync(new FleetStatusFilter())).Count);
    }

    /// <summary>Araç künyesi bayrak/no filtreleri: kar lastiği, web rez. kapalı, GPS ve HGS no.</summary>
    [Fact]
    public async Task Filters_by_flags_gps_and_hgs()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Vehicles.Add(new Vehicle { Plaka = "34BAY01", KarLastigi = true, WebRezKapat = true, TakipNo = "GPS-777", HgsNo = "HGS-100" });
            db.Vehicles.Add(new Vehicle { Plaka = "34BAY02", KarLastigi = false, WebRezKapat = false, TakipNo = "GPS-888", OgsNo = "OGS-200" });
            db.Vehicles.Add(new Vehicle { Plaka = "34BAY03" });
            await db.SaveChangesAsync();
        }
        var svc = scope.ServiceProvider.GetRequiredService<FleetStatusService>();

        Assert.Equal("34BAY01", Assert.Single(await svc.QueryAsync(new FleetStatusFilter { KarLastigi = true })).Plaka);
        Assert.Equal("34BAY01", Assert.Single(await svc.QueryAsync(new FleetStatusFilter { WebRezKapat = true })).Plaka);
        Assert.Equal("34BAY02", Assert.Single(await svc.QueryAsync(new FleetStatusFilter { TakipNo = "888" })).Plaka);
        Assert.Equal("34BAY01", Assert.Single(await svc.QueryAsync(new FleetStatusFilter { HgsNo = "HGS-100" })).Plaka);
        // OGS etiketi de aynı kutudan bulunur (elindeki etiketin hangisi olduğunu bilmeyen operatör).
        Assert.Equal("34BAY02", Assert.Single(await svc.QueryAsync(new FleetStatusFilter { HgsNo = "OGS-200" })).Plaka);
    }

    /// <summary>
    /// Satır özetleri: kira müşterisinin telefonu, sıradaki rezervasyon, açık servis, açık BAF ve
    /// aktif filo kiralama dosya no. Her biri AYRI araca kurulur → hangi kolonun hangi kaynaktan
    /// geldiği tek tek doğrulanır.
    /// </summary>
    [Fact]
    public async Task Row_carries_reservation_service_baf_and_dosya_no()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var kiraci = new Customer { Tip = CustomerType.Bireysel, Ad = "Ali", Soyad = "Veli", CepTel = "0555 111 22 33" };
            var rezci = new Customer { Tip = CustomerType.Bireysel, Ad = "Ayşe", Soyad = "Kaya" };
            db.Customers.AddRange(kiraci, rezci);

            var a = new Vehicle { Plaka = "34OZT01", Durum = VehicleStatus.Kirada };
            var b = new Vehicle { Plaka = "34OZT02" };
            var c = new Vehicle { Plaka = "34OZT03", Durum = VehicleStatus.Serviste };
            var d = new Vehicle { Plaka = "34OZT04" };
            var e = new Vehicle { Plaka = "34OZT05" };
            db.Vehicles.AddRange(a, b, c, d, e);

            var pers = new Personel { Kod = "P-77", Ad = "Mehmet", Soyad = "Demir" };
            db.Personeller.Add(pers);

            db.Rentals.Add(new RentalContract
            {
                SozlesmeNo = "K-77", VehicleId = a.Id, MusteriId = kiraci.Id, Durum = RentalStatus.Kirada,
                BasTar = DateTimeOffset.UtcNow.AddDays(-1), BitTar = DateTimeOffset.UtcNow.AddDays(3), Bakiye = 150m
            });
            db.Reservations.Add(new Reservation
            {
                ReservationNo = "RZ-77", VehicleId = b.Id, MusteriId = rezci.Id, Durum = ReservationStatus.Onayli,
                BasTar = DateTimeOffset.UtcNow.AddDays(5), BitTar = DateTimeOffset.UtcNow.AddDays(8)
            });
            // GEÇMİŞTE bitmiş rezervasyon: "sıradaki" değildir → satıra çıkmamalı.
            db.Reservations.Add(new Reservation
            {
                ReservationNo = "RZ-00", VehicleId = e.Id, MusteriId = rezci.Id, Durum = ReservationStatus.Onayli,
                BasTar = DateTimeOffset.UtcNow.AddDays(-9), BitTar = DateTimeOffset.UtcNow.AddDays(-7)
            });
            db.ServiceRecords.Add(new ServiceRecord
            {
                No = "SV-77", VehicleId = c.Id, Durum = ServiceStatus.Serviste, AtolyeAdi = "Merkez Atölye"
            });
            db.Baflar.Add(new Baf { No = "BAF-77", VehicleId = d.Id, PersonelId = pers.Id, Durum = BafStatus.Acik });
            db.FiloKiralamalar.Add(new FiloKiralama
            {
                No = "FK-77", VehicleId = e.Id, MusteriId = kiraci.Id, Durum = FleetRentalStatus.Aktif,
                DosyaNo = "DSY-2026-9", SureAy = 12, AylikUcret = 1000m
            });
            await db.SaveChangesAsync();
        }

        var rows = await scope.ServiceProvider.GetRequiredService<FleetStatusService>()
            .QueryAsync(new FleetStatusFilter());

        var a1 = rows.Single(r => r.Plaka == "34OZT01");
        Assert.Equal("Ali Veli", a1.MusteriAd);
        Assert.Equal("0555 111 22 33", a1.MusteriTel);
        Assert.NotNull(a1.KiraKalanGun);          // değeri KalanGun saf testinde çivilenir

        var b1 = rows.Single(r => r.Plaka == "34OZT02");
        Assert.Equal("Ayşe Kaya", b1.RezMusteriAd);
        Assert.NotNull(b1.RezBasTar);

        var c1 = rows.Single(r => r.Plaka == "34OZT03");
        Assert.Equal("SV-77", c1.AcikServisNo);
        Assert.Equal("Merkez Atölye", c1.ServisAtolye);
        Assert.True(c1.Serviste);

        var d1 = rows.Single(r => r.Plaka == "34OZT04");
        Assert.Equal("BAF-77", d1.AktifBafNo);
        Assert.Equal("Mehmet Demir", d1.BafPersonelAd);
        Assert.True(d1.Bafta);

        var e1 = rows.Single(r => r.Plaka == "34OZT05");
        Assert.Equal("DSY-2026-9", e1.DosyaNo);
        Assert.Null(e1.RezMusteriAd);             // bitmiş rezervasyon sıradaki sayılmaz
    }

    /// <summary>
    /// "Kira Kalan" saf hesabı — sabit tarihlerle (saatten bağımsız TAKVİM GÜNÜ farkı).
    /// Oracle elle: 7 Ocak → 10 Ocak arası 3 gündür.
    /// </summary>
    [Theory]
    [InlineData("2026-01-07T23:30:00Z", "2026-01-10T00:30:00Z", 3)]
    [InlineData("2026-01-10T08:00:00Z", "2026-01-10T20:00:00Z", 0)]
    [InlineData("2026-01-12T08:00:00Z", "2026-01-10T20:00:00Z", -2)]
    public void KalanGun_takvim_gunu_farkidir(string simdi, string bitis, int beklenen)
        => Assert.Equal(beklenen, FleetStatusRow.RemainingDays(
            DateTimeOffset.Parse(bitis, System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(simdi, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public async Task Tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
            await SeedAsync(s1);

        using var s2 = host.ScopeFor(Guid.NewGuid());
        var rows = await s2.ServiceProvider.GetRequiredService<FleetStatusService>()
            .QueryAsync(new FleetStatusFilter());
        Assert.Empty(rows);
    }
}
