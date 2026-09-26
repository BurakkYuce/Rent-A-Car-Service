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
    private static DateTimeOffset Base(int daysLater)
    {
        var t = new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(daysLater), DateTimeKind.Utc), TimeSpan.Zero);
        return t.AddTicks(-(t.Ticks % TimeSpan.TicksPerSecond));
    }

    private static int _no;

    private static Task<Guid> CustomerAsync(IServiceScope s, string name)
        => s.ServiceProvider.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = name, Soyad = "Test" });

    private static Task<Guid> VehicleAsync(IServiceScope s, string plate, string? group = null, string? owner = null)
        => s.ServiceProvider.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = plate, Grup = group, AracSahibi = owner });

    /// <summary>Kira kurar ve yeni FAZ-46 alanlarını doldurur (repo üzerinden — mega-formu taklit etmez).</summary>
    private static async Task<Guid> RentalAsync(
        TestHost host, Guid tenant, Guid account, Guid vehicle,
        DateTimeOffset start, DateTimeOffset bit,
        string? pickupOffice = null, string? returnOffice = null,
        DateTimeOffset? due = null, string? source = null, Guid? staff = null)
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
            MusteriId = account, VehicleId = vehicle,
            BasTar = start, BitTar = bit, Gun = 3,
            GunlukUcret = 100m, Tutar = 300m, GenelToplam = 300m, Bakiye = 300m,
            CikisOfisi = pickupOffice, DonusOfisi = returnOffice,
            VadeTar = due, Kaynak = source, TeslimAlanPersonelId = staff,
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
        Guid account, vehicle;
        using (var s = host.ScopeFor(tenant))
        {
            account = await CustomerAsync(s, "Kolon");
            vehicle = await VehicleAsync(s, "34 KL 01");
        }
        var due = Base(10);
        await RentalAsync(host, tenant, account, vehicle, Base(0), Base(3), "A Ofis", "B Ofis", due, "Web");

        using var read = host.ScopeFor(tenant);
        var row = Assert.Single(await read.ServiceProvider.GetRequiredService<RentalService>().SearchAsync(new RentalFilter()));

        // Elle kurulan değerler AYNEN dönmeli (projeksiyon, hesap değil).
        Assert.Equal("Web", row.Kaynak);
        Assert.Equal(500m, row.Provizyon);
        Assert.Equal(250m, row.Depozito);
        Assert.Equal(12.5m, row.KomisyonOran);
        Assert.Equal(37.5m, row.KomisyonTutar);
        Assert.Equal(due, row.VadeTar);
        Assert.Equal("ONY-1", row.OnayKodu);
        Assert.Equal("Proje X", row.ProjeAdi);
        Assert.Equal("Assist A", row.AssistFirma);
        Assert.Equal("Şoförlü", row.OzelSoforBilgisi);
        Assert.Equal(1, row.HediyeGun);
        Assert.Equal(2, row.FaturalananGun);
        Assert.Equal("A Ofis", row.CikisOfisi);
        Assert.Equal("B Ofis", row.DonusOfisi);

        // KRİTİK: bilgi alanları tutara/bakiyeye KARIŞMADI (elle: 300 kurulmuştu).
        Assert.Equal(300m, row.Tutar);
        Assert.Equal(300m, row.Bakiye);
    }

    [Fact]
    public async Task Tarih_turu_araligi_dogru_kolona_uygular()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        Guid account, a1, a2;
        using (var s = host.ScopeFor(tenant))
        {
            account = await CustomerAsync(s, "Tarih");
            a1 = await VehicleAsync(s, "34 TR 01");
            a2 = await VehicleAsync(s, "34 TR 02");
        }
        // ELLE: iki kira. Vadeler 10 ve 40 gün sonra; başlangıçlar AYNI pencerede.
        await RentalAsync(host, tenant, account, a1, Base(0), Base(3), due: Base(10));
        await RentalAsync(host, tenant, account, a2, Base(0), Base(3), due: Base(40));

        var svc = host.ScopeFor(tenant).ServiceProvider.GetRequiredService<RentalService>();

        // Vade penceresi [5, 20] → yalnız BİRİ.
        var withDue = await svc.SearchAsync(new RentalFilter
        { TarihTuru = DateListType.Vade, BaslangicMin = Base(5), BaslangicMax = Base(20) });
        Assert.Single(withDue);

        // AYNI pencere Başlangıç'a uygulanınca HİÇBİRİ (ikisi de bugün başlıyor) — aralık
        // gerçekten seçilen kolona uygulanıyor, sessizce BasTar'a düşmüyor.
        var withStart = await svc.SearchAsync(new RentalFilter
        { TarihTuru = DateListType.Baslangic, BaslangicMin = Base(5), BaslangicMax = Base(20) });
        Assert.Empty(withStart);

        // Tür verilmezse eski davranış = Başlangıç.
        Assert.Empty(await svc.SearchAsync(new RentalFilter { BaslangicMin = Base(5), BaslangicMax = Base(20) }));

        // Vadesi GİRİLMEMİŞ kayıt vade aralığına düşmez.
        Guid a3;
        using (var s = host.ScopeFor(tenant)) a3 = await VehicleAsync(s, "34 TR 03");
        await RentalAsync(host, tenant, account, a3, Base(0), Base(3)); // vade null
        Assert.Single(await svc.SearchAsync(new RentalFilter
        { TarihTuru = DateListType.Vade, BaslangicMin = Base(5), BaslangicMax = Base(20) }));
    }

    [Fact]
    public async Task Ofis_durumu_cikis_ve_donus_ayirir()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        Guid account, a1, a2;
        using (var s = host.ScopeFor(tenant))
        {
            account = await CustomerAsync(s, "Ofis");
            a1 = await VehicleAsync(s, "34 OF 01");
            a2 = await VehicleAsync(s, "34 OF 02");
        }
        // ELLE: 1 kira A'dan çıkıp B'ye dönüyor; 1 kira B'den çıkıp A'ya dönüyor.
        await RentalAsync(host, tenant, account, a1, Base(0), Base(3), "A Ofis", "B Ofis");
        await RentalAsync(host, tenant, account, a2, Base(0), Base(3), "B Ofis", "A Ofis");

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
        var staff = Guid.NewGuid();
        Guid account, economic, lux;
        using (var s = host.ScopeFor(tenant))
        {
            account = await CustomerAsync(s, "Suzgec");
            economic = await VehicleAsync(s, "34 SZ 01", group: "EKO", owner: "Filo A");
            lux = await VehicleAsync(s, "34 SZ 02", group: "LUX", owner: "Filo B");
        }
        await RentalAsync(host, tenant, account, economic, Base(0), Base(3), source: "Web", staff: staff);
        await RentalAsync(host, tenant, account, lux, Base(0), Base(3), source: "Acente");

        var svc = host.ScopeFor(tenant).ServiceProvider.GetRequiredService<RentalService>();

        Assert.Equal("34SZ01", Assert.Single(await svc.SearchAsync(new RentalFilter { AracGrubu = "EKO" })).Plaka.Replace(" ", ""));
        Assert.Single(await svc.SearchAsync(new RentalFilter { SahipGrup = "Filo B" }));
        Assert.Single(await svc.SearchAsync(new RentalFilter { RezKaynak = "Acente" }));
        Assert.Single(await svc.SearchAsync(new RentalFilter { PersonelId = staff }));

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

        Guid account, vehicle;
        using (var s = host.ScopeFor(t1))
        {
            account = await CustomerAsync(s, "T1");
            vehicle = await VehicleAsync(s, "34 TN 01", group: "EKO");
        }
        await RentalAsync(host, t1, account, vehicle, Base(0), Base(3), source: "Web");

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

        var requestStart = Base(1);
        var requestEnd = Base(20);
        var input = new RentalRuleInput
        {
            Kod = "erk-rez", Ad = "Erken Rezervasyon",
            Sube = "Merkez", AracGrupKod = "eko",
            Iskonto = 12.5m,
            TalepBas = requestStart, TalepBit = requestEnd,
            PromosyonTuru = PromotionType.Tek,
            KuponGecerlilik = CouponValidity.SadeceIlkBedel,
            HesaplamaTipi = CalculationType.Serbest,
            HizliIslem = true,
            HaftaGunKisiti = "6,0,6"   // tekrar + sırasız → normalize edilmeli
        };
        var id = await svc.CreateAsync(input);

        var rule = Assert.Single((await svc.ListAsync()).Where(x => x.Id == id));
        Assert.Equal(requestStart, rule.TalepBas);
        Assert.Equal(requestEnd, rule.TalepBit);
        Assert.Equal(PromotionType.Tek, rule.PromosyonTuru);
        Assert.Equal(CouponValidity.SadeceIlkBedel, rule.KuponGecerlilik);
        Assert.Equal(CalculationType.Serbest, rule.HesaplamaTipi);
        Assert.True(rule.HizliIslem);
        Assert.Equal("0,6", rule.HaftaGunKisiti);   // tekilleştirildi + sıralandı
        Assert.Equal("Merkez", rule.Sube);
        Assert.Equal("EKO", rule.AracGrupKod);

        // Aynı girdi İKİNCİ kez kaydedilince hiçbir alan kaymamalı (Normalize yeni nesne kuruyor —
        // eklenmeyen alanın sessizce kaybolduğu bilinen tuzak burada kilitleniyor).
        await svc.UpdateAsync(id, input);
        var repeat = Assert.Single((await svc.ListAsync()).Where(x => x.Id == id));
        Assert.Equal(rule.TalepBas, repeat.TalepBas);
        Assert.Equal(rule.TalepBit, repeat.TalepBit);
        Assert.Equal(rule.PromosyonTuru, repeat.PromosyonTuru);
        Assert.Equal(rule.KuponGecerlilik, repeat.KuponGecerlilik);
        Assert.Equal(rule.HesaplamaTipi, repeat.HesaplamaTipi);
        Assert.Equal(rule.HizliIslem, repeat.HizliIslem);
        Assert.Equal(rule.HaftaGunKisiti, repeat.HaftaGunKisiti);
        Assert.Equal(rule.Iskonto, repeat.Iskonto);
        Assert.Equal(rule.Sube, repeat.Sube);
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
        { Kod = "TERS", Ad = "Ters", TalepBas = Base(20), TalepBit = Base(1) }));

        // Talep aralığı GEÇERLİLİK aralığından bağımsız: ters geçerlilikle çakışmaz.
        var id = await svc.CreateAsync(new RentalRuleInput
        {
            Kod = "BAGIMSIZ", Ad = "Bağımsız",
            TalepBas = Base(1), TalepBit = Base(5),
            GecerlilikBas = Base(30), GecerlilikBit = Base(60)
        });
        var k = Assert.Single((await svc.ListAsync()).Where(x => x.Id == id));
        Assert.Equal(Base(1), k.TalepBas);
        Assert.Equal(Base(30), k.GecerlilikBas);
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
        var discountBefore = once.Iskonto;

        // Uçuk bilgi değerleri yaz — iskonto (fiyatı etkileyen TEK alan) değişmemeli.
        await svc.UpdateAsync(id, new RentalRuleInput
        {
            Kod = "SADE", Ad = "Sade", Iskonto = 10m,
            PromosyonTuru = PromotionType.Coklu,
            KuponGecerlilik = CouponValidity.Hepsi,
            HesaplamaTipi = CalculationType.Serbest,
            HizliIslem = true,
            HaftaGunKisiti = "0,1,2,3,4,5,6",
            TalepBas = Base(1), TalepBit = Base(2)
        });
        var after = Assert.Single((await svc.ListAsync()).Where(x => x.Id == id));
        Assert.Equal(discountBefore, after.Iskonto);
        Assert.Equal(10m, after.Iskonto);
    }
}
