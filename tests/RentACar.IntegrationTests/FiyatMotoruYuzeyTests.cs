using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Branches;
using RentACar.Application.BrokerYasaklari;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.DolulukFiyat;
using RentACar.Application.Pricing;
using RentACar.Application.RateMatrices;
using RentACar.Application.RentalRules;
using RentACar.Application.VehicleGroups;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-73 — fiyat motoru YÜZEY genişletmeleri (broker müsaitlik çiti + doluluk şube kapsamı +
/// kampanya 5-durum). Bu faz motoru yeni yüzeylere açar, <b>hesaplanan fiyatı DEĞİŞTİRMEZ</b>.
///
/// <para><b>BAĞIMSIZ ORACLE (elle kurulmuş senaryo, koddan türetilmedi):</b> 10 EKO aracı × 5 günlük
/// pencere = 50 araç-gün; 8 araç tam (40) + 1 araç 2 gün (42) dolu → <b>%84</b>. Tarife 1000/gün.
/// Eşik 80 / çarpan 15 kuralı → 1000 × 1,15 = <b>1150</b>/gün, 5 gün = <b>5750</b>. Çarpan aday
/// değilse fiyat <b>1000</b>/gün ve <b>5000</b> toplam. Bu dört sabit tüm testlerin çıpasıdır.</para>
/// </summary>
[Collection("postgres")]
public sealed class FiyatMotoruYuzeyTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas =
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(10);

    /// <summary>10 araçlı EKO filosu; 42/50 araç-gün dolu → %84 (elle sayıldı).</summary>
    private static async Task SeedFiloAsync(IServiceProvider sp, string? aracSube = null)
    {
        await sp.GetRequiredService<VehicleGroupService>().CreateAsync(new VehicleGroupInput
        { Kod = "EKO", Ad = "Ekonomik" });
        await sp.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
        {
            Kod = "EKO-STD", Ad = "Eko", AracGrupKod = "EKO", ParaBirimi = "TRY",
            Gun1 = 1000m, Gun2 = 1000m, Gun3 = 1000m, Gun4 = 1000m, Gun5 = 1000m,
            OnayDurumu = TarifeOnayDurumu.Onayli, Onaylayan = "t"
        });
        var veh = sp.GetRequiredService<VehicleService>();
        var araclar = new List<Guid>();
        for (var i = 1; i <= 10; i++)
            araclar.Add(await veh.CreateAsync(new VehicleInput { Plaka = $"34 YZ {i:00}", Grup = "EKO", Sube = aracSube }));
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CariType.Bireysel, Ad = "Dolu", Soyad = "M" });
        var rentals = sp.GetRequiredService<RentalService>();
        for (var i = 0; i < 8; i++)   // 8 araç × 5 gün = 40 araç-gün
            await rentals.CreateDirectAsync(new BookingInput
            { MusteriId = m, VehicleId = araclar[i], BasTar = Bas, BitTar = Bas.AddDays(5), GunlukUcret = 100m });
        await rentals.CreateDirectAsync(new BookingInput  // 9. araç 2 gün → toplam 42
        { MusteriId = m, VehicleId = araclar[8], BasTar = Bas, BitTar = Bas.AddDays(2), GunlukUcret = 100m });
    }

    private static Task<QuoteResult> Teklif(IServiceProvider sp, string? sube = null)
        => sp.GetRequiredService<RentalQuoteEngine>().QuoteAsync(new QuoteRequest
        { AracGrupKod = "EKO", BasTar = Bas, BitTar = Bas.AddDays(5), Sube = sube, SigortaUrunKodlari = [] });

    // ---------------------------------------------------------------------------------------
    // 1) KIRILGAN REGRESYON — "fiyat DEĞİŞMEDİ"
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// FAZ-73'ün ana çiti: yeni alanların HİÇBİRİ dokunulmadığında motor bit-birebir eskisi gibi
    /// çalışır. Üç senaryo tek testte, elle kurulmuş sabitlerle:
    /// (a) kural yok → 1000/gün, 5000 toplam;
    /// (b) doluluk kuralı var, şube bayrağı KAPALI (varsayılan) → 1150/gün, 5750 toplam — kuralın
    ///     Sube alanı DOLU olsa bile motorda okunmaz;
    /// (c) kampanya durumu Aktif olan iskonto kuralı → 5750 × 0,90 = 5175.
    /// Herhangi bir kuruş kayması bu testi kırar.
    /// </summary>
    [Fact]
    public async Task Fiyat_degismedi_yeni_alanlar_varsayilanken_bayt_ozdes()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedFiloAsync(sp);

        // (a) Hiç doluluk kuralı yok → tarife kademesi aynen: 1000/gün, 5 gün = 5000.
        var q0 = await Teklif(sp);
        Assert.Equal(1000m, q0.GunlukUcret);
        Assert.Equal(5000m, q0.BazTutar);
        Assert.Equal(5000m, q0.GenelToplam);

        // (b) Doluluk kuralı + ŞUBE DOLU ama SadeceKendiSubeleri KAPALI → şube alanı motorda
        //     OKUNMAZ; çarpan eskisi gibi uygulanır: %84 ≥ %80 → 1150/gün, 5750.
        await sp.GetRequiredService<BranchService>().CreateAsync(new BranchInput { Kod = "MRK", Ad = "Merkez" });
        await sp.GetRequiredService<DolulukFiyatKuralService>().CreateAsync(new DolulukFiyatKuralInput
        { Kod = "D80", Ad = "Doluluk 80", EsikYuzde = 80, CarpanYuzde = 15m, Sube = "Merkez" });

        var q1 = await Teklif(sp);                    // teklifte şube YOK
        Assert.Equal(1150m, q1.GunlukUcret);
        Assert.Equal(5750m, q1.BazTutar);
        var q2 = await Teklif(sp, sube: "Baska");     // teklifte ALAKASIZ şube
        Assert.Equal(1150m, q2.GunlukUcret);
        Assert.Equal(5750m, q2.BazTutar);

        // (c) Kampanya durumu varsayılan (Aktif) iskonto kuralı → 5750 × 0,90 = 5175.
        await sp.GetRequiredService<RentalRuleService>()
            .CreateAsync(new RentalRuleInput { Kod = "GENEL", Ad = "G", Iskonto = 10m });
        var q3 = await Teklif(sp);
        Assert.Equal(575m, q3.IskontoTutar);
        Assert.Equal(5175m, q3.GenelToplam);
    }

    /// <summary>
    /// <c>TarihTipi</c> BİLGİ ALANIDIR (KARARLAR.md genel politikası): uçuk bir değerle
    /// doldurulduğunda teklif rakamları DEĞİŞMEZ. Elle sabit: 5750 × 0,90 = 5175 — hem
    /// Rezervasyon hem Talep tipinde aynı.
    /// </summary>
    [Fact]
    public async Task TarihTipi_bilgi_alani_fiyat_degismedi()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedFiloAsync(sp);
        await sp.GetRequiredService<DolulukFiyatKuralService>().CreateAsync(new DolulukFiyatKuralInput
        { Kod = "D80", Ad = "Doluluk 80", EsikYuzde = 80, CarpanYuzde = 15m });
        var kurallar = sp.GetRequiredService<RentalRuleService>();
        var id = await kurallar.CreateAsync(new RentalRuleInput
        { Kod = "GENEL", Ad = "G", Iskonto = 10m, TarihTipi = KuralTarihTipi.Rezervasyon });

        var once = await Teklif(sp);
        Assert.Equal(5175m, once.GenelToplam);

        await kurallar.UpdateAsync(id, new RentalRuleInput
        { Kod = "GENEL", Ad = "G", Iskonto = 10m, TarihTipi = KuralTarihTipi.Talep });
        Assert.Equal(KuralTarihTipi.Talep, (await kurallar.GetAsync(id))!.TarihTipi);

        var sonra = await Teklif(sp);
        Assert.Equal(5175m, sonra.GenelToplam);       // tek kuruş oynamadı
        Assert.Equal(once.GunlukUcret, sonra.GunlukUcret);
        Assert.Equal(once.IskontoTutar, sonra.IskontoTutar);
    }

    // ---------------------------------------------------------------------------------------
    // 2) DOLULUK ŞUBE KAPSAMI
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// <c>SadeceKendiSubeleri=true</c> kuralı YALNIZ kendi şubesinin teklifine uygulanır.
    /// Elle sabitler: aynı şube → 1150/gün (5750); başka şube ve şubesiz → çarpansız 1000/gün (5000).
    /// </summary>
    [Fact]
    public async Task Sadece_kendi_subeleri_surgeyi_dogru_kisitliyor()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedFiloAsync(sp);
        await sp.GetRequiredService<BranchService>().CreateAsync(new BranchInput { Kod = "MRK", Ad = "Merkez" });
        await sp.GetRequiredService<BranchService>().CreateAsync(new BranchInput { Kod = "IZM", Ad = "Izmir" });
        await sp.GetRequiredService<DolulukFiyatKuralService>().CreateAsync(new DolulukFiyatKuralInput
        {
            Kod = "D80M", Ad = "Merkez Doluluk", EsikYuzde = 80, CarpanYuzde = 15m,
            Sube = "Merkez", SadeceKendiSubeleri = true
        });

        var ayni = await Teklif(sp, sube: "Merkez");
        Assert.Equal(1150m, ayni.GunlukUcret);
        Assert.Equal(5750m, ayni.BazTutar);
        Assert.Contains(ayni.Notlar, n => n.Contains("Doluluk %84"));

        var farkli = await Teklif(sp, sube: "Izmir");
        Assert.Equal(1000m, farkli.GunlukUcret);
        Assert.Equal(5000m, farkli.BazTutar);
        Assert.DoesNotContain(farkli.Notlar, n => n.Contains("Doluluk"));

        var subesiz = await Teklif(sp);
        Assert.Equal(1000m, subesiz.GunlukUcret);      // "belirtilmemiş" şube kısıtı DELMEZ
        Assert.Equal(5000m, subesiz.BazTutar);

        // Şube eşleşmesi harf-duyarsız (RowMatches ile aynı konvansiyon).
        Assert.Equal(1150m, (await Teklif(sp, sube: "merkez")).GunlukUcret);
    }

    /// <summary>Şubesiz "sadece kendi şubesi" kuralı SESSİZCE ölü olurdu → giriş noktasında reddedilir.</summary>
    [Fact]
    public async Task Subesiz_sadece_kendi_subeleri_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<DolulukFiyatKuralService>();
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new DolulukFiyatKuralInput
        { Kod = "X", Ad = "X", EsikYuzde = 80, CarpanYuzde = 10m, SadeceKendiSubeleri = true }));
    }

    // ---------------------------------------------------------------------------------------
    // 3) DOLULUK TOPLU KADEME GİRİŞİ
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Toplu giriş 3 kademeyi tek çağrıda yazar (kod = ÖNEK-eşik) ve MOTORU değiştirmez:
    /// %84 doluluk → aday kademeler %60 ve %80; motor en yüksek eşiği seçer (%80 → +%20)
    /// → 1000 × 1,20 = <b>1200</b>/gün, 5 gün = <b>6000</b> (elle).
    /// </summary>
    [Fact]
    public async Task Toplu_kademe_girisi_yazar_ve_motor_en_yuksek_esigi_secer()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedFiloAsync(sp);
        var svc = sp.GetRequiredService<DolulukFiyatKuralService>();

        var idler = await svc.TopluCreateAsync(new DolulukTopluInput
        {
            KodOnEk = "yaz-eko", AdOnEk = "Yaz Doluluk", AracGrupKod = "EKO",
            Kademeler = [new(60, 5m), new(80, 20m), new(95, 40m)]
        });
        Assert.Equal(3, idler.Count);

        var hepsi = await svc.ListAsync();
        Assert.Equal(3, hepsi.Count);
        Assert.Contains(hepsi, k => k.Kod == "YAZ-EKO-60" && k.EsikYuzde == 60 && k.CarpanYuzde == 5m);
        Assert.Contains(hepsi, k => k.Kod == "YAZ-EKO-80" && k.Ad == "Yaz Doluluk %80");
        Assert.Contains(hepsi, k => k.Kod == "YAZ-EKO-95" && k.CarpanYuzde == 40m);

        var q = await Teklif(sp);
        Assert.Equal(1200m, q.GunlukUcret);
        Assert.Equal(6000m, q.BazTutar);
    }

    /// <summary>
    /// YARIM MERDİVEN YASAK: bir satır geçersizse HİÇBİRİ yazılmaz. Burada 3. satırın çarpanı %70
    /// (tavan %50) — reddedilir ve tablo BOŞ kalır (ilk iki satır sızmaz).
    /// Ayrıca aynı eşiğin iki kez girilmesi de reddedilir.
    /// </summary>
    [Fact]
    public async Task Toplu_giriste_tek_gecersiz_satir_hepsini_reddeder()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<DolulukFiyatKuralService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.TopluCreateAsync(new DolulukTopluInput
        {
            KodOnEk = "A", AdOnEk = "A", Kademeler = [new(60, 5m), new(80, 20m), new(95, 70m)]
        }));
        Assert.Empty(await svc.ListAsync());

        await Assert.ThrowsAsync<ValidationException>(() => svc.TopluCreateAsync(new DolulukTopluInput
        {
            KodOnEk = "B", AdOnEk = "B", Kademeler = [new(60, 5m), new(60, 20m)]
        }));
        Assert.Empty(await svc.ListAsync());
    }

    // ---------------------------------------------------------------------------------------
    // 4) KAMPANYA 5-DURUM (wire-in regresyonu)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// <b>Spec'in istediği wire-in regresyonu:</b> yalnız <see cref="KampanyaDurum.Aktif"/> kural
    /// motorda otomatik seçilir. Elle sabit: tarife 1000/gün × 5 = 5000 iskontosuz; %10 iskonto
    /// açıldığında 4500. Planlandı/Taslak/Pasif/İptal durumlarında toplam 5000 KALIR.
    /// (Doluluk kuralı bilerek yok — bu test kural seçimini ölçer.)
    /// </summary>
    [Fact]
    public async Task Yalniz_aktif_durum_motorda_secilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedFiloAsync(sp);
        var kurallar = sp.GetRequiredService<RentalRuleService>();

        var id = await kurallar.CreateAsync(new RentalRuleInput
        { Kod = "KAMP", Ad = "Kampanya", Iskonto = 10m, KampanyaDurum = KampanyaDurum.Aktif });
        Assert.Equal(4500m, (await Teklif(sp)).GenelToplam);   // 5000 − 500

        foreach (var durum in new[]
                 { KampanyaDurum.Planlandi, KampanyaDurum.Taslak, KampanyaDurum.Pasif, KampanyaDurum.Iptal })
        {
            await kurallar.UpdateAsync(id, new RentalRuleInput
            { Kod = "KAMP", Ad = "Kampanya", Iskonto = 10m, KampanyaDurum = durum });
            Assert.Equal(durum, (await kurallar.GetAsync(id))!.KampanyaDurum);
            Assert.Empty(await kurallar.ListActiveAsync());
            Assert.Equal(5000m, (await Teklif(sp)).GenelToplam);   // iskonto UYGULANMAZ
        }

        // Aktife dönünce iskonto geri gelir (durum gerçekten okunuyor, kalıcı bir yan etki değil).
        await kurallar.UpdateAsync(id, new RentalRuleInput
        { Kod = "KAMP", Ad = "Kampanya", Iskonto = 10m, KampanyaDurum = KampanyaDurum.Aktif });
        Assert.Equal(4500m, (await Teklif(sp)).GenelToplam);
    }

    /// <summary>
    /// <c>Aktif</c> ↔ <c>KampanyaDurum</c> DEĞİŞMEZİ: iki alan tek yazma noktasından senkron gider.
    /// (a) Durum verilmezse eski <c>Aktif</c> bayrağından türetilir — migration backfill'iyle AYNI
    /// eşleme (true→Aktif, false→Pasif); eski çağıranlar davranış değiştirmez.
    /// (b) Durum verilirse <c>Aktif</c> ondan türetilir.
    /// (c) DB CHECK (<c>ck_kural_durum_senkron</c>) ayrışmayı yapısal olarak engeller.
    /// </summary>
    [Fact]
    public async Task Aktif_ve_kampanya_durumu_senkron_kalir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        var kurallar = sp.GetRequiredService<RentalRuleService>();

        // (a) Eski yol: yalnız Aktif bayrağı.
        var a = await kurallar.CreateAsync(new RentalRuleInput { Kod = "A", Ad = "A", Aktif = true });
        var p = await kurallar.CreateAsync(new RentalRuleInput { Kod = "P", Ad = "P", Aktif = false });
        Assert.Equal(KampanyaDurum.Aktif, (await kurallar.GetAsync(a))!.KampanyaDurum);
        Assert.Equal(KampanyaDurum.Pasif, (await kurallar.GetAsync(p))!.KampanyaDurum);
        Assert.Single(await kurallar.ListActiveAsync());        // yalnız A

        // (b) Yeni yol: durum verilir, Aktif ondan türetilir.
        await kurallar.UpdateAsync(a, new RentalRuleInput
        { Kod = "A", Ad = "A", KampanyaDurum = KampanyaDurum.Iptal, Aktif = true });   // Aktif=true YOK SAYILIR
        var guncel = (await kurallar.GetAsync(a))!;
        Assert.Equal(KampanyaDurum.Iptal, guncel.KampanyaDurum);
        Assert.False(guncel.Aktif);

        // (c) DB çiti: iki kolonu elle ayrıştırmak CHECK ihlaliyle reddedilir.
        var db = sp.GetRequiredService<IDbContextFactory<RentACar.Infrastructure.Persistence.AppDbContext>>();
        await using var ctx = await db.CreateDbContextAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.ExecuteSqlRawAsync(
            "UPDATE \"KiralamaKurallari\" SET \"Aktif\" = true WHERE \"Kod\" = 'A'"));
    }

    /// <summary>Kampanya arama süzgeci (SAF fonksiyon) — elle kurulmuş 4 kayıt üzerinde beklenen
    /// eşleşme sayıları. Filtre yalnız GÖRÜNÜMÜ daraltır; motor bu yolu kullanmaz.</summary>
    [Fact]
    public async Task Kampanya_arama_suzgeci_dogru_daraltir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RentalRuleService>();
        var t1 = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero);

        await svc.CreateAsync(new RentalRuleInput
        {
            Kod = "YAZ", Ad = "Yaz Kampanyası", Kanal = "WEB", KampanyaMi = true,
            GecerlilikBas = t1, GecerlilikBit = t2, KampanyaDurum = KampanyaDurum.Aktif
        });
        await svc.CreateAsync(new RentalRuleInput
        {
            Kod = "KIS", Ad = "Kış Kampanyası", Kanal = "ACENTA", KampanyaMi = true,
            GecerlilikBas = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero),
            KampanyaDurum = KampanyaDurum.Planlandi, TarihTipi = KuralTarihTipi.Talep
        });
        await svc.CreateAsync(new RentalRuleInput
        { Kod = "GENEL", Ad = "Genel indirim", KampanyaDurum = KampanyaDurum.Aktif });
        await svc.CreateAsync(new RentalRuleInput
        { Kod = "ESKI", Ad = "Eski", KampanyaDurum = KampanyaDurum.Iptal });

        Assert.Equal(4, (await svc.SearchAsync()).Count);
        Assert.Equal(2, (await svc.SearchAsync(new RentalRuleFilter { Durum = KampanyaDurum.Aktif })).Count);
        Assert.Single(await svc.SearchAsync(new RentalRuleFilter { Durum = KampanyaDurum.Planlandi }));
        Assert.Single(await svc.SearchAsync(new RentalRuleFilter { TarihTipi = KuralTarihTipi.Talep }));
        Assert.Equal(2, (await svc.SearchAsync(new RentalRuleFilter { KampanyaMi = true })).Count);
        Assert.Single(await svc.SearchAsync(new RentalRuleFilter { Kanal = "web" }));   // harf duyarsız
        // Türkçe harf duyarsız metin araması: "kis" → "Kış Kampanyası".
        Assert.Single(await svc.SearchAsync(new RentalRuleFilter { Terim = "kis" }));
        // Aralık ÇAKIŞMASI: 1-15 Temmuz penceresi YAZ ile kesişir; KIS (Aralık) ve açık uçlular da
        // (GENEL/ESKI süresiz) kesişir → 3. Kapsama arasaydık yalnız YAZ dönerdi.
        Assert.Equal(3, (await svc.SearchAsync(new RentalRuleFilter
        {
            GecerliBas = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            GecerliBit = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero)
        })).Count);
    }

    // ---------------------------------------------------------------------------------------
    // 5) BROKER MÜSAİTLİK ÇİTİ (saf fonksiyon)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Broker çiti kapsam kurallarının elle kurulmuş doğruluk tablosu. Yasak: kaynak "Acenta A",
    /// gruplar "EKO,STD", min gün 3 → 2 günlük EKO kirası ENGELLİ; 3 günlük veya LUX grubu ya da
    /// başka kaynak SERBEST. Kaynak seçilmemişse çit hiç çalışmaz.
    /// </summary>
    [Fact]
    public void Broker_citi_kapsam_ve_kisit_dogru()
    {
        var y = new BrokerYasak
        {
            Kod = "Y1", Ad = "Acenta kısıtı", Kaynak = "Acenta A",
            AracGrupKod = "EKO,STD", MinGun = 3, Aktif = true
        };
        var liste = new List<BrokerYasak> { y };
        var t = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.NotNull(BrokerMusaitlik.Engel(liste, "Acenta A", "EKO", null, 2, t));   // 2 < 3 → engel
        Assert.Null(BrokerMusaitlik.Engel(liste, "Acenta A", "EKO", null, 3, t));      // 3 ≥ 3 → serbest
        Assert.Null(BrokerMusaitlik.Engel(liste, "Acenta A", "LUX", null, 2, t));      // kapsam dışı grup
        Assert.Null(BrokerMusaitlik.Engel(liste, "Acenta B", "EKO", null, 2, t));      // başka kaynak
        Assert.Null(BrokerMusaitlik.Engel(liste, null, "EKO", null, 2, t));            // kaynak seçilmemiş

        // Tüm satış kapalı: gün'e bakılmaz.
        var kapali = new List<BrokerYasak>
        {
            new() { Kod = "Y2", Ad = "Kapalı", Kaynak = "Acenta A", TumSatisKapali = true, Aktif = true }
        };
        Assert.NotNull(BrokerMusaitlik.Engel(kapali, "Acenta A", "EKO", null, 30, t));

        // Geçerlilik penceresi dışı → engel yok.
        var gecmis = new List<BrokerYasak>
        {
            new()
            {
                Kod = "Y3", Ad = "Geçmiş", Kaynak = "Acenta A", TumSatisKapali = true, Aktif = true,
                GecerlilikBit = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)
            }
        };
        Assert.Null(BrokerMusaitlik.Engel(gecmis, "Acenta A", "EKO", null, 5, t));
    }

    /// <summary>
    /// Broker çiti /musaitlik yüzeyinin veri yolunu kurar: eleme LİSTE üzerinde olur ve elenmeyen
    /// grubun FİYATI motordan geldiği gibi kalır (elle: 5 gün × 1000 = 5000). Yüzey fiyat hesaplamaz.
    /// </summary>
    [Fact]
    public async Task Broker_citi_listeyi_daraltir_fiyat_motordan_gelir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedFiloAsync(sp);
        await sp.GetRequiredService<BrokerYasakService>().CreateAsync(new BrokerYasakInput
        { Kod = "Y1", Ad = "EKO kapalı", Kaynak = "Acenta A", AracGrupKod = "EKO", TumSatisKapali = true });

        var yasaklar = await sp.GetRequiredService<BrokerYasakService>().ListActiveAsync();
        Assert.NotNull(BrokerMusaitlik.Engel(yasaklar, "Acenta A", "EKO", null, 5, Bas));
        Assert.Null(BrokerMusaitlik.Engel(yasaklar, "Acenta B", "EKO", null, 5, Bas));

        // Çit fiyata dokunmaz: motor aynı teklifi verir (5 gün × 1000 = 5000 — elle).
        var q = await Teklif(sp);
        Assert.Equal(1000m, q.GunlukUcret);
        Assert.Equal(5000m, q.GenelToplam);
    }

    // ---------------------------------------------------------------------------------------
    // 6) YETKİ + TENANT İZOLASYONU (racar_app)
    // ---------------------------------------------------------------------------------------

    /// <summary>Toplu giriş de OperationsWrite ister (tekil CreateAsync ile aynı guard).</summary>
    [Fact]
    public async Task Toplu_giris_yetki_ister()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "muh", UserRole.Muhasebe);
        var svc = scope.ServiceProvider.GetRequiredService<DolulukFiyatKuralService>();
        await Assert.ThrowsAsync<YetkiYokException>(() => svc.TopluCreateAsync(new DolulukTopluInput
        { KodOnEk = "X", AdOnEk = "X", Kademeler = [new(80, 10m)] }));
    }

    /// <summary>Yeni alanlar tenant sızdırmaz: iki tenant, racar_app bağlantısı (fixture öyle ayarlı).</summary>
    [Fact]
    public async Task Yeni_alanlar_tenant_izole()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
        {
            await s1.ServiceProvider.GetRequiredService<BranchService>()
                .CreateAsync(new BranchInput { Kod = "MRK", Ad = "Merkez" });
            await s1.ServiceProvider.GetRequiredService<DolulukFiyatKuralService>()
                .CreateAsync(new DolulukFiyatKuralInput
                {
                    Kod = "D80", Ad = "T1", EsikYuzde = 80, CarpanYuzde = 10m,
                    Sube = "Merkez", SadeceKendiSubeleri = true
                });
            await s1.ServiceProvider.GetRequiredService<RentalRuleService>()
                .CreateAsync(new RentalRuleInput { Kod = "K1", Ad = "T1", KampanyaDurum = KampanyaDurum.Planlandi });
        }

        using var s2 = host.ScopeFor(t2);
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<DolulukFiyatKuralService>().ListAsync());
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<RentalRuleService>().ListAsync());

        // Aynı kodlar t2'de serbestçe açılabilir (izolasyon gerçek, benzersizlik tenant içi).
        await s2.ServiceProvider.GetRequiredService<DolulukFiyatKuralService>()
            .CreateAsync(new DolulukFiyatKuralInput { Kod = "D80", Ad = "T2", EsikYuzde = 80, CarpanYuzde = 10m });
        await s2.ServiceProvider.GetRequiredService<RentalRuleService>()
            .CreateAsync(new RentalRuleInput { Kod = "K1", Ad = "T2" });
        Assert.Single(await s2.ServiceProvider.GetRequiredService<DolulukFiyatKuralService>().ListAsync());
        Assert.Single(await s2.ServiceProvider.GetRequiredService<RentalRuleService>().ListAsync());
    }
}
