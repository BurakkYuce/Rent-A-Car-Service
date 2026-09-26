using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.RentalRules;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-46 — kira listesi kolon/süzgeç derinliği + kiralama kuralı derinliği.
///
/// <para><b>Bağımsız oracle:</b> her senaryoda kaç kaydın dönmesi gerektiği ELLE kurulan veriden
/// sayılır (ör. "3 kiradan yalnız 1'inin vadesi aralıkta → 1"), servis kodundan türetilmez.</para>
///
/// <para>Kilitlenen sözleşmeler: (1) yeni kolonlar projeksiyona taşındı ve HESAP yapmıyor,
/// (2) her süzgeç gerçekten daraltıyor ve BOŞKEN daraltmıyor, (3) tarih türü seçimi aralığı
/// doğru kolona uyguluyor, (4) kural formu iki kez kaydedilince hiçbir alan kaymıyor.</para>
/// </summary>
[Collection("postgres")]
public sealed class KiraListesiKuralDerinlikTests(PostgresFixture fx)
{
    /// <summary>PG timestamptz mikrosaniye, .NET tick 100ns — round-trip eşitliği Linux CI'da
    /// düşmesin diye tarih tabanı TAM SANİYEYE hizalanır.</summary>
    private static DateTimeOffset Taban(int gunSonra)
    {
        var t = new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(gunSonra), DateTimeKind.Utc), TimeSpan.Zero);
        return t.AddTicks(-(t.Ticks % TimeSpan.TicksPerSecond));
    }

    private static int _no;

    private static Task<Guid> CariAsync(IServiceScope s, string ad)
        => s.ServiceProvider.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = ad, Soyad = "Test" });

    private static Task<Guid> AracAsync(IServiceScope s, string plaka, string? grup = null, string? sahip = null)
        => s.ServiceProvider.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = plaka, Grup = grup, AracSahibi = sahip });

    /// <summary>Kira kurar ve yeni FAZ-46 alanlarını doldurur (repo üzerinden — mega-formu taklit etmez).</summary>
    private static async Task<Guid> KiraAsync(
        TestHost host, Guid tenant, Guid cari, Guid arac,
        DateTimeOffset bas, DateTimeOffset bit,
        string? cikisOfisi = null, string? donusOfisi = null,
        DateTimeOffset? vade = null, string? kaynak = null, Guid? personel = null)
    {
        using var scope = host.ScopeFor(tenant);
        var factory = scope.ServiceProvider
            .GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<RentACar.Infrastructure.Persistence.AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        // Sözleşme no burada boşluksuz sıradan DEĞİL, testin kendi sayacından üretiliyor:
        // bu testler numara üretimini değil LİSTE projeksiyonu/süzgeçlerini sınıyor.
        var no = Interlocked.Increment(ref _no);
        var r = new RentACar.Domain.Entities.RentalContract
        {
            SozlesmeNo = $"KS-T{no:D6}",
            MusteriId = cari, VehicleId = arac,
            BasTar = bas, BitTar = bit, Gun = 3,
            GunlukUcret = 100m, Tutar = 300m, GenelToplam = 300m, Bakiye = 300m,
            CikisOfisi = cikisOfisi, DonusOfisi = donusOfisi,
            VadeTar = vade, Kaynak = kaynak, TeslimAlanPersonelId = personel,
            // Bilgi alanları — listede görünmeli, hiçbir toplama girmemeli.
            Provizyon = 500m, Depozito = 250m, KomisyonOran = 12.5m, KomisyonTutar = 37.5m,
            OnayKodu = "ONY-1", ProjeAdi = "Proje X", AssistFirma = "Assist A",
            OzelSoforBilgisi = "Şoförlü", HediyeGun = 1, FaturalananGun = 2
        };
        db.Rentals.Add(r);
        await db.SaveChangesAsync();
        return r.Id;
    }

    // ---------------------------------------------------------------- Kira listesi

    [Fact]
    public async Task Yeni_kolonlar_projeksiyona_tasindi_ve_hesap_yapmiyor()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        Guid cari, arac;
        using (var s = host.ScopeFor(tenant))
        {
            cari = await CariAsync(s, "Kolon");
            arac = await AracAsync(s, "34 KL 01");
        }
        var vade = Taban(10);
        await KiraAsync(host, tenant, cari, arac, Taban(0), Taban(3), "A Ofis", "B Ofis", vade, "Web");

        using var oku = host.ScopeFor(tenant);
        var satir = Assert.Single(await oku.ServiceProvider.GetRequiredService<RentalService>().SearchAsync(new RentalFilter()));

        // Elle kurulan değerler AYNEN dönmeli (projeksiyon, hesap değil).
        Assert.Equal("Web", satir.Kaynak);
        Assert.Equal(500m, satir.Provizyon);
        Assert.Equal(250m, satir.Depozito);
        Assert.Equal(12.5m, satir.KomisyonOran);
        Assert.Equal(37.5m, satir.KomisyonTutar);
        Assert.Equal(vade, satir.VadeTar);
        Assert.Equal("ONY-1", satir.OnayKodu);
        Assert.Equal("Proje X", satir.ProjeAdi);
        Assert.Equal("Assist A", satir.AssistFirma);
        Assert.Equal("Şoförlü", satir.OzelSoforBilgisi);
        Assert.Equal(1, satir.HediyeGun);
        Assert.Equal(2, satir.FaturalananGun);
        Assert.Equal("A Ofis", satir.CikisOfisi);
        Assert.Equal("B Ofis", satir.DonusOfisi);

        // KRİTİK: bilgi alanları tutara/bakiyeye KARIŞMADI (elle: 300 kurulmuştu).
        Assert.Equal(300m, satir.Tutar);
        Assert.Equal(300m, satir.Bakiye);
    }

    [Fact]
    public async Task Tarih_turu_araligi_dogru_kolona_uygular()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        Guid cari, a1, a2;
        using (var s = host.ScopeFor(tenant))
        {
            cari = await CariAsync(s, "Tarih");
            a1 = await AracAsync(s, "34 TR 01");
            a2 = await AracAsync(s, "34 TR 02");
        }
        // ELLE: iki kira. Vadeler 10 ve 40 gün sonra; başlangıçlar AYNI pencerede.
        await KiraAsync(host, tenant, cari, a1, Taban(0), Taban(3), vade: Taban(10));
        await KiraAsync(host, tenant, cari, a2, Taban(0), Taban(3), vade: Taban(40));

        var svc = host.ScopeFor(tenant).ServiceProvider.GetRequiredService<RentalService>();

        // Vade penceresi [5, 20] → yalnız BİRİ.
        var vadeli = await svc.SearchAsync(new RentalFilter
        { TarihTuru = DateListType.Vade, BaslangicMin = Taban(5), BaslangicMax = Taban(20) });
        Assert.Single(vadeli);

        // AYNI pencere Başlangıç'a uygulanınca HİÇBİRİ (ikisi de bugün başlıyor) — aralık
        // gerçekten seçilen kolona uygulanıyor, sessizce BasTar'a düşmüyor.
        var baslangicli = await svc.SearchAsync(new RentalFilter
        { TarihTuru = DateListType.Baslangic, BaslangicMin = Taban(5), BaslangicMax = Taban(20) });
        Assert.Empty(baslangicli);

        // Tür verilmezse eski davranış = Başlangıç.
        Assert.Empty(await svc.SearchAsync(new RentalFilter { BaslangicMin = Taban(5), BaslangicMax = Taban(20) }));

        // Vadesi GİRİLMEMİŞ kayıt vade aralığına düşmez.
        Guid a3;
        using (var s = host.ScopeFor(tenant)) a3 = await AracAsync(s, "34 TR 03");
        await KiraAsync(host, tenant, cari, a3, Taban(0), Taban(3)); // vade null
        Assert.Single(await svc.SearchAsync(new RentalFilter
        { TarihTuru = DateListType.Vade, BaslangicMin = Taban(5), BaslangicMax = Taban(20) }));
    }

    [Fact]
    public async Task Ofis_durumu_cikis_ve_donus_ayirir()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        Guid cari, a1, a2;
        using (var s = host.ScopeFor(tenant))
        {
            cari = await CariAsync(s, "Ofis");
            a1 = await AracAsync(s, "34 OF 01");
            a2 = await AracAsync(s, "34 OF 02");
        }
        // ELLE: 1 kira A'dan çıkıp B'ye dönüyor; 1 kira B'den çıkıp A'ya dönüyor.
        await KiraAsync(host, tenant, cari, a1, Taban(0), Taban(3), "A Ofis", "B Ofis");
        await KiraAsync(host, tenant, cari, a2, Taban(0), Taban(3), "B Ofis", "A Ofis");

        var svc = host.ScopeFor(tenant).ServiceProvider.GetRequiredService<RentalService>();

        Assert.Single(await svc.SearchAsync(new RentalFilter { Ofis = "A Ofis", OfisDurum = OfficeStatus.Cikis }));
        Assert.Single(await svc.SearchAsync(new RentalFilter { Ofis = "A Ofis", OfisDurum = OfficeStatus.Donus }));
        // Seçim yoksa eski davranış: çıkış VEYA dönüş → ikisi de.
        Assert.Equal(2, (await svc.SearchAsync(new RentalFilter { Ofis = "A Ofis" })).Count);
    }

    [Fact]
    public async Task Arac_kaynak_ve_personel_suzgecleri_daraltir()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        var personel = Guid.NewGuid();
        Guid cari, ekonomik, lux;
        using (var s = host.ScopeFor(tenant))
        {
            cari = await CariAsync(s, "Suzgec");
            ekonomik = await AracAsync(s, "34 SZ 01", grup: "EKO", sahip: "Filo A");
            lux = await AracAsync(s, "34 SZ 02", grup: "LUX", sahip: "Filo B");
        }
        await KiraAsync(host, tenant, cari, ekonomik, Taban(0), Taban(3), kaynak: "Web", personel: personel);
        await KiraAsync(host, tenant, cari, lux, Taban(0), Taban(3), kaynak: "Acente");

        var svc = host.ScopeFor(tenant).ServiceProvider.GetRequiredService<RentalService>();

        Assert.Equal("34SZ01", Assert.Single(await svc.SearchAsync(new RentalFilter { AracGrubu = "EKO" })).Plaka.Replace(" ", ""));
        Assert.Single(await svc.SearchAsync(new RentalFilter { SahipGrup = "Filo B" }));
        Assert.Single(await svc.SearchAsync(new RentalFilter { RezKaynak = "Acente" }));
        Assert.Single(await svc.SearchAsync(new RentalFilter { PersonelId = personel }));

        // Eşleşmeyen araç süzgeci BOŞ döner (erken çıkış yolu).
        Assert.Empty(await svc.SearchAsync(new RentalFilter { AracGrubu = "YOK" }));
        // Süzgeçler BOŞKEN daraltmaz — 2 kayıt.
        Assert.Equal(2, (await svc.SearchAsync(new RentalFilter())).Count);
    }

    [Fact]
    public async Task Kira_listesi_tenant_izolasyonu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        Guid cari, arac;
        using (var s = host.ScopeFor(t1))
        {
            cari = await CariAsync(s, "T1");
            arac = await AracAsync(s, "34 TN 01", grup: "EKO");
        }
        await KiraAsync(host, t1, cari, arac, Taban(0), Taban(3), kaynak: "Web");

        // racar_app ile bağlanan T2 bağlamı T1'in kirasını GÖRMEZ (süzgeçli sorgu dahil).
        using var s2 = host.ScopeFor(t2);
        var svc2 = s2.ServiceProvider.GetRequiredService<RentalService>();
        Assert.Empty(await svc2.SearchAsync(new RentalFilter()));
        Assert.Empty(await svc2.SearchAsync(new RentalFilter { AracGrubu = "EKO" }));
        Assert.Empty(await svc2.SearchAsync(new RentalFilter { RezKaynak = "Web" }));
    }

    // ---------------------------------------------------------------- Kiralama kuralı

    [Fact]
    public async Task Kural_yeni_alanlari_round_trip_ve_iki_kayitta_kaymiyor()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RentalRuleService>();

        var talepBas = Taban(1);
        var talepBit = Taban(20);
        var girdi = new RentalRuleInput
        {
            Kod = "erk-rez", Ad = "Erken Rezervasyon",
            Sube = "Merkez", AracGrupKod = "eko",
            Iskonto = 12.5m,
            TalepBas = talepBas, TalepBit = talepBit,
            PromosyonTuru = PromotionType.Tek,
            KuponGecerlilik = CouponValidity.SadeceIlkBedel,
            HesaplamaTipi = CalculationType.Serbest,
            HizliIslem = true,
            HaftaGunKisiti = "6,0,6"   // tekrar + sırasız → normalize edilmeli
        };
        var id = await svc.CreateAsync(girdi);

        var kural = Assert.Single((await svc.ListAsync()).Where(x => x.Id == id));
        Assert.Equal(talepBas, kural.TalepBas);
        Assert.Equal(talepBit, kural.TalepBit);
        Assert.Equal(PromotionType.Tek, kural.PromosyonTuru);
        Assert.Equal(CouponValidity.SadeceIlkBedel, kural.KuponGecerlilik);
        Assert.Equal(CalculationType.Serbest, kural.HesaplamaTipi);
        Assert.True(kural.HizliIslem);
        Assert.Equal("0,6", kural.HaftaGunKisiti);   // tekilleştirildi + sıralandı
        Assert.Equal("Merkez", kural.Sube);
        Assert.Equal("EKO", kural.AracGrupKod);

        // Aynı girdi İKİNCİ kez kaydedilince hiçbir alan kaymamalı (Normalize yeni nesne kuruyor —
        // eklenmeyen alanın sessizce kaybolduğu bilinen tuzak burada kilitleniyor).
        await svc.UpdateAsync(id, girdi);
        var tekrar = Assert.Single((await svc.ListAsync()).Where(x => x.Id == id));
        Assert.Equal(kural.TalepBas, tekrar.TalepBas);
        Assert.Equal(kural.TalepBit, tekrar.TalepBit);
        Assert.Equal(kural.PromosyonTuru, tekrar.PromosyonTuru);
        Assert.Equal(kural.KuponGecerlilik, tekrar.KuponGecerlilik);
        Assert.Equal(kural.HesaplamaTipi, tekrar.HesaplamaTipi);
        Assert.Equal(kural.HizliIslem, tekrar.HizliIslem);
        Assert.Equal(kural.HaftaGunKisiti, tekrar.HaftaGunKisiti);
        Assert.Equal(kural.Iskonto, tekrar.Iskonto);
        Assert.Equal(kural.Sube, tekrar.Sube);
    }

    [Fact]
    public void HaftaGun_gecersiz_deger_gurultulu_reddedilir()
    {
        // Sessizce atmak, kullanıcının kurduğunu sandığı kısıtı yok ederdi.
        Assert.Throws<ValidationException>(() => RentalRuleService.NormalizeWeekday("7"));
        Assert.Throws<ValidationException>(() => RentalRuleService.NormalizeWeekday("-1"));
        Assert.Throws<ValidationException>(() => RentalRuleService.NormalizeWeekday("Pzt"));
        Assert.Null(RentalRuleService.NormalizeWeekday(null));
        Assert.Null(RentalRuleService.NormalizeWeekday("   "));
        Assert.Equal("1,3,5", RentalRuleService.NormalizeWeekday(" 5, 1 ,3, 5 "));
    }

    [Fact]
    public async Task Talep_araligi_tutarsizsa_reddedilir_ve_gecerlilikten_bagimsizdir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RentalRuleService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new RentalRuleInput
        { Kod = "TERS", Ad = "Ters", TalepBas = Taban(20), TalepBit = Taban(1) }));

        // Talep aralığı GEÇERLİLİK aralığından bağımsız: ters geçerlilikle çakışmaz.
        var id = await svc.CreateAsync(new RentalRuleInput
        {
            Kod = "BAGIMSIZ", Ad = "Bağımsız",
            TalepBas = Taban(1), TalepBit = Taban(5),
            GecerlilikBas = Taban(30), GecerlilikBit = Taban(60)
        });
        var k = Assert.Single((await svc.ListAsync()).Where(x => x.Id == id));
        Assert.Equal(Taban(1), k.TalepBas);
        Assert.Equal(Taban(30), k.GecerlilikBas);
    }

    [Fact]
    public async Task Kural_yeni_alanlari_FIYATA_ETKI_ETMEZ()
    {
        // Kırılgan regresyon: yeni alanlar BİLGİDİR; fiyat motoru onları okumaz.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<RentalRuleService>();

        var id = await svc.CreateAsync(new RentalRuleInput { Kod = "SADE", Ad = "Sade", Iskonto = 10m });
        var once = Assert.Single((await svc.ListAsync()).Where(x => x.Id == id));
        var iskontoOnce = once.Iskonto;

        // Uçuk bilgi değerleri yaz — iskonto (fiyatı etkileyen TEK alan) değişmemeli.
        await svc.UpdateAsync(id, new RentalRuleInput
        {
            Kod = "SADE", Ad = "Sade", Iskonto = 10m,
            PromosyonTuru = PromotionType.Coklu,
            KuponGecerlilik = CouponValidity.Hepsi,
            HesaplamaTipi = CalculationType.Serbest,
            HizliIslem = true,
            HaftaGunKisiti = "0,1,2,3,4,5,6",
            TalepBas = Taban(1), TalepBit = Taban(2)
        });
        var sonra = Assert.Single((await svc.ListAsync()).Where(x => x.Id == id));
        Assert.Equal(iskontoOnce, sonra.Iskonto);
        Assert.Equal(10m, sonra.Iskonto);
    }
}
