using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Pricing;
using RentACar.Application.RateMatrices;
using RentACar.Application.ReservationSources;
using RentACar.Application.VehicleGroups;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-49 — Rezervasyon kaynağı KURAL MATRİSİ. İki yarım ayrı ayrı kilitlenir:
///
/// <para><b>(1) KURAL bayrakları GERÇEKTEN uygulanır.</b> Uzatma yasağı, rezervasyon tarih kilidi,
/// provizyon yasağı, km sınırsızlığı, drop yasağı ve en-fazla-gün için POZİTİF + NEGATİF test çifti
/// yazılır: guard'ın hem çalıştığı hem de yanlışlıkla HER ZAMAN reddetmediği kanıtlanır.</para>
///
/// <para><b>(2) Oran/tutar alanları BİLGİDİR.</b> Komisyon/ön ödeme/indirim/puan oranları ve ek
/// hizmet varsayılan tutarları uçuk değerlerle doldurulup aynı senaryo yeniden fiyatlanır; tutar
/// KURUŞU KURUŞUNA aynı kalmalıdır (KARARLAR.md FAZ-49 + genel politika). Bu test, ileride biri
/// oranı sessizce motora/faturaya bağlarsa KIRILIR — kasıtlı kilit.</para>
///
/// <para>BAĞIMSIZ ORACLE: beklenen değerler elle kurulan senaryodan (3 gün × 900 = 2700 gibi),
/// asla PricingService/RentalQuoteEngine kodundan türetilmez.</para>
/// </summary>
[Collection("postgres")]
public sealed class RezKaynakKuralMatrisiTests(PostgresFixture fx)
{
    // Gün başlangıcına hizalı UTC (tam saniye): DateTimeOffset round-trip eşitliği Linux CI'da
    // 100ns/µs artık-tick farkıyla patlıyordu.
    private static readonly DateTimeOffset Bas = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(3);

    private static Task<Guid> KaynakAsync(IServiceProvider sp, ReservationSourceInput input)
        => sp.GetRequiredService<ReservationSourceService>().CreateAsync(input);

    private static async Task<(Guid Musteri, Guid Arac)> TaraflarAsync(IServiceProvider sp, string plaka)
    {
        var arac = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka });
        var musteri = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CariType.Bireysel, Ad = "Kural", Soyad = "Test" });
        return (musteri, arac);
    }

    private static BookingInput Istek(Guid musteri, Guid arac, string? kaynak, int gun = 3)
        => new()
        {
            MusteriId = musteri, VehicleId = arac,
            BasTar = Bas, BitTar = Bas.AddDays(gun),
            GunlukUcret = 100m, Kaynak = kaynak
        };

    // =====================================================================================
    // (1) KURAL BAYRAKLARI — GERÇEKTEN UYGULANIR (pozitif + negatif çift)
    // =====================================================================================

    /// <summary>Uzatma yasağı: aynı senaryo yalnız bayrakta farklı iki kaynakla kurulur.</summary>
    [Fact]
    public async Task Uzatamaz_kaynakli_kirada_uzatma_reddedilir_serbest_kaynakta_calisir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();

        await KaynakAsync(sp, new ReservationSourceInput { Kod = "BROKER", Ad = "Broker A", Uzatamaz = true });
        await KaynakAsync(sp, new ReservationSourceInput { Kod = "OFIS", Ad = "Ofis Satış" });

        var (m1, a1) = await TaraflarAsync(sp, "34 KM 01");
        var yasakli = await rentals.CreateDirectAsync(Istek(m1, a1, "BROKER"));
        // POZİTİF: kaynak kuralı uzatmayı keser.
        await Assert.ThrowsAsync<ValidationException>(() => rentals.ExtendAsync(yasakli, Bas.AddDays(5)));
        Assert.Equal(Bas.AddDays(3), (await rentals.GetAsync(yasakli))!.BitTar);   // tarih DEĞİŞMEDİ
        Assert.Equal(300m, (await rentals.GetAsync(yasakli))!.Tutar);              // ELLE: 3 × 100

        // NEGATİF: kural yoksa uzatma çalışır (guard her zaman reddetmiyor).
        var (m2, a2) = await TaraflarAsync(sp, "34 KM 02");
        var serbest = await rentals.CreateDirectAsync(Istek(m2, a2, "OFIS"));
        Assert.True(await rentals.ExtendAsync(serbest, Bas.AddDays(5)));
        var c = (await rentals.GetAsync(serbest))!;
        Assert.Equal(5, c.Gun);
        Assert.Equal(500m, c.Tutar);                                               // ELLE: 5 × 100
    }

    /// <summary>Kaynak metni Kod yerine AD ile yazıldığında da kural bulunur (serbest metin alanı).</summary>
    [Fact]
    public async Task Kural_kaynak_ADI_ile_de_cozulur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();

        await KaynakAsync(sp, new ReservationSourceInput { Kod = "BRK", Ad = "Broker A", Uzatamaz = true });
        var (m, a) = await TaraflarAsync(sp, "34 KM 03");
        var id = await rentals.CreateDirectAsync(Istek(m, a, "broker a"));          // AD + farklı büyük/küçük
        await Assert.ThrowsAsync<ValidationException>(() => rentals.ExtendAsync(id, Bas.AddDays(5)));
    }

    /// <summary>
    /// Kaynağı PASİFE çekmek kuralı KALDIRMAZ — aksi halde bayrağı aşmak için bir tık yeterdi.
    /// (Fiyat motorunun kanal çözümünden bilinçli fark: orada tarife SEÇİMİ, burada KURAL var.)
    /// </summary>
    [Fact]
    public async Task Pasif_kaynagin_kurali_da_uygulanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var kaynaklar = sp.GetRequiredService<ReservationSourceService>();
        var rentals = sp.GetRequiredService<RentalService>();

        var kid = await KaynakAsync(sp, new ReservationSourceInput { Kod = "ACENTE", Ad = "Acente", Uzatamaz = true });
        var (m, a) = await TaraflarAsync(sp, "34 KM 04");
        var id = await rentals.CreateDirectAsync(Istek(m, a, "ACENTE"));

        await kaynaklar.UpdateAsync(kid, new ReservationSourceInput
        { Kod = "ACENTE", Ad = "Acente", Aktif = false, Uzatamaz = true });

        await Assert.ThrowsAsync<ValidationException>(() => rentals.ExtendAsync(id, Bas.AddDays(5)));
    }

    /// <summary>Rezervasyon tarih kilidi: tarih değişimi reddedilir, DİĞER alanların düzenlenmesi serbest.</summary>
    [Fact]
    public async Task RezTarihleriDegisemez_tarihi_kilitler_diger_alanlari_kilitlemez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var res = sp.GetRequiredService<ReservationService>();

        await KaynakAsync(sp, new ReservationSourceInput
        { Kod = "OTEL", Ad = "Otel", RezTarihleriDegisemez = true });
        await KaynakAsync(sp, new ReservationSourceInput { Kod = "WEB", Ad = "Web" });

        var (m, a) = await TaraflarAsync(sp, "34 KM 05");
        var id = await res.CreateAsync(Istek(m, a, "OTEL"));

        // POZİTİF: bitişi (ve başlangıcı) değiştirme reddedilir.
        var tarihli = Istek(m, a, "OTEL", gun: 5);
        await Assert.ThrowsAsync<ValidationException>(() => res.UpdateAsync(id, tarihli));

        // NEGATİF-1: tarihe dokunmayan düzenleme geçer (guard aging/not düzenlemesini kilitlemiyor).
        var notlu = Istek(m, a, "OTEL");
        notlu.Aciklama = "Otel misafiri";
        Assert.True(await res.UpdateAsync(id, notlu));
        var r = (await res.GetAsync(id))!;
        Assert.Equal("Otel misafiri", r.Aciklama);
        Assert.Equal(Bas.AddDays(3), r.BitTar);

        // NEGATİF-2: kuralsız kaynakta tarih değişimi çalışır.
        var (m2, a2) = await TaraflarAsync(sp, "34 KM 06");
        var serbest = await res.CreateAsync(Istek(m2, a2, "WEB"));
        Assert.True(await res.UpdateAsync(serbest, Istek(m2, a2, "WEB", gun: 5)));
        Assert.Equal(Bas.AddDays(5), (await res.GetAsync(serbest))!.BitTar);
    }

    /// <summary>
    /// ADVERSARIAL: aynı istekte kaynağı serbest bir kaynağa çevirip tarihi de değiştirmek kuralı
    /// DELMEZ — kayıtlı kaynağın kuralı da denetlenir.
    /// </summary>
    [Fact]
    public async Task Tarih_kilidi_kaynagi_degistirerek_asilamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var res = sp.GetRequiredService<ReservationService>();

        await KaynakAsync(sp, new ReservationSourceInput
        { Kod = "OTEL", Ad = "Otel", RezTarihleriDegisemez = true });
        await KaynakAsync(sp, new ReservationSourceInput { Kod = "WEB", Ad = "Web" });

        var (m, a) = await TaraflarAsync(sp, "34 KM 07");
        var id = await res.CreateAsync(Istek(m, a, "OTEL"));

        // Kaynak WEB'e çevriliyor + tarih uzatılıyor → yine RED (kayıtlı kaynağın kuralı geçerli).
        await Assert.ThrowsAsync<ValidationException>(() => res.UpdateAsync(id, Istek(m, a, "WEB", gun: 5)));
        var r = (await res.GetAsync(id))!;
        Assert.Equal(Bas.AddDays(3), r.BitTar);
        Assert.Equal("OTEL", r.Kaynak);          // istek tümüyle reddedildi, kaynak da değişmedi
    }

    /// <summary>Uzatma yasağı rezervasyon düzenlemesinde de geçerli (bitişi ileri almak = uzatma).</summary>
    [Fact]
    public async Task Uzatamaz_rezervasyon_bitisini_ileri_almayi_da_reddeder()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var res = sp.GetRequiredService<ReservationService>();

        await KaynakAsync(sp, new ReservationSourceInput { Kod = "BRK", Ad = "Broker", Uzatamaz = true });
        var (m, a) = await TaraflarAsync(sp, "34 KM 08");
        var id = await res.CreateAsync(Istek(m, a, "BRK", gun: 5));

        // İleri alma (uzatma) → RED.
        await Assert.ThrowsAsync<ValidationException>(() => res.UpdateAsync(id, Istek(m, a, "BRK", gun: 7)));
        // Kısaltma uzatma DEĞİLDİR (tarih kilidi bayrağı kapalı) → geçer.
        Assert.True(await res.UpdateAsync(id, Istek(m, a, "BRK", gun: 4)));
        Assert.Equal(Bas.AddDays(4), (await res.GetAsync(id))!.BitTar);
    }

    /// <summary>KM sınırsız: limit 0'a (sınırsız) sabitlenir — dönüşte fazla km bedeli çıkmaz.</summary>
    [Fact]
    public async Task KmSinirsiz_kaynakta_km_limiti_sifirlanir_digerinde_korunur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();
        var res = sp.GetRequiredService<ReservationService>();

        await KaynakAsync(sp, new ReservationSourceInput { Kod = "SINIRSIZ", Ad = "Sınırsız", KmSinirsiz = true });
        await KaynakAsync(sp, new ReservationSourceInput { Kod = "NORMAL", Ad = "Normal" });

        var (m1, a1) = await TaraflarAsync(sp, "34 KM 09");
        var istek1 = Istek(m1, a1, "SINIRSIZ");
        istek1.KmLimit = 500; istek1.FazlaKmUcret = 2m;
        var kira = await rentals.CreateDirectAsync(istek1);
        Assert.Equal(0, (await rentals.GetAsync(kira))!.KmLimit);      // POZİTİF: sınırsız

        var (m2, a2) = await TaraflarAsync(sp, "34 KM 10");
        var istek2 = Istek(m2, a2, "NORMAL");
        istek2.KmLimit = 500;
        var normal = await rentals.CreateDirectAsync(istek2);
        Assert.Equal(500, (await rentals.GetAsync(normal))!.KmLimit);  // NEGATİF: dokunulmadı

        // Rezervasyon yolu da aynı kuralı uygular.
        var (m3, a3) = await TaraflarAsync(sp, "34 KM 11");
        var istek3 = Istek(m3, a3, "SINIRSIZ");
        istek3.KmLimit = 750;
        var rez = await res.CreateAsync(istek3);
        Assert.Equal(0, (await res.GetAsync(rez))!.KmLimit);
    }

    /// <summary>Açık kira güncellemesinde de km sınırsızlığı uygulanır (form 500 gönderse bile).</summary>
    [Fact]
    public async Task KmSinirsiz_acik_kira_guncellemesinde_de_uygulanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();

        await KaynakAsync(sp, new ReservationSourceInput { Kod = "SINIRSIZ", Ad = "Sınırsız", KmSinirsiz = true });
        var (m, a) = await TaraflarAsync(sp, "34 KM 12");
        var id = await rentals.CreateDirectAsync(Istek(m, a, kaynak: null));
        Assert.Equal(0, (await rentals.GetAsync(id))!.KmLimit);

        // Kaynak sınırsıza çevriliyor + form km limiti gönderiyor → limit 0 kalır.
        Assert.True(await rentals.UpdateOpenAsync(id, new RentalUpdateInput { Kaynak = "SINIRSIZ", KmLimit = 400 }));
        Assert.Equal(0, (await rentals.GetAsync(id))!.KmLimit);

        // Kuralsız kaynağa dönülünce limit yeniden yazılabilir (guard yapışkan değil).
        Assert.True(await rentals.UpdateOpenAsync(id, new RentalUpdateInput { Kaynak = "SERBEST", KmLimit = 400 }));
        Assert.Equal(400, (await rentals.GetAsync(id))!.KmLimit);
    }

    /// <summary>Provizyon yasağı: bloke alma reddedilir; kuralsız kaynakta çalışır.</summary>
    [Fact]
    public async Task ProvizyonYok_kaynakta_provizyon_alinamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();

        await KaynakAsync(sp, new ReservationSourceInput { Kod = "KURUMSAL", Ad = "Kurumsal", ProvizyonYok = true });
        await KaynakAsync(sp, new ReservationSourceInput { Kod = "WEB", Ad = "Web" });

        var (m1, a1) = await TaraflarAsync(sp, "34 KM 13");
        var i1 = Istek(m1, a1, "KURUMSAL"); i1.Provizyon = 2000m;
        var yasakli = await rentals.CreateDirectAsync(i1);
        await Assert.ThrowsAsync<ValidationException>(() => rentals.ProvizyonAlAsync(yasakli));
        Assert.Equal(ProvizyonDurum.Yok, (await rentals.GetAsync(yasakli))!.ProvizyonDurum);

        var (m2, a2) = await TaraflarAsync(sp, "34 KM 14");
        var i2 = Istek(m2, a2, "WEB"); i2.Provizyon = 2000m;
        var serbest = await rentals.CreateDirectAsync(i2);
        Assert.True(await rentals.ProvizyonAlAsync(serbest));
        Assert.Equal(ProvizyonDurum.Alindi, (await rentals.GetAsync(serbest))!.ProvizyonDurum);
    }

    /// <summary>En fazla gün: oluşturmada ve uzatmada aynı sınır; sınır içinde kalan işlem geçer.</summary>
    [Fact]
    public async Task MaxGun_olusturma_ve_uzatmada_uygulanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();
        var res = sp.GetRequiredService<ReservationService>();

        await KaynakAsync(sp, new ReservationSourceInput { Kod = "GUNLU", Ad = "Günlü Kaynak", MaxGun = 4 });

        // POZİTİF (oluşturma): 5 gün > 4 → red.
        var (m1, a1) = await TaraflarAsync(sp, "34 KM 15");
        await Assert.ThrowsAsync<ValidationException>(() => rentals.CreateDirectAsync(Istek(m1, a1, "GUNLU", gun: 5)));
        await Assert.ThrowsAsync<ValidationException>(() => res.CreateAsync(Istek(m1, a1, "GUNLU", gun: 5)));

        // NEGATİF (sınırda): 4 gün geçer.
        var kira = await rentals.CreateDirectAsync(Istek(m1, a1, "GUNLU", gun: 4));
        Assert.Equal(4, (await rentals.GetAsync(kira))!.Gun);

        // POZİTİF (uzatma): 4 → 6 gün red; kayıt bozulmadı.
        await Assert.ThrowsAsync<ValidationException>(() => rentals.ExtendAsync(kira, Bas.AddDays(6)));
        Assert.Equal(4, (await rentals.GetAsync(kira))!.Gun);
    }

    /// <summary>Drop yasağı: farklı dönüş ofisi reddedilir; aynı ofis (ve boş dönüş ofisi) geçer.</summary>
    [Fact]
    public async Task AyniYonDrop_farkli_donus_ofisini_reddeder()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();

        await KaynakAsync(sp, new ReservationSourceInput { Kod = "TEKYON", Ad = "Tek Yön Yok", AyniYonDrop = true });

        var (m, a) = await TaraflarAsync(sp, "34 KM 16");
        var drop = Istek(m, a, "TEKYON");
        drop.CikisOfisi = "Merkez"; drop.DonusOfisi = "Havalimanı";
        await Assert.ThrowsAsync<ValidationException>(() => rentals.CreateDirectAsync(drop));

        var ayni = Istek(m, a, "TEKYON");
        ayni.CikisOfisi = "Merkez"; ayni.DonusOfisi = "merkez";     // büyük/küçük harf duyarsız
        var id = await rentals.CreateDirectAsync(ayni);
        Assert.Equal("Merkez", (await rentals.GetAsync(id))!.CikisOfisi);
    }

    /// <summary>
    /// GUARD YAŞLANMIŞ KAYDI KİLİTLEMEZ (tarih-politikası dersi): kural kaynağa SONRADAN konabilir.
    /// Kural konmadan önce açılmış drop'lu kiranın not düzenlemesi hâlâ geçmeli; ancak YENİ bir
    /// drop yaratma denemesi reddedilmeli.
    /// </summary>
    [Fact]
    public async Task Sonradan_konan_drop_kurali_mevcut_kaydi_kilitlemez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var kaynaklar = sp.GetRequiredService<ReservationSourceService>();
        var rentals = sp.GetRequiredService<RentalService>();

        var kid = await KaynakAsync(sp, new ReservationSourceInput { Kod = "TEKYON", Ad = "Tek Yön" });
        var (m, a) = await TaraflarAsync(sp, "34 KM 17");
        var drop = Istek(m, a, "TEKYON");
        drop.CikisOfisi = "Merkez"; drop.DonusOfisi = "Havalimanı";
        var id = await rentals.CreateDirectAsync(drop);          // kural HENÜZ yok → drop'lu kayıt açıldı

        await kaynaklar.UpdateAsync(kid, new ReservationSourceInput
        { Kod = "TEKYON", Ad = "Tek Yön", Aktif = true, AyniYonDrop = true });

        // Ofisler AYNI kalıyor, yalnız not değişiyor → GEÇMELİ (kayıt kilitlenmedi).
        Assert.True(await rentals.UpdateOpenAsync(id, new RentalUpdateInput
        {
            Kaynak = "TEKYON", CikisOfisi = "Merkez", DonusOfisi = "Havalimanı",
            Aciklama = "Kural sonradan kondu"
        }));
        Assert.Equal("Kural sonradan kondu", (await rentals.GetAsync(id))!.Aciklama);

        // YENİ bir drop yaratmak (dönüş ofisini başka bir yere taşımak) → RED.
        await Assert.ThrowsAsync<ValidationException>(() => rentals.UpdateOpenAsync(id, new RentalUpdateInput
        { Kaynak = "TEKYON", CikisOfisi = "Merkez", DonusOfisi = "Şube 2" }));
    }

    /// <summary>Aynı ders rezervasyonda: sonradan konan MaxGun mevcut uzun rezervasyonu kilitlemez.</summary>
    [Fact]
    public async Task Sonradan_konan_MaxGun_mevcut_rezervasyonun_not_duzenlemesini_kilitlemez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var kaynaklar = sp.GetRequiredService<ReservationSourceService>();
        var res = sp.GetRequiredService<ReservationService>();

        var kid = await KaynakAsync(sp, new ReservationSourceInput { Kod = "GUNLU", Ad = "Günlü" });
        var (m, a) = await TaraflarAsync(sp, "34 KM 18");
        var id = await res.CreateAsync(Istek(m, a, "GUNLU", gun: 10));   // sınır HENÜZ yok

        await kaynaklar.UpdateAsync(kid, new ReservationSourceInput
        { Kod = "GUNLU", Ad = "Günlü", Aktif = true, MaxGun = 4 });

        // Tarihe dokunmayan düzenleme geçer.
        var notlu = Istek(m, a, "GUNLU", gun: 10);
        notlu.Aciklama = "Sınır sonradan kondu";
        Assert.True(await res.UpdateAsync(id, notlu));
        Assert.Equal("Sınır sonradan kondu", (await res.GetAsync(id))!.Aciklama);

        // Tarihe DOKUNAN düzenleme yeni sınıra uymalı: 8 gün > 4 → red, 3 gün → geçer.
        await Assert.ThrowsAsync<ValidationException>(() => res.UpdateAsync(id, Istek(m, a, "GUNLU", gun: 8)));
        Assert.True(await res.UpdateAsync(id, Istek(m, a, "GUNLU", gun: 3)));
    }

    /// <summary>
    /// Kural matrisi TENANT'A ÖZELDİR: aynı Kod'lu kaynak iki tenant'ta farklı kural taşıyabilir ve
    /// biri diğerinin akışını etkilemez (racar_app + RLS ile).
    /// </summary>
    [Fact]
    public async Task Kurallar_tenant_izole()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
        {
            var sp = s1.ServiceProvider;
            await KaynakAsync(sp, new ReservationSourceInput { Kod = "ACENTE", Ad = "Acente", Uzatamaz = true });
            var (m, a) = await TaraflarAsync(sp, "34 TN 01");
            var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(Istek(m, a, "ACENTE"));
            await Assert.ThrowsAsync<ValidationException>(
                () => sp.GetRequiredService<RentalService>().ExtendAsync(id, Bas.AddDays(5)));
        }

        using var s2 = host.ScopeFor(t2);
        var sp2 = s2.ServiceProvider;
        // t2'de AYNI kodlu kaynak var ama kural TAŞIMIYOR — t1'in bayrağı sızmamalı.
        await KaynakAsync(sp2, new ReservationSourceInput { Kod = "ACENTE", Ad = "Acente" });
        var (m2, a2) = await TaraflarAsync(sp2, "34 TN 01");
        var kira2 = await sp2.GetRequiredService<RentalService>().CreateDirectAsync(Istek(m2, a2, "ACENTE"));
        Assert.True(await sp2.GetRequiredService<RentalService>().ExtendAsync(kira2, Bas.AddDays(5)));
        Assert.Equal(5, (await sp2.GetRequiredService<RentalService>().GetAsync(kira2))!.Gun);
    }

    // =====================================================================================
    // (2) ORAN/TUTAR ALANLARI — BİLGİ; FİYATA DOKUNMAZ
    // =====================================================================================

    /// <summary>
    /// FAZ-49 Exit çıtası: kaynağın komisyon/ön ödeme/indirim/puan oranları ve ek hizmet varsayılan
    /// tutarları uçuk değerlerle doldurulduğunda AYNI senaryonun fiyatı KURUŞU KURUŞUNA aynı kalır.
    /// Hem saf fiyat motoru (PricingService) hem de kayda geçen sözleşme (Tutar/GenelToplam/Bakiye)
    /// kontrol edilir — bir oran fiyata ya da ek hizmet satırına sızsa test KIRILIR.
    /// </summary>
    [Fact]
    public async Task FAZ49_oranlar_ve_ek_hizmet_tutarlari_FIYATA_ETKI_ETMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var kaynaklar = sp.GetRequiredService<ReservationSourceService>();

        await sp.GetRequiredService<VehicleGroupService>()
            .CreateAsync(new VehicleGroupInput { Kod = "EKO", Ad = "Ekonomik" });
        await sp.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
        {
            Kod = "EKO-WEB", Ad = "Eko Web", Kanal = "WEB", AracGrupKod = "EKO",
            Gun1 = 1000m, Gun2 = 950m, Gun3 = 900m, OnayDurumu = TarifeOnayDurumu.Onayli
        });
        var kid = await kaynaklar.CreateAsync(new ReservationSourceInput { Kod = "WEB", Ad = "Web Sitesi" });

        var arac = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 OR 49", Grup = "EKO" });
        var musteri = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Oran", Soyad = "Testi" });

        BookingInput Talep() => new()
        {
            MusteriId = musteri, VehicleId = arac,
            BasTar = Bas, BitTar = Bas.AddDays(3),
            Kaynak = "WEB", FiyatTuru = "Otomatik"
        };

        var fiyat = sp.GetRequiredService<PricingService>();
        var once = await fiyat.PriceAsync(Talep());
        Assert.Equal(3, once.Gun);
        Assert.Equal(2700m, once.Tutar);           // ELLE: 3 gün × Gun3 (900) = 2700

        // Kaynağa UÇUK oranlar + ek hizmet tutarları + tüm bilgi işaretleri yazılıyor.
        await kaynaklar.UpdateAsync(kid, new ReservationSourceInput
        {
            Kod = "WEB", Ad = "Web Sitesi", Aktif = true,
            KaynakGrubu = RezKaynakGrubu.Acente,
            KomisyonOrani = 90m, OnOdemeOrani = 80m, IndirimOrani = 75m, PuanOrani = 60m,
            KiraOrani = 50m, HizmetOrani = 50m, DropOrani = 50m,
            BebekKoltugu = 9999m, Navigasyon = 9999m, EkSurucu = 9999m, Wifi = 9999m,
            ScdwDahil = true, CdwDahil = true, LcfDahil = true, PaiDahil = true,
            MaliyetYansitma = true, MatrisErken = true, MatrisGecikme = true,
            MatrisIptal = true, MatrisNoShow = true, MatrisUzatma = true,
            OtomatikMailGitme = true, RiskAnalizYapma = true, SubeGor = true,
            AcenteFiyatDegistir = true, Gizle = true, SadeceMusteriOdeme = true,
            SigortaKaynakNo = "SG-1", DropKaynakNo = "DR-1",
            ProvizyonSecenek = "Kart", MuafiyatSecenek = "Tam", MailAdres = "acente@example.com"
        });

        var sonra = await fiyat.PriceAsync(Talep());
        Assert.Equal(once.Gun, sonra.Gun);
        Assert.Equal(2700m, sonra.Tutar);          // KURUŞU KURUŞUNA AYNI

        // Kayda geçen sözleşme de aynı: ek hizmet varsayılan tutarları satır ÜRETMEZ.
        var kiraId = await sp.GetRequiredService<RentalService>().CreateDirectAsync(Talep());
        var c = (await sp.GetRequiredService<RentalService>().GetAsync(kiraId))!;
        Assert.Equal(2700m, c.Tutar);
        Assert.Equal(2700m, c.GenelToplam);        // 4 × 9999 ek hizmet SIZMADI
        Assert.Equal(2700m, c.Bakiye);
    }

    // =====================================================================================
    // Master alanları: round-trip + çekirdek davranış regresyonu + doğrulama + yetki
    // =====================================================================================

    /// <summary>30+ alan create VE update yollarının İKİSİNDE de yazılmalı (kopya-kurucu tuzağı).</summary>
    [Fact]
    public async Task Kural_matrisi_alanlari_her_iki_yolda_da_roundtrip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<ReservationSourceService>();

        var id = await svc.CreateAsync(new ReservationSourceInput
        {
            Kod = "matris", Ad = "Matris Kaynağı", KaynakGrubu = RezKaynakGrubu.Broker,
            Uzatamaz = true, RezTarihleriDegisemez = true, ProvizyonYok = true,
            KmSinirsiz = true, AyniYonDrop = true, MaxGun = 30,
            MaliyetYansitma = true, MatrisErken = true, MatrisGecikme = true,
            MatrisIptal = true, MatrisNoShow = true, MatrisUzatma = true,
            SigortaKaynakNo = "  SG-77  ", DropKaynakNo = "DR-77",
            ProvizyonSecenek = "Kart", MuafiyatSecenek = "Kısmi",
            ScdwDahil = true, CdwDahil = true, LcfDahil = true, PaiDahil = true,
            BebekKoltugu = 150.5m, Navigasyon = 90m, EkSurucu = 200m, Wifi = 0m,
            KomisyonOrani = 12.5m, OnOdemeOrani = 30m, IndirimOrani = 5m, PuanOrani = 1.25m,
            MailAdres = "kaynak@example.com", OtomatikMailGitme = true, RiskAnalizYapma = true,
            SubeGor = true, AcenteFiyatDegistir = true, Gizle = true, SadeceMusteriOdeme = true
        });

        var g = await svc.GetAsync(id);
        Assert.Equal("MATRIS", g!.Kod);
        Assert.Equal(RezKaynakGrubu.Broker, g.KaynakGrubu);
        Assert.True(g.Uzatamaz); Assert.True(g.RezTarihleriDegisemez); Assert.True(g.ProvizyonYok);
        Assert.True(g.KmSinirsiz); Assert.True(g.AyniYonDrop);
        Assert.Equal(30, g.MaxGun);
        Assert.True(g.MaliyetYansitma && g.MatrisErken && g.MatrisGecikme && g.MatrisIptal
                    && g.MatrisNoShow && g.MatrisUzatma);
        Assert.Equal("SG-77", g.SigortaKaynakNo);              // trim
        Assert.Equal("DR-77", g.DropKaynakNo);
        Assert.Equal("Kart", g.ProvizyonSecenek);
        Assert.Equal("Kısmi", g.MuafiyatSecenek);
        Assert.True(g.ScdwDahil && g.CdwDahil && g.LcfDahil && g.PaiDahil);
        Assert.Equal(150.5m, g.BebekKoltugu);
        Assert.Equal(90m, g.Navigasyon);
        Assert.Equal(200m, g.EkSurucu);
        Assert.Equal(0m, g.Wifi);                              // 0 MEŞRU — null'a çevrilmez
        Assert.Equal(12.5m, g.KomisyonOrani);
        Assert.Equal(30m, g.OnOdemeOrani);
        Assert.Equal(5m, g.IndirimOrani);
        Assert.Equal(1.25m, g.PuanOrani);
        Assert.Equal("kaynak@example.com", g.MailAdres);
        Assert.True(g.OtomatikMailGitme && g.RiskAnalizYapma && g.SubeGor
                    && g.AcenteFiyatDegistir && g.Gizle && g.SadeceMusteriOdeme);

        // UPDATE yolu: kapatma/temizleme de bir güncellemedir.
        await svc.UpdateAsync(id, new ReservationSourceInput
        { Kod = "matris", Ad = "Matris Kaynağı", Aktif = true, KomisyonOrani = 7m });

        var y = await svc.GetAsync(id);
        Assert.Null(y!.KaynakGrubu);
        Assert.False(y.Uzatamaz); Assert.False(y.RezTarihleriDegisemez); Assert.False(y.ProvizyonYok);
        Assert.False(y.KmSinirsiz); Assert.False(y.AyniYonDrop);
        Assert.Null(y.MaxGun);
        Assert.Null(y.SigortaKaynakNo);
        Assert.Null(y.BebekKoltugu);
        Assert.Equal(7m, y.KomisyonOrani);
        Assert.False(y.Gizle);
    }

    /// <summary>REGRESYON: kural alanları eklendi diye ÇEKİRDEK kod benzersizliği bozulmadı.</summary>
    [Fact]
    public async Task Cekirdek_kod_benzersizligi_kural_alanlariyla_da_korunur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<ReservationSourceService>();

        await svc.CreateAsync(new ReservationSourceInput
        { Kod = "BROKER", Ad = "Broker", Uzatamaz = true, MaxGun = 10 });

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new ReservationSourceInput
        { Kod = "broker", Ad = "Başka Broker", KmSinirsiz = true }));

        Assert.Single(await svc.ListAsync());
        Assert.True((await svc.ListAsync())[0].Uzatamaz);      // ilk kayıt bozulmadı
    }

    /// <summary>Doğrulama: sınır/oran/tutar alanlarının anlamsız değerleri temiz redle döner.</summary>
    [Fact]
    public async Task Kural_alani_dogrulamalari()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<ReservationSourceService>();

        // MaxGun 0/negatif "sınırsız" DEĞİL "hiç kiralanamaz" olurdu → red (sınırsız = boş).
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new ReservationSourceInput
        { Kod = "G0", Ad = "Sıfır gün", MaxGun = 0 }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new ReservationSourceInput
        { Kod = "GN", Ad = "Negatif gün", MaxGun = -1 }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new ReservationSourceInput
        { Kod = "K1", Ad = "Aşırı komisyon", KomisyonOrani = 100.01m }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new ReservationSourceInput
        { Kod = "T1", Ad = "Negatif tutar", BebekKoltugu = -1m }));

        Assert.Empty(await svc.ListAsync());
    }

    /// <summary>Yetki: kural matrisi de OperationsWrite ister (yeni alanlar ayrı bir kapı açmadı).</summary>
    [Fact]
    public async Task Yetkisiz_rol_kural_matrisi_yazamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "muh", UserRole.Muhasebe);
        var svc = scope.ServiceProvider.GetRequiredService<ReservationSourceService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new ReservationSourceInput
        { Kod = "X", Ad = "Yetkisiz", Uzatamaz = true }));
    }
}
