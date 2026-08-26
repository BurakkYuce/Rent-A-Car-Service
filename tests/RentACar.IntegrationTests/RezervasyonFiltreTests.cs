using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Availability;
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
/// FAZ-48 — rezervasyon arama/filtre barı + talep bilgi alanları, müsaitlik penceresi (gün/saat).
///
/// <para>BAĞIMSIZ ORACLE: beklenen sayılar senaryodan sayılarak yazıldı (ör. "3 rezervasyondan 1'i
/// Onaylı → 1 satır"), servis/repo mantığından türetilmedi. Filtreler hem DARALTTIĞI hem de
/// BOŞKEN DARALTMADIĞI için sınanır (regresyon çiti: bar eklemek eski listeyi kısaltmamalı).</para>
/// </summary>
[Collection("postgres")]
public sealed class RezervasyonFiltreTests(PostgresFixture fx)
{
    // Rezervasyon geçmişe kapalı (TarihPolitikasi) → now-göreli gelecek. TAM SAATE hizalı: CI
    // (Linux, 100ns tick) ile Mac (µs) çözünürlük farkı DB round-trip eşitliğini patlatmasın; ayrıca
    // "gün başı" karşılaştırmaları belirsiz kalmasın (gece yarısında çalışan koşuda flake üretirdi).
    private static readonly DateTimeOffset Bas =
        new(DateTime.UtcNow.Date.AddDays(3).AddHours(9), TimeSpan.Zero);

    private static BookingInput Girdi(Guid cari, Guid arac, DateTimeOffset? bas = null, int gun = 4) => new()
    {
        MusteriId = cari, VehicleId = arac,
        BasTar = bas ?? Bas, BitTar = (bas ?? Bas).AddDays(gun), GunlukUcret = 100m
    };

    // ---------------------------------------------------------------- müsaitlik penceresi (saf)

    [Fact]
    public void Pencere_gun_sayisi_bitis_tarihinin_yerine_gecer()
    {
        // Elle kurulan senaryo: 10 Haziran'dan itibaren 3 gün → 13 Haziran'da biter.
        var p = AvailabilityService.Pencere(new DateOnly(2026, 6, 10), null, 3, null, null);
        Assert.NotNull(p);
        Assert.Equal(new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero), p!.Value.From);
        Assert.Equal(new DateTimeOffset(2026, 6, 13, 0, 0, 0, TimeSpan.Zero), p.Value.To);
    }

    [Fact]
    public void Pencere_saatler_uygulanir_ve_gun_sayisi_bitis_tarihini_ezer()
    {
        // Bitiş tarihi 20 Haziran YAZILI olsa bile gün sayısı verildiyse o kazanır: 10 + 3 = 13.
        var p = AvailabilityService.Pencere(
            new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 20), 3,
            new TimeOnly(9, 30), new TimeOnly(18, 0));
        Assert.NotNull(p);
        Assert.Equal(new DateTimeOffset(2026, 6, 10, 9, 30, 0, TimeSpan.Zero), p!.Value.From);
        Assert.Equal(new DateTimeOffset(2026, 6, 13, 18, 0, 0, TimeSpan.Zero), p.Value.To);
    }

    [Fact]
    public void Pencere_eski_davranis_birebir_korunur()
    {
        // REGRESYON ÇİTİ: saat/gün girilmeyen ESKİ form (yalnız iki tarih) tam olarak eski
        // pencereyi üretmeli — 00:00 → 00:00, offset Zero.
        var p = AvailabilityService.Pencere(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 15), null, null, null);
        Assert.NotNull(p);
        Assert.Equal(new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero), p!.Value.From);
        Assert.Equal(new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero), p.Value.To);

        // Bitiş saati boşken başlangıç saati bitişe de uygulanır (09:00 al → 09:00 bırak).
        var q = AvailabilityService.Pencere(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 15), null, new TimeOnly(9, 0), null);
        Assert.Equal(new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero), q!.Value.To);
    }

    [Fact]
    public void Pencere_eksik_girdide_null()
    {
        Assert.Null(AvailabilityService.Pencere(null, new DateOnly(2026, 6, 15), 3, null, null)); // başlangıç yok
        Assert.Null(AvailabilityService.Pencere(new DateOnly(2026, 6, 10), null, null, null, null)); // bitiş de gün de yok
        Assert.Null(AvailabilityService.Pencere(new DateOnly(2026, 6, 10), null, 0, null, null));    // gün 0 sayılmaz
    }

    [Fact]
    public async Task Saat_hassasiyeti_musaitligi_gercekten_daraltir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var arac = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 SA 01" });
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Saat", Soyad = "Test" });

        // Araç 3 gün sonraki günün 12:00–16:00 arası rezerve.
        var gun = new DateOnly(Bas.Year, Bas.Month, Bas.Day);
        var dolu = AvailabilityService.Pencere(gun, gun, null, new TimeOnly(12, 0), new TimeOnly(16, 0))!.Value;
        await sp.GetRequiredService<ReservationService>().CreateAsync(new BookingInput
        {
            MusteriId = cari, VehicleId = arac, BasTar = dolu.From, BitTar = dolu.To, GunlukUcret = 100m
        });

        var svc = sp.GetRequiredService<AvailabilityService>();
        // Sabah 09:00–11:00 → çakışma YOK, araç müsait (1 araç).
        var sabah = AvailabilityService.Pencere(gun, gun, null, new TimeOnly(9, 0), new TimeOnly(11, 0))!.Value;
        Assert.Single(await svc.FindAvailableAsync(sabah.From, sabah.To));
        // Öğleden sonra 13:00–15:00 → çakışır, araç listede YOK (0 araç).
        var ogle = AvailabilityService.Pencere(gun, gun, null, new TimeOnly(13, 0), new TimeOnly(15, 0))!.Value;
        Assert.Empty(await svc.FindAvailableAsync(ogle.From, ogle.To));
    }

    // ---------------------------------------------------------------- rezervasyon arama/filtre

    /// <summary>3 rezervasyon: (1) Rezerv/Web/plaka 34AA/bugün+3, (2) Onaylı/Telefon/plaka 06BB/bugün+10,
    /// (3) İptal/Web/plaka 34AA/bugün+20. Beklenen sayılar bu tablodan ELLE sayıldı.</summary>
    private static async Task<(Guid rez1, Guid rez2, Guid rez3)> UcRezervasyonAsync(IServiceProvider sp)
    {
        var vs = sp.GetRequiredService<VehicleService>();
        var a1 = await vs.CreateAsync(new VehicleInput { Plaka = "34 AA 11" });
        var a2 = await vs.CreateAsync(new VehicleInput { Plaka = "06 BB 22" });
        var cs = sp.GetRequiredService<CustomerService>();
        var c1 = await cs.CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Ayşe", Soyad = "Yılmaz", CepTel = "5551112233" });
        var c2 = await cs.CreateAsync(new CustomerInput { Tip = CariType.Kurumsal, Unvan = "Beta Lojistik A.Ş." });

        var svc = sp.GetRequiredService<ReservationService>();
        var r1 = await svc.CreateAsync(new BookingInput
        { MusteriId = c1, VehicleId = a1, BasTar = Bas, BitTar = Bas.AddDays(2), GunlukUcret = 100m, Kaynak = "Web" });
        var r2 = await svc.CreateAsync(new BookingInput
        { MusteriId = c2, VehicleId = a2, BasTar = Bas.AddDays(7), BitTar = Bas.AddDays(9), GunlukUcret = 100m, Kaynak = "Telefon" });
        var r3 = await svc.CreateAsync(new BookingInput
        { MusteriId = c1, VehicleId = a1, BasTar = Bas.AddDays(17), BitTar = Bas.AddDays(19), GunlukUcret = 100m, Kaynak = "Web" });
        await svc.ConfirmAsync(r2);
        await svc.CancelAsync(r3);
        return (r1, r2, r3);
    }

    [Fact]
    public async Task Bos_filtre_daraltmaz_durum_filtresi_daraltir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (_, rez2, _) = await UcRezervasyonAsync(sp);
        var svc = sp.GetRequiredService<ReservationService>();

        // Regresyon çiti: boş filtre = 3 kaydın hepsi.
        Assert.Equal(3, (await svc.SearchAsync(new ReservationFilter())).Count);

        // Onaylı olan TEK kayıt (senaryoda 2 numaralı).
        var onayli = await svc.SearchAsync(new ReservationFilter { Durum = ReservationStatus.Onayli });
        Assert.Equal(rez2, Assert.Single(onayli).Rez.Id);
        Assert.Equal("Beta Lojistik A.Ş.", onayli[0].MusteriAd);
        Assert.Equal("06BB22", onayli[0].Plaka);

        // İptal 1, Rezerv 1.
        Assert.Single(await svc.SearchAsync(new ReservationFilter { Durum = ReservationStatus.Iptal }));
        Assert.Single(await svc.SearchAsync(new ReservationFilter { Durum = ReservationStatus.Rezerv }));
    }

    [Fact]
    public async Task Arama_rez_no_musteri_ve_plaka_uzerinde_calisir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await UcRezervasyonAsync(sp);
        var svc = sp.GetRequiredService<ReservationService>();

        // Plaka 34AA'lı 2 kayıt (1. ve 3.), 06BB'li 1 kayıt.
        Assert.Equal(2, (await svc.SearchAsync(new ReservationFilter { Query = "34AA" })).Count);
        Assert.Single(await svc.SearchAsync(new ReservationFilter { Query = "06bb" })); // case-insensitive
        // Müşteri adı: kurumsal ünvan parçası 1 kayıt; bireysel soyad 2 kayıt.
        Assert.Single(await svc.SearchAsync(new ReservationFilter { Query = "Lojistik" }));
        Assert.Equal(2, (await svc.SearchAsync(new ReservationFilter { Query = "yılmaz" })).Count);
        // Rez No araması: numara ÜRETİLEN değerden alınır (format sabit dize olarak yazılamaz —
        // artık {yyyy}{dd}{MM}{TT}{sss} deseninde ve güne göre değişir).
        var ilkNo = (await svc.SearchAsync(new ReservationFilter())).OrderBy(r => r.Rez.ReservationNo).First().Rez.ReservationNo;
        Assert.Single(await svc.SearchAsync(new ReservationFilter { Query = ilkNo }));
        // Plaka DB'de boşluksuz ("34AA11"); kullanıcı BOŞLUKLU yazınca da bulunmalı.
        Assert.Equal(2, (await svc.SearchAsync(new ReservationFilter { Query = "34 AA 11" })).Count);
        // Hiçbir şeye uymayan terim → 0 (sessizce tümünü döndürmez).
        Assert.Empty(await svc.SearchAsync(new ReservationFilter { Query = "ZZZ-yok" }));
    }

    [Fact]
    public async Task Tarih_araligi_ve_kaynak_filtresi()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await UcRezervasyonAsync(sp);
        var svc = sp.GetRequiredService<ReservationService>();

        // Kaynak: Web 2 (1. ve 3.), Telefon 1.
        Assert.Equal(2, (await svc.SearchAsync(new ReservationFilter { Kaynak = "Web" })).Count);
        Assert.Single(await svc.SearchAsync(new ReservationFilter { Kaynak = "telefon" })); // case-insensitive
        Assert.Single(await svc.SearchAsync(new ReservationFilter { Kaynak = " Telefon " })); // Trim
        Assert.Empty(await svc.SearchAsync(new ReservationFilter { Kaynak = "Bayi" }));       // tanımsız → 0

        // Başlangıçlar: Bas, Bas+7, Bas+17. [Bas+5, Bas+10] aralığı yalnız 2. kaydı kapsar.
        var pencere = await svc.SearchAsync(new ReservationFilter
        { TarihMin = Bas.AddDays(5), TarihMax = Bas.AddDays(10) });
        Assert.Single(pencere);

        // GÜN DAHİL konvansiyonu: 2. kayıt o günün 09:00'unda başlıyor. Üst sınır o günün 00:00'ı
        // olarak verilirse kayıt DIŞARIDA kalır; ekran/export .AddDays(1).AddTicks(-1) ile gün
        // sonuna taşıdığı için İÇERİDE kalmalı (aksi halde "son gün kayboldu" şikâyeti doğar).
        var gunBasi = new DateTimeOffset(Bas.AddDays(7).UtcDateTime.Date, TimeSpan.Zero);
        Assert.Empty(await svc.SearchAsync(new ReservationFilter { TarihMin = Bas.AddDays(5), TarihMax = gunBasi }));
        Assert.Single(await svc.SearchAsync(new ReservationFilter
        { TarihMin = Bas.AddDays(5), TarihMax = gunBasi.AddDays(1).AddTicks(-1) }));
    }

    [Fact]
    public async Task Arama_sube_kapsamini_zorlar_ve_cagiran_genisletemez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid b1;
        using (var seed = host.ScopeFor(tenant))
        {
            var sp = seed.ServiceProvider;
            var branches = sp.GetRequiredService<BranchService>();
            b1 = await branches.CreateAsync(new BranchInput { Kod = "MRK", Ad = "Merkez" });
            await branches.CreateAsync(new BranchInput { Kod = "ANK", Ad = "Ankara" });
            var locs = sp.GetRequiredService<LocationService>();
            await locs.CreateAsync(new LocationInput { Kod = "L1", Ad = "Merkez", Sube = "Merkez" });
            await locs.CreateAsync(new LocationInput { Kod = "L2", Ad = "Havalimanı", Sube = "Merkez" });
            await locs.CreateAsync(new LocationInput { Kod = "L3", Ad = "Ankara Ofis", Sube = "Ankara" });

            var arac = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 KP 01" });
            var cari = await sp.GetRequiredService<CustomerService>()
                .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Kapsam", Soyad = "Cari" });
            var svc = sp.GetRequiredService<ReservationService>();
            // Merkez şubesinin İKİ ofisinde birer, Ankara'da bir rezervasyon.
            var i = 0;
            foreach (var ofis in new[] { "Merkez", "Havalimanı", "Ankara Ofis" })
            {
                var girdi = Girdi(cari, arac, Bas.AddDays(i * 6), 2);
                girdi.CikisOfisi = ofis;
                await svc.CreateAsync(girdi);
                i++;
            }
        }

        // Operatör@Merkez: şubesinin TÜM ofislerini görür (2), Ankara'yı asla.
        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator,
            assignedBranch: "Merkez", assignedBranchId: b1);
        var opSvc = op.ServiceProvider.GetRequiredService<ReservationService>();
        var kapsam = await opSvc.SearchAsync(new ReservationFilter());
        Assert.Equal(2, kapsam.Count);
        Assert.All(kapsam, r => Assert.NotEqual("Ankara Ofis", r.Rez.CikisOfisi));

        // Çağıran filtreye Unrestricted kapsam koysa bile GENİŞLETEMEZ (servis üzerine yazar).
        var sahte = await opSvc.SearchAsync(new ReservationFilter
        { Kapsam = default }); // default = Unrestricted
        Assert.Equal(2, sahte.Count);

        // Admin tümünü görür (3).
        using var admin = host.ScopeFor(tenant);
        Assert.Equal(3, (await admin.ServiceProvider.GetRequiredService<ReservationService>()
            .SearchAsync(new ReservationFilter())).Count);
    }

    [Fact]
    public async Task Arama_tenant_izole()
    {
        using var host = new TestHost(fx.AppConnectionString); // racar_app (RLS zorunlu)
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
            await UcRezervasyonAsync(s1.ServiceProvider);

        using var s2 = host.ScopeFor(Guid.NewGuid());
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<ReservationService>()
            .SearchAsync(new ReservationFilter()));
    }

    // ---------------------------------------------------------------- talep bilgi alanları

    [Fact]
    public async Task Talep_alanlari_round_trip_ve_guncelleme()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var arac = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 TL 01" });
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Talep", Soyad = "Cari" });
        var svc = sp.GetRequiredService<ReservationService>();

        var girdi = Girdi(cari, arac);
        girdi.TalepTuru = "  Kurumsal  ";       // Trim uygulanmalı
        girdi.GeldigiBirim = "Çağrı Merkezi";
        girdi.OnayKodu = "ON-2026-7";
        girdi.ProjeAdi = "ACME Filo Projesi";
        girdi.OtaKiraBedeli = 1234.56m;
        girdi.OtaCdw = 99.90m;
        var id = await svc.CreateAsync(girdi);

        var r = await svc.GetAsync(id);
        Assert.Equal("Kurumsal", r!.TalepTuru);
        Assert.Equal("Çağrı Merkezi", r.GeldigiBirim);
        Assert.Equal("ON-2026-7", r.OnayKodu);
        Assert.Equal("ACME Filo Projesi", r.ProjeAdi);
        Assert.Equal(1234.56m, r.OtaKiraBedeli);
        Assert.Equal(99.90m, r.OtaCdw);

        // GENEL POLİTİKA ÇİTİ: bu alanlar BİLGİDİR — tutar 4 gün × 100 = 400 olarak KALMALI.
        Assert.Equal(400m, r.Tutar);

        // Güncelleme: alanlar değişir, tutar yine 400 (fiyat girdileri değişmedi).
        var g2 = Girdi(cari, arac);
        g2.TalepTuru = "Bireysel";
        g2.ProjeAdi = null;                       // boşaltma da çalışmalı
        g2.OnayKodu = "ON-2026-8";
        Assert.True(await svc.UpdateAsync(id, g2));
        var r2 = await svc.GetAsync(id);
        Assert.Equal("Bireysel", r2!.TalepTuru);
        Assert.Null(r2.ProjeAdi);
        Assert.Equal("ON-2026-8", r2.OnayKodu);
        Assert.Equal(400m, r2.Tutar);
    }

    [Fact]
    public async Task Talep_alani_uzunluk_citi()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var arac = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 UZ 01" });
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Uzun", Soyad = "Cari" });

        var girdi = Girdi(cari, arac);
        girdi.ProjeAdi = new string('x', 129);   // kolon 128 — DB hatası yerine anlaşılır red
        await Assert.ThrowsAsync<ValidationException>(() =>
            sp.GetRequiredService<ReservationService>().CreateAsync(girdi));
    }

    [Fact]
    public async Task Kiraya_cevir_talep_alanlarini_sozlesmeye_tasir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var arac = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 KC 01" });
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Cevir", Soyad = "Cari" });

        var girdi = Girdi(cari, arac);
        girdi.TalepTuru = "Sigorta İkame";
        girdi.GeldigiBirim = "Şube";
        girdi.OnayKodu = "SGK-42";
        girdi.ProjeAdi = "İkame Havuzu";
        var rezId = await sp.GetRequiredService<ReservationService>().CreateAsync(girdi);

        var kiraId = await sp.GetRequiredService<ReservationService>().ConvertToRentalAsync(rezId);
        var kira = await sp.GetRequiredService<RentalService>().GetAsync(kiraId);

        // Elle girilen DEĞERLER birebir sözleşmede (operatör mega-formda tekrar girmez).
        Assert.Equal("Sigorta İkame", kira!.TalepTuru);
        Assert.Equal("Şube", kira.GeldigiBirim);
        Assert.Equal("SGK-42", kira.OnayKodu);
        Assert.Equal("İkame Havuzu", kira.ProjeAdi);
    }

    [Fact]
    public async Task Yetkisiz_rol_talep_alanlariyla_rezervasyon_acamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid arac, cari;
        using (var seed = host.ScopeFor(tenant))
        {
            arac = await seed.ServiceProvider.GetRequiredService<VehicleService>()
                .CreateAsync(new VehicleInput { Plaka = "34 YT 01" });
            cari = await seed.ServiceProvider.GetRequiredService<CustomerService>()
                .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Yetki", Soyad = "Cari" });
        }

        // Muhasebe rolünde OperationsWrite YOK → yeni alanlar guard'ı delmez.
        using var muhasebe = host.ScopeFor(tenant, Guid.NewGuid(), "muh", UserRole.Muhasebe);
        var girdi = Girdi(cari, arac);
        girdi.TalepTuru = "Kurumsal";
        await Assert.ThrowsAsync<ValidationException>(() =>
            muhasebe.ServiceProvider.GetRequiredService<ReservationService>().CreateAsync(girdi));
    }
}
