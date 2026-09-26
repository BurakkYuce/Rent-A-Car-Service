using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.FaturaDonemleri;
using RentACar.Application.Finance;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-30 — dönem faturası/tahsilatının ELLE tetiklenmesi.
///
/// <para><b>KULLANICI KARARI:</b> elle tetik, Ayarlar'daki <c>DonemselOtomatikTahsilat</c>
/// anahtarından BAĞIMSIZ çalışır ("her gece kendiliğinden çalışsın mı" ile "şu an şunu çalıştır"
/// ayrı sorulardır). Test bunu ampirik kilitliyor: ayar KAPALIYKEN elle tetik çalışıyor.</para>
///
/// <para><b>GÜVENLİK KARARI:</b> job çekirdeği (<c>DonemFaturaUretici</c>) kullanılmıyor — o
/// bilinçli olarak PermissionGuard'sızdır ve kullanıcı-yüzeyli ekrandan çağrılması yetkiyi
/// baypas ederdi. Manuel guard'lı yol (<c>DonemTahsilatService</c>) kullanılıyor; testler yetki
/// ve şube kapsamını doğruluyor.</para>
///
/// <para><b>BAĞIMSIZ ORACLE:</b> 90 günlük 100 TL/gün kira = 9000 toplam; ay-çıpalı plan.
/// Beklenen sayılar elle kurulan senaryodan, servis kodundan DEĞİL.</para>
/// </summary>
[Collection("postgres")]
public sealed class OtomatikTahsilatTests(PostgresFixture fx)
{
    private static async Task<(Guid Kira, Guid Cari)> DonemliKiraAsync(
        IServiceProvider sp, string plaka, string? sube = null)
    {
        // -65 gün: iki ay-çıpalı dönemin kesin geçmişte bitmesi için tampon (job testiyle aynı çıpa).
        var bas = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-65);
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka });
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "Tetik", Soyad = "Musteri" });
        var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = m, VehicleId = v, BasTar = bas, BitTar = bas.AddDays(90),
            GunlukUcret = 100m, DonemselFaturalama = true, CikisOfisi = sube
        });
        return (id, m);
    }

    private static async Task<(decimal Borc, decimal Alacak)> DefterAsync(IServiceProvider sp)
    {
        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        var rows = await db.AccountLedgerEntries.AsNoTracking().ToListAsync();
        return (rows.Where(r => r.Direction == LedgerDirection.Debit).Sum(r => r.Amount.AmountInBase),
                rows.Where(r => r.Direction == LedgerDirection.Credit).Sum(r => r.Amount.AmountInBase));
    }

    [Fact]
    public async Task Ayar_KAPALIYKEN_de_elle_tetik_calisir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<AutoCollectionService>();

        // Ayarlar'a HİÇ DOKUNULMUYOR → DonemselFaturalamaJob ve DonemselOtomatikTahsilat KAPALI
        // (varsayılan). Job bu tenant'ta hiçbir şey kesmez; elle tetik yine de çalışmalı.
        await DonemliKiraAsync(sp, "34 OT 01");

        var adaylar = await svc.CandidatesAsync();
        // ELLE: 65 gün geçmiş → ilk iki ay-çıpalı dönem vadesi gelmiş.
        Assert.Equal(2, adaylar.Count);
        Assert.All(adaylar, a => Assert.True(a.DonemBit <= DateTimeOffset.UtcNow));

        var sonuc = await svc.RunAsync(
            [.. adaylar.Select(a => (a.RentalId, a.DonemSira))], doCollection: true, LedgerAccountType.Kasa);

        Assert.Equal(2, sonuc.Kesilen);
        Assert.Equal(2, sonuc.Tahsilat);
        Assert.Empty(sonuc.Atlananlar);

        var (borc, alacak) = await DefterAsync(sp);
        Assert.Equal(borc, alacak);                 // DENGE

        // Aynı dönemler artık aday DEĞİL (Kesildi).
        Assert.Empty(await svc.CandidatesAsync());
    }

    [Fact]
    public async Task Ayni_donem_IKINCI_kez_calistirilamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<AutoCollectionService>();
        await DonemliKiraAsync(sp, "34 OT 02");

        var adaylar = await svc.CandidatesAsync();
        var secim = adaylar.Select(a => (a.RentalId, a.DonemSira)).ToList();
        Assert.Equal(2, (await svc.RunAsync(secim, true, LedgerAccountType.Kasa)).Kesilen);

        var (borc1, _) = await DefterAsync(sp);

        // AYNI seçim tekrar: dönemler artık aday değil → hepsi ATLANIR, defter DEĞİŞMEZ.
        var ikinci = await svc.RunAsync(secim, true, LedgerAccountType.Kasa);
        Assert.Equal(0, ikinci.Kesilen);
        Assert.Equal(2, ikinci.Atlananlar.Count);

        var (borc2, alacak2) = await DefterAsync(sp);
        Assert.Equal(borc1, borc2);                 // çift fatura/tahsilat YOK
        Assert.Equal(borc2, alacak2);
    }

    [Fact]
    public async Task Tahsilatsiz_calistirma_yalniz_FATURA_keser()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<AutoCollectionService>();
        var (kira, cari) = await DonemliKiraAsync(sp, "34 OT 03");

        var adaylar = await svc.CandidatesAsync();
        var sonuc = await svc.RunAsync(
            [.. adaylar.Select(a => (a.RentalId, a.DonemSira))], doCollection: false, LedgerAccountType.Kasa);

        Assert.Equal(2, sonuc.Kesilen);
        Assert.Equal(0, sonuc.Tahsilat);

        // Fatura kesildi → cari BORÇLANDI; tahsilat yazılmadığı için bakiye borçlu kalmalı.
        Assert.True(await sp.GetRequiredService<CashService>().GetAccountBalanceAsync(cari) > 0m);

        var (borc, alacak) = await DefterAsync(sp);
        Assert.Equal(borc, alacak);
    }

    [Fact]
    public async Task Kapsam_disi_secim_GURULTULU_atlanir_ve_defter_degismez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<AutoCollectionService>();
        await DonemliKiraAsync(sp, "34 OT 04");

        var (borcOnce, _) = await DefterAsync(sp);

        // UYDURMA seçim: var olmayan kira / var olmayan dönem sırası.
        var sonuc = await svc.RunAsync(
            [(Guid.NewGuid(), 1), (Guid.NewGuid(), 99)], doCollection: true, LedgerAccountType.Kasa);
        Assert.Equal(0, sonuc.Kesilen);
        Assert.Equal(2, sonuc.Atlananlar.Count);    // sessizce yutulmadı

        var (borcSonra, alacakSonra) = await DefterAsync(sp);
        Assert.Equal(borcOnce, borcSonra);
        Assert.Equal(borcSonra, alacakSonra);
    }

    [Fact]
    public async Task Filtreler_ELLE_beklenen_alt_kumeyi_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<AutoCollectionService>();

        var (k1, _) = await DonemliKiraAsync(sp, "34 OT 05");
        await DonemliKiraAsync(sp, "34 OT 06");

        var hepsi = await svc.CandidatesAsync();
        Assert.Equal(4, hepsi.Count);               // ELLE: 2 kira × 2 vadesi geçmiş dönem

        // Sözleşme no filtresi: yalnız o kiranın dönemleri.
        var no = hepsi.First(a => a.RentalId == k1).SozlesmeNo;
        var tek = await svc.CandidatesAsync(new OtomatikTahsilatFiltre { SozlesmeNo = no });
        Assert.Equal(2, tek.Count);
        Assert.All(tek, a => Assert.Equal(k1, a.RentalId));

        // Vade aralığı: hiçbir dönemin bitmediği gelecek pencere → boş.
        Assert.Empty(await svc.CandidatesAsync(new OtomatikTahsilatFiltre
        { VadeMin = DateTimeOffset.UtcNow.AddDays(1) }));

        // Yalnız bakiyeli: henüz fatura kesilmediği için cari bakiye 0 → hiçbiri.
        Assert.Empty(await svc.CandidatesAsync(new OtomatikTahsilatFiltre { SadeceBakiyeli = true }));
    }

    [Fact]
    public async Task Yetki_ve_secim_siniri()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using (var admin = host.ScopeFor(tenant)) await DonemliKiraAsync(admin.ServiceProvider, "34 OT 07");

        // Operatör FinanceWrite taşımaz → ne listeyi görebilir ne çalıştırabilir.
        using (var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator, assignedBranch: "Merkez"))
        {
            var svc = op.ServiceProvider.GetRequiredService<AutoCollectionService>();
            await Assert.ThrowsAsync<NoPermissionException>(() => svc.CandidatesAsync());
            await Assert.ThrowsAsync<NoPermissionException>(
                () => svc.RunAsync([(Guid.NewGuid(), 1)], true, LedgerAccountType.Kasa));
        }

        using var mh = host.ScopeFor(tenant, Guid.NewGuid(), "mh", UserRole.Muhasebe);
        var m = mh.ServiceProvider.GetRequiredService<AutoCollectionService>();
        Assert.Equal(2, (await m.CandidatesAsync()).Count);

        // Boş seçim ve üst sınır gürültülü reddedilir.
        await Assert.ThrowsAsync<ValidationException>(() => m.RunAsync([], true, LedgerAccountType.Kasa));
        var cok = Enumerable.Range(0, AutoCollectionService.MaxSelection + 1)
            .Select(i => (Guid.NewGuid(), i)).ToList();
        await Assert.ThrowsAsync<ValidationException>(() => m.RunAsync(cok, true, LedgerAccountType.Kasa));
        // Kasa/Banka dışı hesap reddedilir (gelir/gider hesabına tahsilat yazılamaz).
        await Assert.ThrowsAsync<ValidationException>(
            () => m.RunAsync([(Guid.NewGuid(), 1)], true, LedgerAccountType.Gelir));
    }

    /// <summary>
    /// ADVERSARIAL H1 kalıcı kilidi — kira-başına opt-in çiti. İlk sürümde
    /// <c>DonemselFaturalama</c> sorulmuyordu ve periyodik faturalamayı HİÇ AÇMAMIŞ her uzun kira
    /// ekranda aday çıkıyordu; "hepsini seç + çalıştır" ile kesilmemesi gereken sözleşmelerde
    /// değişmez fatura + tahsilat yazılabiliyordu (ampirik: 2 fatura, 2 tahsilat, defter borç 12200).
    /// Job'un kapısı da bu bayraktır — iki yol AYNI kuralı konuşmalı.
    /// </summary>
    [Fact]
    public async Task Opt_in_KAPALI_kira_aday_DEGILDIR_ve_calistirilamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<AutoCollectionService>();

        // ELLE: iki uzun kira — biri opt-in AÇIK, biri KAPALI. İkisinin de dönem planı var
        // (plan uzunluk kuralıyla kurulur, bayrakla değil).
        var (acik, _) = await DonemliKiraAsync(sp, "34 OT 09");
        var bas = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-65);
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 OT 10" });
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "OptIn", Soyad = "Kapali" });
        var kapali = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = m, VehicleId = v, BasTar = bas, BitTar = bas.AddDays(90),
            GunlukUcret = 100m, DonemselFaturalama = false
        });

        var adaylar = await svc.CandidatesAsync();
        Assert.All(adaylar, a => Assert.Equal(acik, a.RentalId));   // KAPALI kira listede YOK
        Assert.Equal(2, adaylar.Count);

        // Uydurma POST ile kapalı kirayı zorlamak da işe yaramaz: aday çiti reddeder.
        var (borcOnce, _) = await DefterAsync(sp);
        var sonuc = await svc.RunAsync([(kapali, 1)], doCollection: true, LedgerAccountType.Kasa);
        Assert.Equal(0, sonuc.Kesilen);
        Assert.Single(sonuc.Atlananlar);

        var (borcSonra, alacakSonra) = await DefterAsync(sp);
        Assert.Equal(borcOnce, borcSonra);              // kesilmemesi gereken sözleşmede kesim YOK
        Assert.Equal(borcSonra, alacakSonra);
    }

    /// <summary>ADVERSARIAL M1 — sayaç GERÇEĞİ söyler: idempotent yutulan tahsilat "yazıldı"
    /// sayılmaz, atlananlara açıklamasıyla düşer.</summary>
    [Fact]
    public async Task Tahsilat_sayaci_yutulan_tahsilati_SAYMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<AutoCollectionService>();
        var (kira, _) = await DonemliKiraAsync(sp, "34 OT 11");

        var ilk = (await svc.CandidatesAsync()).First();

        // Aynı dönemin tahsilatını ÖNCE manuel yoldan al (deterministik anahtar tüketilir).
        await sp.GetRequiredService<PeriodCollectionService>()
            .IssueAndCollectAsync(ilk.RentalId, ilk.DonemSira, true, LedgerAccountType.Kasa);

        var kasaOnce = (await sp.GetRequiredService<CashService>().ListAsync()).Count;

        // Şimdi elle tetik AYNI dönemi çalıştırsın: fatura zaten kesildiği için aday da değil.
        var sonuc = await svc.RunAsync([(ilk.RentalId, ilk.DonemSira)], true, LedgerAccountType.Kasa);
        var kasaSonra = (await sp.GetRequiredService<CashService>().ListAsync()).Count;

        Assert.Equal(kasaOnce, kasaSonra);              // yeni tahsilat YAZILMADI
        Assert.Equal(0, sonuc.Tahsilat);                // sayaç bunu "yazıldı" saymıyor
    }

    /// <summary>ADVERSARIAL M2 — atlanan mesajları SÖZLEŞME NO taşır; iki farklı kiranın mesajı
    /// birbirinden ayırt edilebilmeli.</summary>
    [Fact]
    public async Task Atlanan_mesajlari_SOZLESME_NO_tasir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<AutoCollectionService>();
        await DonemliKiraAsync(sp, "34 OT 12");
        await DonemliKiraAsync(sp, "34 OT 13");

        var adaylar = await svc.CandidatesAsync();
        // Her kiranın YALNIZ 2. dönemini seç → sıralı kesim kuralı ikisini de reddeder.
        var ikinciler = adaylar.Where(a => a.DonemSira == 2).ToList();
        Assert.Equal(2, ikinciler.Count);

        var sonuc = await svc.RunAsync(
            [.. ikinciler.Select(a => (a.RentalId, a.DonemSira))], true, LedgerAccountType.Kasa);

        Assert.Equal(0, sonuc.Kesilen);
        Assert.Equal(2, sonuc.Atlananlar.Count);
        // İki mesaj BİRBİRİNDEN FARKLI olmalı (sözleşme no ile ayrışıyor).
        Assert.Equal(2, sonuc.Atlananlar.Distinct().Count());
        Assert.All(ikinciler, a => Assert.Contains(sonuc.Atlananlar, m => m.Contains(a.SozlesmeNo)));
    }

    [Fact]
    public async Task Tenant_izolasyonu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var a = host.ScopeFor(Guid.NewGuid())) await DonemliKiraAsync(a.ServiceProvider, "34 OT 08");

        using var b = host.ScopeFor(Guid.NewGuid());
        Assert.Empty(await b.ServiceProvider.GetRequiredService<AutoCollectionService>().CandidatesAsync());
    }
}
