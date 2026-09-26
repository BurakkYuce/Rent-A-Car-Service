using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.DropTanimlari;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-22 Bölüm 2 — drop matrisi derinliği + fiyat motoru ÖZGÜLLÜK MERDİVENİ (PARA).
///
/// <para><b>ANLAM:</b> <c>DropTanim.Lokasyon</c> DÖNÜŞ ofisidir, <c>Sube</c> ÇIKIŞ şubesidir —
/// motorda böyle eşleşiyor. Faz planı bunun tersini varsayıyordu; testler doğru anlamı kilitliyor.</para>
///
/// <para><b>Geriye uyum sözleşmesi:</b> <c>CikisLokasyon</c>/<c>MinGun</c> null olan satırların
/// davranışı BİRE BİR eskisi gibidir — yeni koşullar yalnız DARALTIR.</para>
///
/// <para>Bağımsız oracle: beklenen ücretler elle kurulan senaryodan yazılır.</para>
/// </summary>
[Collection("postgres")]
public sealed class DropDerinlikTests(PostgresFixture fx)
{
    private static DropTanimInput T(string returnLocation, string pickupBranch, decimal? fee,
        string? pickupLocation = null, int? minDays = null, bool active = true)
        => new()
        {
            Lokasyon = returnLocation, Sube = pickupBranch, Ucret = fee,
            CikisLokasyon = pickupLocation, MinGun = minDays, Aktif = active
        };

    [Fact]
    public async Task Yeni_alanlar_round_trip_ve_GUNCELLEME_calisir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<DropDefinitionService>();

        var id = await svc.CreateAsync(new DropTanimInput
        {
            Lokasyon = " Ankara Ofis ", Sube = " İstanbul ", Ucret = 500m,
            CikisLokasyon = " IST Havalimanı ", MinGun = 3, ManSuresi = 45, Drop2 = 120m
        });

        var d = await svc.GetAsync(id);
        Assert.Equal("Ankara Ofis", d!.Lokasyon);          // trim
        Assert.Equal("İstanbul", d.Sube);
        Assert.Equal("IST Havalimanı", d.CikisLokasyon);   // trim
        Assert.Equal(3, d.MinGun);
        Assert.Equal(45, d.ManSuresi);
        Assert.Equal(120m, d.Drop2);

        // Düzenleme ucu FAZ-22'de eklendi; yeni alanlar güncellemede DÜŞMEMELİ.
        Assert.True(await svc.UpdateAsync(id, new DropTanimInput
        {
            Lokasyon = "Ankara Ofis", Sube = "İstanbul", Ucret = 600m,
            CikisLokasyon = "SAW Havalimanı", MinGun = 5, ManSuresi = 60, Drop2 = 200m, Aktif = false
        }));
        var d2 = await svc.GetAsync(id);
        Assert.Equal(600m, d2!.Ucret);
        Assert.Equal("SAW Havalimanı", d2.CikisLokasyon);
        Assert.Equal(5, d2.MinGun);
        Assert.Equal(60, d2.ManSuresi);
        Assert.Equal(200m, d2.Drop2);
        Assert.False(d2.Aktif);
    }

    [Fact]
    public async Task Mevcut_BENZERSIZLIK_kirilmadi_regresyon()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<DropDefinitionService>();

        await svc.CreateAsync(T("Ankara", "İstanbul", 500m));
        // Benzersizlik CikisLokasyon'u DA kapsar ama NULL'lar HÂLÂ ÇAKIŞIR (NULLS NOT DISTINCT):
        // aynı (Lokasyon, Sube) için ikinci ŞUBE-GENELİ satır eskisi gibi REDDEDİLİR.
        await Assert.ThrowsAnyAsync<Exception>(() => svc.CreateAsync(T("Ankara", "İstanbul", 700m)));
        Assert.Single(await svc.ListAsync());
    }

    [Fact]
    public async Task Gecersiz_sayisal_girdi_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<DropDefinitionService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(T("A", "B", -1m)));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(
            new DropTanimInput { Lokasyon = "A", Sube = "B", Drop2 = -5m }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(T("A", "B", 100m, minDays: 0)));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(
            new DropTanimInput { Lokasyon = "A", Sube = "B", ManSuresi = -1 }));
        Assert.Empty(await svc.ListAsync());
    }

    [Fact]
    public async Task Filtre_ELLE_beklenen_alt_kumeyi_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<DropDefinitionService>();

        // ELLE: 3 kayıt.
        await svc.CreateAsync(T("Ankara", "İstanbul", 500m, pickupLocation: "IST Havalimanı"));
        await svc.CreateAsync(T("Ankara", "İzmir", 400m));
        await svc.CreateAsync(T("Bursa", "İstanbul", 300m, active: false));

        Assert.Equal(3, (await svc.SearchAsync()).Count);
        Assert.Equal(2, (await svc.SearchAsync(new DropTanimFilter { DonusLokasyon = "Ankara" })).Count);
        Assert.Equal(2, (await svc.SearchAsync(new DropTanimFilter { Sube = "İstanbul" })).Count);
        Assert.Single(await svc.SearchAsync(new DropTanimFilter { CikisLokasyon = "IST Havalimanı" }));
        Assert.Equal(2, (await svc.SearchAsync(new DropTanimFilter { Aktif = true })).Count);
        Assert.Single(await svc.SearchAsync(new DropTanimFilter { Aktif = false }));
        // Birleşik: Ankara + İstanbul → 1
        Assert.Single(await svc.SearchAsync(new DropTanimFilter { DonusLokasyon = "Ankara", Sube = "İstanbul" }));

        // KÜLTÜR/COLLATION KİLİDİ: karşılaştırma motorla AYNI (OrdinalIgnoreCase) ve BELLEKTE.
        // SQL lower() kullanılsaydı sonuç DB collation'ına bağlanırdı — PG (en_US.UTF-8)
        // lower('İstanbul')='istanbul' üretirken .NET 'i̇stanbul' (i + U+0307) üretiyor; bu test
        // yerelde geçip CI'da patlamıştı.
        Assert.Equal(2, (await svc.SearchAsync(new DropTanimFilter { Sube = "  İSTANBUL  " })).Count);
        Assert.Equal(2, (await svc.SearchAsync(new DropTanimFilter { DonusLokasyon = "ankara" })).Count);
        Assert.Single(await svc.SearchAsync(new DropTanimFilter { CikisLokasyon = " ist Havalimanı " }));

        // PARİTE (ve ortak SINIR): OrdinalIgnoreCase Türkçe İ/ı'yı katlamaz — "HAVALIMANI"
        // (ASCII I) kayıttaki "Havalimanı" (U+0131) ile eşleşmez. FİLTRE de MOTOR da aynı şekilde
        // eşleşmez; önemli olan ikisinin AYNI davranması (liste, ücret uygulanan bir kuralı
        // gizlemesin). Comparer'ı Türkçeye çevirmek ayrı ve incelemeli bir iş (kodda AÇIK İŞ).
        Assert.Empty(await svc.SearchAsync(new DropTanimFilter { CikisLokasyon = "IST HAVALIMANI" }));
        var fee = s.ServiceProvider.GetRequiredService<FeeLineService>();
        // Doğru yazımda özgül satır seçilir (500). İ/ı sapmasında o satır MOTORDA DA seçilmez —
        // ücret İzmir satırının fallback'inden (400) gelir. İkisi de aynı sınırı yaşıyor.
        Assert.Equal(500m, await fee.ResolveDropFeeAsync("ist Havalimanı", "Ankara", null, 3));
        Assert.Equal(400m, await fee.ResolveDropFeeAsync("IST HAVALIMANI", "Ankara", null, 3));
    }

    // ---------------- Fiyat motoru: özgüllük merdiveni ----------------

    [Fact]
    public async Task ESKI_SATIRLAR_bire_bir_ayni_davranir_geriye_uyum()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<DropDefinitionService>();
        var fee = sp.GetRequiredService<FeeLineService>();

        // CikisLokasyon/MinGun boş = eski satır.
        await svc.CreateAsync(T("Ankara", "İstanbul", 500m));
        await svc.CreateAsync(T("Ankara", "İzmir", 400m));

        // ELLE: çıkış İstanbul → şubeye özel satır 500.
        Assert.Equal(500m, await fee.ResolveDropFeeAsync("İstanbul", "Ankara", null, 3));
        // ELLE: çıkış Antalya (özel satır yok) → fallback, Sube sırasıyla ilk = "İstanbul" → 500.
        Assert.Equal(500m, await fee.ResolveDropFeeAsync("Antalya", "Ankara", null, 3));
        // ELLE: aynı ofis → ücret yok.
        Assert.Null(await fee.ResolveDropFeeAsync("Ankara", "Ankara", null, 3));
        // ELLE: tanımsız dönüş → ücret yok.
        Assert.Null(await fee.ResolveDropFeeAsync("İstanbul", "Bursa", null, 3));
        // Manuel override koşuldan bağımsız; açık 0 = muafiyet.
        Assert.Equal(750m, await fee.ResolveDropFeeAsync("İstanbul", "Ankara", 750m, 3));
        Assert.Null(await fee.ResolveDropFeeAsync("İstanbul", "Ankara", 0m, 3));
    }

    [Fact]
    public async Task CIKIS_LOKASYONU_satiri_sube_satirini_YENER_ve_SON_SOZDUR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<DropDefinitionService>();
        var fee = sp.GetRequiredService<FeeLineService>();

        // Şube geneli 500; IST Havalimanı çıkışına özel 900.
        await svc.CreateAsync(T("Ankara", "İstanbul", 500m));
        await svc.CreateAsync(T("Ankara", "İstanbul", 900m, pickupLocation: "IST Havalimanı"));

        Assert.Equal(900m, await fee.ResolveDropFeeAsync("IST Havalimanı", "Ankara", null, 3));   // ELLE
        Assert.Equal(500m, await fee.ResolveDropFeeAsync("İstanbul", "Ankara", null, 3));         // ELLE

        // SON SÖZ: çıkış-lokasyonu satırı ücretsizse (0) daha genel satırın 500'üne DÜŞÜLMEZ.
        await svc.CreateAsync(T("Bursa", "İstanbul", 500m));
        await svc.CreateAsync(T("Bursa", "İstanbul", 0m, pickupLocation: "SAW Havalimanı"));
        Assert.Null(await fee.ResolveDropFeeAsync("SAW Havalimanı", "Bursa", null, 3));
        Assert.Equal(500m, await fee.ResolveDropFeeAsync("İstanbul", "Bursa", null, 3));

        // Pasif özgül satır da SON SÖZDÜR.
        await svc.CreateAsync(T("İzmir", "İstanbul", 500m));
        await svc.CreateAsync(T("İzmir", "İstanbul", 800m, pickupLocation: "ADB Havalimanı", active: false));
        Assert.Null(await fee.ResolveDropFeeAsync("ADB Havalimanı", "İzmir", null, 3));
    }

    [Fact]
    public async Task ASGARI_GUN_altinda_satir_UYGULANMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<DropDefinitionService>();
        var fee = sp.GetRequiredService<FeeLineService>();

        await svc.CreateAsync(T("Ankara", "İstanbul", 500m, minDays: 5));

        Assert.Null(await fee.ResolveDropFeeAsync("İstanbul", "Ankara", null, 4));      // ELLE: 4 < 5
        Assert.Equal(500m, await fee.ResolveDropFeeAsync("İstanbul", "Ankara", null, 5)); // ELLE: 5 >= 5
        Assert.Equal(500m, await fee.ResolveDropFeeAsync("İstanbul", "Ankara", null, 10));

        // MinGun'suz satır her gün sayısında çalışır (eski davranış).
        await svc.CreateAsync(T("Bursa", "İstanbul", 300m));
        Assert.Equal(300m, await fee.ResolveDropFeeAsync("İstanbul", "Bursa", null, 1));
    }

    [Fact]
    public async Task DARALTMA_UCRETI_ARTIRAMAZ_capraz_sube_fallbacki_yok()
    {
        // ADVERSARIAL H2: daraltma koşulları aday havuzundan ELEYEREK uygulansaydı, şubeye özel
        // 300'lük satıra MinGun eklemek onu havuzdan düşürür, fallback devreye girer ve müşteri
        // BAŞKA şubenin 1000'ini öderdi — bir daraltma kuralı ücreti 700 TL ARTIRIRDI.
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<DropDefinitionService>();
        var fee = sp.GetRequiredService<FeeLineService>();

        await svc.CreateAsync(T("Ankara", "Adana", 1000m));                   // başka şube, pahalı
        await svc.CreateAsync(T("Ankara", "İstanbul", 300m, minDays: 7));      // çıkış şubesi, koşullu

        // ELLE: 3 gün → İstanbul satırının koşulu tutmuyor → rota ÜCRETSİZ (Adana'nın 1000'i DEĞİL).
        Assert.Null(await fee.ResolveDropFeeAsync("İstanbul", "Ankara", null, 3));
        // ELLE: 7 gün → koşul tutuyor → 300.
        Assert.Equal(300m, await fee.ResolveDropFeeAsync("İstanbul", "Ankara", null, 7));

        // Aynı şey CikisLokasyon daraltması için de geçerli.
        await svc.CreateAsync(T("Bursa", "Adana", 1000m));
        await svc.CreateAsync(T("Bursa", "İstanbul", 300m, pickupLocation: "IST Havalimanı"));
        Assert.Null(await fee.ResolveDropFeeAsync("İstanbul", "Bursa", null, 3));            // ELLE
        Assert.Equal(300m, await fee.ResolveDropFeeAsync("IST Havalimanı", "Bursa", null, 3));

        // Çıkış şubesine AİT HİÇ satır yoksa fallback hâlâ çalışır (eski davranış).
        Assert.Equal(1000m, await fee.ResolveDropFeeAsync("Antalya", "Ankara", null, 3));
    }

    [Fact]
    public async Task UCRETSIZ_bilgi_satiri_gercek_ucreti_SUSTURMAZ()
    {
        // ADVERSARIAL H3: DropTanim aslen bir karşılama/iletişim matrisi; çoğu satırda Ucret null.
        // Çıkış-lokasyonlu bir NOT satırı basamağa girseydi gerçek 500'lük ücreti sıfırlardı.
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<DropDefinitionService>();
        var fee = sp.GetRequiredService<FeeLineService>();

        await svc.CreateAsync(T("Ankara", "İstanbul", 500m));
        // Fiyat kararı TAŞIMAYAN (Ucret=null) not satırı — aynı çıkış lokasyonuna bağlı.
        await svc.CreateAsync(new DropTanimInput
        {
            Lokasyon = "Ankara", Sube = "İstanbul", CikisLokasyon = "İstanbul",
            KarsilamaSekli = "Vale ile karşılama", Ucret = null
        });

        Assert.Equal(500m, await fee.ResolveDropFeeAsync("İstanbul", "Ankara", null, 3));   // ELLE

        // AÇIK 0 ise SUSTURUR (fiyat kararıdır: "bu rota ücretsiz").
        await svc.CreateAsync(T("Bursa", "İstanbul", 500m));
        await svc.CreateAsync(T("Bursa", "İzmir", 0m, pickupLocation: "İstanbul"));
        Assert.Null(await fee.ResolveDropFeeAsync("İstanbul", "Bursa", null, 3));
    }

    [Fact]
    public async Task Cikis_lokasyonu_basamaginda_SUBESI_DE_eslesen_satir_kazanir()
    {
        // ADVERSARIAL M4: basamak yalnız Sube'ye göre sıralansaydı, hem lokasyonu hem şubesi
        // eşleşen 200'lük satır yerine alfabetik önce gelen 1000'lik satır uygulanırdı.
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<DropDefinitionService>();
        var fee = sp.GetRequiredService<FeeLineService>();

        await svc.CreateAsync(T("Ankara", "Adana", 1000m, pickupLocation: "IST Havalimanı"));
        await svc.CreateAsync(T("Ankara", "IST Havalimanı", 200m, pickupLocation: "IST Havalimanı"));

        Assert.Equal(200m, await fee.ResolveDropFeeAsync("IST Havalimanı", "Ankara", null, 3));   // ELLE
    }

    [Fact]
    public async Task Ayni_subeden_IKI_FARKLI_cikis_ofisine_ayri_fiyat_verilebilir()
    {
        // ADVERSARIAL M5: benzersizlik (Lokasyon, Sube) olsaydı bu yapılandırma imkânsızdı ve
        // kullanıcı sahte şube adı uydurmak zorunda kalırdı.
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<DropDefinitionService>();
        var fee = sp.GetRequiredService<FeeLineService>();

        await svc.CreateAsync(T("Ankara", "İstanbul", 900m, pickupLocation: "IST Havalimanı"));
        await svc.CreateAsync(T("Ankara", "İstanbul", 600m, pickupLocation: "SAW Havalimanı"));
        await svc.CreateAsync(T("Ankara", "İstanbul", 750m));   // şube geneli (CikisLokasyon NULL)

        Assert.Equal(900m, await fee.ResolveDropFeeAsync("IST Havalimanı", "Ankara", null, 3));
        Assert.Equal(600m, await fee.ResolveDropFeeAsync("SAW Havalimanı", "Ankara", null, 3));
        Assert.Equal(750m, await fee.ResolveDropFeeAsync("İstanbul", "Ankara", null, 3));

        // NULL çiftinin benzersizliği KORUNDU (NULLS NOT DISTINCT): ikinci şube-geneli satır YASAK.
        await Assert.ThrowsAnyAsync<Exception>(() => svc.CreateAsync(T("Ankara", "İstanbul", 800m)));
        // Aynı çıkış lokasyonuyla ikinci satır da yasak.
        await Assert.ThrowsAnyAsync<Exception>(() =>
            svc.CreateAsync(T("Ankara", "İstanbul", 800m, pickupLocation: "IST Havalimanı")));
        Assert.Equal(3, (await svc.ListAsync()).Count);
    }

    [Fact]
    public async Task Cikis_lokasyonu_ESLESMEZSE_satir_hic_aday_olmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<DropDefinitionService>();
        var fee = sp.GetRequiredService<FeeLineService>();

        // TEK satır ve o da başka bir çıkış ofisine bağlı.
        await svc.CreateAsync(T("Ankara", "İstanbul", 500m, pickupLocation: "IST Havalimanı"));

        Assert.Null(await fee.ResolveDropFeeAsync("SAW Havalimanı", "Ankara", null, 3));   // ELLE
        Assert.Equal(500m, await fee.ResolveDropFeeAsync("IST Havalimanı", "Ankara", null, 3));
        // Harf/boşluk farkı eşleşmeyi bozmaz.
        Assert.Equal(500m, await fee.ResolveDropFeeAsync(" ist havalimanı ", "Ankara", null, 3));
    }

    [Fact]
    public async Task Drop2_ve_ManSuresi_UCRETE_GIRMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<DropDefinitionService>();
        var fee = sp.GetRequiredService<FeeLineService>();

        await svc.CreateAsync(new DropTanimInput
        { Lokasyon = "Ankara", Sube = "İstanbul", Ucret = 500m, Drop2 = 250m, ManSuresi = 90 });

        // ELLE: yalnız Ucret döner — Drop2 eklenmez (formülü kalibre edilmedi, bilinçli).
        Assert.Equal(500m, await fee.ResolveDropFeeAsync("İstanbul", "Ankara", null, 3));
    }

    [Fact]
    public async Task Drop_derinligi_tenant_izolasyonlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
            await s1.ServiceProvider.GetRequiredService<DropDefinitionService>()
                .CreateAsync(T("Ankara", "İstanbul", 500m, pickupLocation: "IST"));

        using var s2 = host.ScopeFor(Guid.NewGuid());
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<DropDefinitionService>().SearchAsync());
        Assert.Null(await s2.ServiceProvider.GetRequiredService<FeeLineService>()
            .ResolveDropFeeAsync("İstanbul", "Ankara", null, 3));
    }
}
