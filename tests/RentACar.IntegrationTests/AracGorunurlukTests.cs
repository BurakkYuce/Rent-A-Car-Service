using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-11 — araç LİSTESİ filtre derinliği (Grup↔SIPP anahtarı, tarih-tipi aralığı, sahiplik,
/// şube) + listenin diğer tablolardan çözdüğü ek bilgi + TAKVİM araç kümesi daraltması.
///
/// <para>Bağımsız oracle: her senaryoda beklenen SAYI elle kurulan tohumdan okunur
/// (ör. "2 aracın grubu A" → 2), servis/repo kodundan türetilmez.</para>
/// </summary>
[Collection("postgres")]
public sealed class AracGorunurlukTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Oca = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static async Task SeedAsync(IServiceScope scope, params Vehicle[] vehicles)
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        db.Vehicles.AddRange(vehicles);
        await db.SaveChangesAsync();
    }

    // ---- Bölüm B: filtreler ----

    /// <summary>
    /// "Grup Türü" anahtarı — AYNI arama kutusu iki farklı alanı hedefler.
    /// Tohum: 2 araç Grup="A" (SIPP'leri farklı), 2 araç SIPP="CDMD" (grupları farklı).
    /// Beklenen: Grup modunda 2, SIPP modunda 2 — ve kümeler ÖRTÜŞMEZ.
    /// </summary>
    [Fact]
    public async Task GrupTuru_anahtari_grup_ile_sipp_arasinda_gecer()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedAsync(scope,
            new Vehicle { Plaka = "34GRP01", Grup = "A", Sipp = "ECMN" },
            new Vehicle { Plaka = "34GRP02", Grup = "A", Sipp = "EDMR" },
            new Vehicle { Plaka = "34GRP03", Grup = "B", Sipp = "CDMD" },
            new Vehicle { Plaka = "34GRP04", Grup = "C", Sipp = "CDMD" });
        var svc = scope.ServiceProvider.GetRequiredService<VehicleService>();

        var groupA = await svc.SearchAsync(new VehicleFilter { Grup = "A", GrupTuru = VehicleGroupType.Grup, PageSize = 50 });
        Assert.Equal(2, groupA.Total);
        Assert.Equal(["34GRP01", "34GRP02"], groupA.Items.Select(v => v.Plaka).Order().ToArray());

        var sippCdmd = await svc.SearchAsync(new VehicleFilter { Grup = "CDMD", GrupTuru = VehicleGroupType.Sipp, PageSize = 50 });
        Assert.Equal(2, sippCdmd.Total);
        Assert.Equal(["34GRP03", "34GRP04"], sippCdmd.Items.Select(v => v.Plaka).Order().ToArray());

        // Küçük harf yazan kullanıcı da bulmalı (SIPP kayıtta büyük harf normalize edilir).
        Assert.Equal(2, (await svc.SearchAsync(
            new VehicleFilter { Grup = "cdmd", GrupTuru = VehicleGroupType.Sipp, PageSize = 50 })).Total);

        // Anahtar Grup'tayken "CDMD" bir GRUP adı olarak aranır → hiçbir araç eşleşmez.
        Assert.Equal(0, (await svc.SearchAsync(
            new VehicleFilter { Grup = "CDMD", GrupTuru = VehicleGroupType.Grup, PageSize = 50 })).Total);
    }

    /// <summary>
    /// Tarih tipi + aralık. Tohum (filo giriş): 05.01, 10.02, 20.03.
    /// Aralık 01.01–10.02 → beklenen 2 (bitiş GÜNÜ DAHİL: 10.02'deki araç sayılır).
    /// Tescil tarihleri bilerek FARKLI kurulur → tip anahtarının gerçekten alan değiştirdiği görülür.
    /// </summary>
    [Fact]
    public async Task Tarih_araligi_secilen_tarih_tipine_uygulanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedAsync(scope,
            new Vehicle { Plaka = "34TAR01", FiloGirisTarih = Oca.AddDays(4), TescilTarihi = Oca.AddMonths(6) },
            new Vehicle { Plaka = "34TAR02", FiloGirisTarih = Oca.AddMonths(1).AddDays(9), TescilTarihi = Oca.AddMonths(7) },
            new Vehicle { Plaka = "34TAR03", FiloGirisTarih = Oca.AddMonths(2).AddDays(19), TescilTarihi = Oca.AddDays(2) });
        var svc = scope.ServiceProvider.GetRequiredService<VehicleService>();

        var entryRange = await svc.SearchAsync(new VehicleFilter
        {
            TarihTuru = VehicleDateType.FiloGiris,
            TarihBas = Oca,
            TarihBit = Oca.AddMonths(1).AddDays(9),   // 10 Şubat — DAHİL olmalı
            PageSize = 50
        });
        Assert.Equal(2, entryRange.Total);
        Assert.Equal(["34TAR01", "34TAR02"], entryRange.Items.Select(v => v.Plaka).Order().ToArray());

        // Aynı aralık TESCİL'e uygulanınca bambaşka bir araç gelir (tip anahtarı gerçekten çalışıyor).
        var registrationRange = await svc.SearchAsync(new VehicleFilter
        {
            TarihTuru = VehicleDateType.Tescil, TarihBas = Oca, TarihBit = Oca.AddMonths(1), PageSize = 50
        });
        Assert.Equal("34TAR03", Assert.Single(registrationRange.Items).Plaka);

        // Tip seçilmemişse aralık HİÇ uygulanmaz — üçü de gelir.
        Assert.Equal(3, (await svc.SearchAsync(new VehicleFilter
        {
            TarihTuru = VehicleDateType.Yok, TarihBas = Oca, TarihBit = Oca.AddDays(1), PageSize = 50
        })).Total);
    }

    /// <summary>
    /// Araç sahibi filtresi: belirli sahip (tam eşleşme) + "girilmemiş" kovası.
    /// Tohum: 2 araç "Yatırım Filo A.Ş.", 1 araç sahipsiz, 1 araç yalnız boşluk (= girilmemiş).
    /// </summary>
    [Fact]
    public async Task Arac_sahibi_filtresi_secilen_sahibi_ve_bos_kovayi_ayirir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedAsync(scope,
            new Vehicle { Plaka = "34SAH01", AracSahibi = "Yatırım Filo A.Ş." },
            new Vehicle { Plaka = "34SAH02", AracSahibi = "Yatırım Filo A.Ş." },
            new Vehicle { Plaka = "34SAH03" },
            new Vehicle { Plaka = "34SAH04", AracSahibi = "   " });
        var svc = scope.ServiceProvider.GetRequiredService<VehicleService>();

        var selected = await svc.SearchAsync(new VehicleFilter { AracSahibi = "Yatırım Filo A.Ş.", PageSize = 50 });
        Assert.Equal(2, selected.Total);

        var empty = await svc.SearchAsync(new VehicleFilter { Sahiplik = VehicleOwnership.Girilmemis, PageSize = 50 });
        Assert.Equal(2, empty.Total);   // sahipsiz + yalnız-boşluk

        Assert.Equal(4, (await svc.SearchAsync(new VehicleFilter { Sahiplik = VehicleOwnership.Hepsi, PageSize = 50 })).Total);

        // "Girilmemiş" kovası seçiliyken sahip adı yok sayılır (iki kova aynı anda anlamsız).
        Assert.Equal(2, (await svc.SearchAsync(new VehicleFilter
        {
            Sahiplik = VehicleOwnership.Girilmemis, AracSahibi = "Yatırım Filo A.Ş.", PageSize = 50
        })).Total);
    }

    /// <summary>
    /// Şube ("Ofis") filtresi + ROL KAPSAMI birlikte. Operatör başka bir şubeyi seçse bile kendi
    /// şubesinin dışını GÖREMEZ — UI filtresi kapsamı genişletemez (C3 kuralı).
    /// </summary>
    [Fact]
    public async Task Sube_filtresi_ve_operator_kapsami_birlikte_calisir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using (var admin = host.ScopeFor(tenant))
        {
            await SeedAsync(admin,
                new Vehicle { Plaka = "34SUB01", Sube = "Kadıköy" },
                new Vehicle { Plaka = "34SUB02", Sube = "Kadıköy" },
                new Vehicle { Plaka = "34SUB03", Sube = "Beşiktaş" });

            var all = admin.ServiceProvider.GetRequiredService<VehicleService>();
            Assert.Equal(2, (await all.SearchAsync(new VehicleFilter { Sube = "Kadıköy", PageSize = 50 })).Total);
        }

        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator, assignedBranch: "Kadıköy");
        var opSvc = op.ServiceProvider.GetRequiredService<VehicleService>();
        // Operatör "Beşiktaş" seçse bile kapsam dışına çıkamaz → 0 (sızıntı yok).
        Assert.Equal(0, (await opSvc.SearchAsync(new VehicleFilter { Sube = "Beşiktaş", PageSize = 50 })).Total);
        Assert.Equal(2, (await opSvc.SearchAsync(new VehicleFilter { PageSize = 50 })).Total);
    }

    // ---- Bölüm B: liste ek bilgisi (diğer tablolardan çözülen kolonlar) ----

    /// <summary>
    /// Ek bilgi kolonları: aktif kira sözleşme no, açık servis/BAF/satış bayrakları, kasko ve kredi.
    /// Her bilgi AYRI araca kurulur; beklenen değerler tohumdaki sabitlerdir.
    /// </summary>
    [Fact]
    public async Task ListeEk_diger_tablolardan_cozulen_kolonlari_doldurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        Guid a, b, c, d, e, f;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var account = new Customer { Tip = CustomerType.Bireysel, Ad = "Ek", Soyad = "Test" };
            db.Customers.Add(account);
            var va = new Vehicle { Plaka = "34EKB01", Durum = VehicleStatus.Kirada };
            var vb = new Vehicle { Plaka = "34EKB02" };
            var vc = new Vehicle { Plaka = "34EKB03" };
            var vd = new Vehicle { Plaka = "34EKB04" };
            var ve = new Vehicle { Plaka = "34EKB05" };
            var vf = new Vehicle { Plaka = "34EKB06" };
            db.Vehicles.AddRange(va, vb, vc, vd, ve, vf);
            var pers = new Personel { Kod = "P-11", Ad = "Ek", Soyad = "Personel" };
            db.Personeller.Add(pers);

            db.Rentals.Add(new RentalContract
            {
                SozlesmeNo = "K-EK1", VehicleId = va.Id, MusteriId = account.Id, Durum = RentalStatus.Kirada,
                BasTar = DateTimeOffset.UtcNow.AddDays(-1), BitTar = DateTimeOffset.UtcNow.AddDays(2)
            });
            db.ServiceRecords.Add(new ServiceRecord { No = "SV-EK1", VehicleId = vb.Id, Durum = ServiceStatus.Acik });
            db.Baflar.Add(new Baf { No = "BAF-EK1", VehicleId = vc.Id, PersonelId = pers.Id, Durum = BafStatus.Acik });
            db.VehicleSales.Add(new VehicleSale
            {
                No = "SAT-EK1", VehicleId = vd.Id, AliciCariId = account.Id, Durum = SaleStatus.Tamamlandi,
                SatisNet = 100m, GenelToplam = 100m
            });
            // İPTAL satış bayrağı YAKMAZ (vf).
            db.VehicleSales.Add(new VehicleSale
            {
                No = "SAT-EK2", VehicleId = vf.Id, AliciCariId = account.Id, Durum = SaleStatus.Iptal,
                SatisNet = 100m, GenelToplam = 100m
            });
            db.InsurancePolicies.Add(new InsurancePolicy
            {
                VehicleId = ve.Id, Tip = InsuranceType.Kasko, Prim = 4200m,
                Baslangic = Oca, Bitis = Oca.AddYears(1)
            });
            // Trafik poliçesi kasko kolonuna SIZMAMALI.
            db.InsurancePolicies.Add(new InsurancePolicy
            {
                VehicleId = ve.Id, Tip = InsuranceType.Trafik, Prim = 999m,
                Baslangic = Oca, Bitis = Oca.AddYears(3)
            });
            db.AracKredileri.Add(new AracKredi
            {
                No = "KR-EK1", BankaAdi = "Ziraat", VehicleId = ve.Id, KrediTutari = 100000m,
                TaksitSayisi = 12, BaslangicTarihi = Oca
            });
            await db.SaveChangesAsync();
            (a, b, c, d, e, f) = (va.Id, vb.Id, vc.Id, vd.Id, ve.Id, vf.Id);
        }

        var extra = await scope.ServiceProvider.GetRequiredService<VehicleService>()
            .ListExtrasAsync([a, b, c, d, e, f]);

        Assert.Equal("K-EK1", extra[a].AktifKiraSozlesmeNo);
        Assert.True(extra[b].AcikServis);
        Assert.False(extra[a].AcikServis);
        Assert.True(extra[c].AcikBaf);
        Assert.True(extra[d].SatisVar);
        Assert.False(extra[f].SatisVar);              // iptal edilmiş satış "var" saymaz

        Assert.Equal(4200m, extra[e].KaskoPrim);      // trafik primi (999) DEĞİL
        Assert.Equal(Oca.AddYears(1), extra[e].KaskoBitis);
        Assert.Equal("Ziraat", extra[e].KrediKurulusu);
        Assert.Equal(Oca.AddMonths(12), extra[e].KrediSonTarih);  // 01.01.2026 + 12 taksit = 01.01.2027
    }

    /// <summary>Ek bilgi TENANT İZOLE: başka tenant'ın araç kimlikleriyle sorulunca boş döner
    /// (RLS + query filter). racar_app bağlantısıyla koşar (fixture öyle ayarlı).</summary>
    [Fact]
    public async Task ListeEk_tenant_izole()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        Guid vehicleId;
        using (var s1 = host.ScopeFor(t1))
        {
            var factory = s1.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            var account = new Customer { Tip = CustomerType.Bireysel, Ad = "İzo", Soyad = "Lasyon" };
            var v = new Vehicle { Plaka = "34IZO01", Durum = VehicleStatus.Kirada };
            db.Customers.Add(account); db.Vehicles.Add(v);
            db.Rentals.Add(new RentalContract
            {
                SozlesmeNo = "K-IZO", VehicleId = v.Id, MusteriId = account.Id, Durum = RentalStatus.Kirada,
                BasTar = DateTimeOffset.UtcNow.AddDays(-1), BitTar = DateTimeOffset.UtcNow.AddDays(2)
            });
            await db.SaveChangesAsync();
            vehicleId = v.Id;
        }

        using var s2 = host.ScopeFor(Guid.NewGuid());
        var extra = await s2.ServiceProvider.GetRequiredService<VehicleService>().ListExtrasAsync([vehicleId]);
        // Anahtar var (istenen kimlik) ama İÇERİK boş — başka tenant'ın kirası sızmaz.
        Assert.Null(extra[vehicleId].AktifKiraSozlesmeNo);
        Assert.False(extra[vehicleId].AcikServis);
    }

    // ---- Bölüm C: takvimin araç kümesi ----

    /// <summary>
    /// Takvim ekranı doluluk sorgusunu DEĞİŞTİRMEZ; yalnız gösterilecek araç kümesini daraltır.
    /// Tohum: 5 araç, 2'si Grup="A" → grup filtresiyle beklenen 2 (sabit).
    /// </summary>
    [Fact]
    public async Task Takvim_arac_kumesi_grup_ve_sube_filtresiyle_daralir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedAsync(scope,
            new Vehicle { Plaka = "34TKV01", Grup = "A", Sube = "Kadıköy" },
            new Vehicle { Plaka = "34TKV02", Grup = "A", Sube = "Beşiktaş" },
            new Vehicle { Plaka = "34TKV03", Grup = "B", Sube = "Kadıköy" },
            new Vehicle { Plaka = "34TKV04", Grup = "B", Sube = "Beşiktaş" },
            new Vehicle { Plaka = "34TKV05", Grup = "C", Sube = "Kadıköy" });
        var svc = scope.ServiceProvider.GetRequiredService<VehicleService>();

        // Takvim sayfasının kullandığı çağrı (PageSize 200).
        var groupA = await svc.SearchAsync(new VehicleFilter { Grup = "A", PageSize = 200 });
        Assert.Equal(2, groupA.Total);

        var kadikoy = await svc.SearchAsync(new VehicleFilter { Sube = "Kadıköy", PageSize = 200 });
        Assert.Equal(3, kadikoy.Total);

        // Plaka araması (takvimdeki arama kutusu Query alanına bağlanır).
        var plate = await svc.SearchAsync(new VehicleFilter { Query = "TKV05", PageSize = 200 });
        Assert.Equal("34TKV05", Assert.Single(plate.Items).Plaka);

        // Filtresiz: beşi de görünür (takvim varsayılanı bozulmadı).
        Assert.Equal(5, (await svc.SearchAsync(new VehicleFilter { PageSize = 200 })).Total);
    }
}
