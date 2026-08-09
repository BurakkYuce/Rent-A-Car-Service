using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.Kur;
using RentACar.Application.TenantSettings;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-82 — Ayarlar fiyat/muhasebe varsayılanları + kur elle-giriş kilidi.
///
/// <para>BAĞIMSIZ ORACLE: beklenen değerler elle kurulan senaryodan yazıldı, servis mantığından
/// türetilmedi (ör. "ayar yokken çıkış yakıt 8" sabiti testte ELLE duruyor; 100 EUR × 40 = 4000
/// tahsilat aritmetiği elle).</para>
///
/// <para>Bu dosyanın omurgası REGRESYONDUR: eklenen her ayar için "ayar boş/varsayılanken davranış
/// DEĞİŞMEDİ" testi var — FAZ-82 alanlarının hiçbiri deftere yazmıyor ve hiçbiri bir kaydın alanını
/// sunucu tarafında sessizce türetmiyor.</para>
/// </summary>
[Collection("postgres")]
public sealed class AyarFiyatKuralTests(PostgresFixture fx)
{
    private static Task<Guid> CariAsync(IServiceProvider sp, string ad = "Ayar") =>
        sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = ad, Soyad = "Test" });

    private static Task SabitKurAsync(IServiceProvider sp, string kod, decimal kur)
        => sp.GetRequiredService<SabitKurService>().UpsertAsync(new SabitKurInput { Kod = kod, Kur = kur, Aktif = true });

    // ---------------------------------------------------------------- Grup 3: alanlar + doğrulama

    [Fact]
    public async Task Fiyat_ve_is_kurali_alanlari_roundtrip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<TenantSettingsService>();

        await svc.SaveAsync(new TenantSettingsModel
        {
            VarsayilanFiyatTuru = "KDV Dahil Günlük",
            VarsayilanYakitSeviyesi = 12,
            DropMesafeYokIseSifir = true,
            SaatFarkiToleransDk = 45,
            IadeIslemSaatSiniri = 18,
            KurElleGirisKilitli = true
        });

        var m = await svc.GetAsync();
        Assert.Equal("KDV Dahil Günlük", m.VarsayilanFiyatTuru);
        Assert.Equal(12, m.VarsayilanYakitSeviyesi);
        Assert.True(m.DropMesafeYokIseSifir);
        Assert.Equal(45, m.SaatFarkiToleransDk);
        Assert.Equal(18, m.IadeIslemSaatSiniri);
        Assert.True(m.KurElleGirisKilitli);
    }

    [Fact]
    public async Task Yeni_tenantta_alanlar_bos_ve_kilit_kapali()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<TenantSettingsService>();

        // Hiç kaydedilmemiş tenant: model boş, kilit KAPALI (migration defaultValue false ile aynı anlam).
        var bos = await svc.GetAsync();
        Assert.Null(bos.VarsayilanFiyatTuru);
        Assert.Null(bos.VarsayilanYakitSeviyesi);
        Assert.Null(bos.DropMesafeYokIseSifir);
        Assert.False(bos.KurElleGirisKilitli);

        // Alanlara hiç dokunmadan bir kayıt: satır oluşur ama anlam aynı kalır.
        await svc.SaveAsync(new TenantSettingsModel { FirmaUnvan = "Yalnız firma" });
        var m = await svc.GetAsync();
        Assert.Null(m.VarsayilanFiyatTuru);
        Assert.Null(m.VarsayilanYakitSeviyesi);
        Assert.Null(m.SaatFarkiToleransDk);
        Assert.False(m.KurElleGirisKilitli);
    }

    [Theory]
    [InlineData(13)]
    [InlineData(-1)]
    public async Task Yakit_seviyesi_aralik_disi_reddedilir(int deger)
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<TenantSettingsService>();

        await Assert.ThrowsAsync<ValidationException>(
            () => svc.SaveAsync(new TenantSettingsModel { VarsayilanYakitSeviyesi = deger }));
    }

    [Fact]
    public async Task Fiyat_turu_tanimsiz_deger_reddedilir_kanonik_yazimla_saklanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<TenantSettingsService>();

        // "Gunluk" motorun tanımadığı bir metin: kabul edilseydi formda ön-seçili görünür, motorda
        // BAŞKA (net-mod olmayan) davranış çalışırdı.
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.SaveAsync(new TenantSettingsModel { VarsayilanFiyatTuru = "Gunluk" }));

        // Büyük/küçük harf duyarsız eşleşme KANONİK yazımla saklanır (motor tam metin karşılaştırıyor).
        await svc.SaveAsync(new TenantSettingsModel { VarsayilanFiyatTuru = "  tOPLAM " });
        Assert.Equal("Toplam", (await svc.GetAsync()).VarsayilanFiyatTuru);
    }

    [Fact]
    public async Task Beklemede_alanlar_negatif_kabul_etmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<TenantSettingsService>();

        await Assert.ThrowsAsync<ValidationException>(
            () => svc.SaveAsync(new TenantSettingsModel { SaatFarkiToleransDk = -1 }));
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.SaveAsync(new TenantSettingsModel { IadeIslemSaatSiniri = -5 }));
    }

    /// <summary>
    /// Ayarlar formunun EN TEHLİKELİ tuzağı: form değiştirilmeden ikinci kez kaydedildiğinde hiçbir
    /// alan kaymamalı/silinmemeli (tek kayıt tüm tenant ayarını sıfırlayabiliyor). Burada servis
    /// seviyesinde "oku → aynısını geri yaz → oku" turu yapılır; ondalık KDV oranı özellikle sınanır
    /// (ekranda tr-TR ile basılınca tarayıcı alanı boşaltıp bir sonraki kayıtta NULL'lıyordu —
    /// Ayarlar.razor artık InvariantCulture basıyor).
    /// </summary>
    [Fact]
    public async Task Ayarlar_iki_kez_kaydedilince_hicbir_alan_kaymaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<TenantSettingsService>();

        await svc.SaveAsync(new TenantSettingsModel
        {
            FirmaUnvan = "Yüce Rent", VarsayilanDoviz = "EUR", VarsayilanKdvOrani = 0.2000m,
            MinKiraGun = 2, MaxKiraGun = 60, RezOnayZorunlu = true,
            SmtpHost = "smtp.firma.com", SmtpPort = 587, SmtpSifre = "gizli",
            VarsayilanFiyatTuru = "Günlük", VarsayilanYakitSeviyesi = 6,
            DropMesafeYokIseSifir = false, SaatFarkiToleransDk = 30, IadeIslemSaatSiniri = 20,
            KurElleGirisKilitli = true, WhatsAppGunlukOzet = true, WhatsAppNumarasi = "+905550000000"
        });

        var ilk = await svc.GetAsync();
        await svc.SaveAsync(ilk);           // formu değiştirmeden ikinci kayıt (prefill round-trip)
        var ikinci = await svc.GetAsync();

        Assert.Equal("Yüce Rent", ikinci.FirmaUnvan);
        Assert.Equal("EUR", ikinci.VarsayilanDoviz);
        Assert.Equal(0.2000m, ikinci.VarsayilanKdvOrani);   // ondalık KAYBOLMADI
        Assert.Equal(2, ikinci.MinKiraGun);
        Assert.Equal(60, ikinci.MaxKiraGun);
        Assert.True(ikinci.RezOnayZorunlu);
        Assert.Equal("smtp.firma.com", ikinci.SmtpHost);
        Assert.Equal(587, ikinci.SmtpPort);
        Assert.Equal("gizli", ikinci.SmtpSifre);
        Assert.Equal("Günlük", ikinci.VarsayilanFiyatTuru);
        Assert.Equal(6, ikinci.VarsayilanYakitSeviyesi);
        Assert.False(ikinci.DropMesafeYokIseSifir);
        Assert.Equal(30, ikinci.SaatFarkiToleransDk);
        Assert.Equal(20, ikinci.IadeIslemSaatSiniri);
        Assert.True(ikinci.KurElleGirisKilitli);
        Assert.Equal("+905550000000", ikinci.WhatsAppNumarasi);
        Assert.True(ikinci.WhatsAppGunlukOzet);
    }

    // ---------------------------------------------------------------- Form varsayılanları çözücüsü

    [Fact]
    public async Task Ayar_yokken_form_varsayilanlari_bugunku_sabitler()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var coz = scope.ServiceProvider.GetRequiredService<FormVarsayilanCozucu>();

        Assert.Equal(8, await coz.CikisYakitAsync());   // ELLE: sayfaya gömülü olan eski sabit
        Assert.Null(await coz.FiyatTuruAsync());        // "—" (seçilmemiş)
    }

    [Fact]
    public async Task Form_varsayilanlari_ayardan_okunur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<TenantSettingsService>().SaveAsync(new TenantSettingsModel
        {
            VarsayilanYakitSeviyesi = 4, VarsayilanFiyatTuru = "Otomatik"
        });

        var coz = sp.GetRequiredService<FormVarsayilanCozucu>();
        Assert.Equal(4, await coz.CikisYakitAsync());
        Assert.Equal("Otomatik", await coz.FiyatTuruAsync());
    }

    /// <summary>Elle DB düzenlemesi/eski veri ile aralık dışı ya da motorun tanımadığı bir değer
    /// saklanmışsa çözücü SABİTE düşer — teslim formu HTML min/max'ıyla çakışmaz, operatöre motorun
    /// tanımadığı bir mod ön-seçili gösterilmez.</summary>
    [Fact]
    public async Task Bozuk_ayar_kaydi_guvenli_sabite_duser()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;

        // Servis doğrulamasını BİLEREK atlayarak bozuk satır yaz (eski veri taklidi).
        await sp.GetRequiredService<ITenantSettingsRepository>().UpsertAsync(s =>
        {
            s.VarsayilanYakitSeviyesi = 99;
            s.VarsayilanFiyatTuru = "Gunluk";   // motorun tanımadığı yazım
        });

        var coz = sp.GetRequiredService<FormVarsayilanCozucu>();
        Assert.Equal(8, await coz.CikisYakitAsync());
        Assert.Null(await coz.FiyatTuruAsync());
    }

    /// <summary>Çözücü ADMIN kapısı ARKASINDA DEĞİLDİR: rezervasyon/teklif/kira formları operatör
    /// yetkisiyle açılıyor; ayar servisi üzerinden okunsaydı operatörde 403 üretirdi (KdvVarsayilan deseni).</summary>
    [Fact]
    public async Task Cozucu_operatorde_calisir_ayar_servisi_calismaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using (var admin = host.ScopeFor(tenant))
            await admin.ServiceProvider.GetRequiredService<TenantSettingsService>()
                .SaveAsync(new TenantSettingsModel { VarsayilanYakitSeviyesi = 3 });

        using var op = host.ScopeFor(tenant, role: UserRole.Operator);
        Assert.Equal(3, await op.ServiceProvider.GetRequiredService<FormVarsayilanCozucu>().CikisYakitAsync());
        await Assert.ThrowsAsync<ValidationException>(
            () => op.ServiceProvider.GetRequiredService<TenantSettingsService>().GetAsync());
    }

    /// <summary>
    /// SÖZLEŞME REGRESYONU: varsayılan fiyat türü SUNUCUDA uygulanmaz. Ayar "Otomatik" olsa bile,
    /// FiyatTuru göndermeyen bir rezervasyon NULL kalır ve manuel ücret (elle: 4 gün × 100 = 400)
    /// aynen kazanır. Ayar sunucuya sızsaydı tarife motoru devreye girer, tutar değişirdi.
    /// </summary>
    [Fact]
    public async Task Varsayilan_fiyat_turu_kayda_sizmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<TenantSettingsService>()
            .SaveAsync(new TenantSettingsModel { VarsayilanFiyatTuru = "Otomatik" });

        var bas = DateTimeOffset.UtcNow.AddDays(3);
        var id = await sp.GetRequiredService<ReservationService>().CreateAsync(new BookingInput
        {
            MusteriId = Guid.NewGuid(), VehicleId = Guid.NewGuid(),
            BasTar = bas, BitTar = bas.AddDays(4), GunlukUcret = 100m
            // FiyatTuru BİLEREK gönderilmedi
        });

        var r = await sp.GetRequiredService<ReservationService>().GetAsync(id);
        Assert.Null(r!.FiyatTuru);      // ayar kayda sızmadı
        Assert.Equal(400m, r.Tutar);    // ELLE: 4 × 100 (manuel ücret kazandı)
    }

    // ---------------------------------------------------------------- Grup 4: kur elle-giriş kilidi

    /// <summary>REGRESYON: kilit KAPALIYKEN (varsayılan) açık kur aynen kabul edilir — 1.1 sözleşmesi
    /// ve mevcut 10+ çağrı sitesi hiç etkilenmez.</summary>
    [Fact]
    public async Task Kur_kilidi_kapaliyken_acik_kur_aynen_kabul_edilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var coz = scope.ServiceProvider.GetRequiredService<KurCozucu>();

        Assert.Equal(5.5m, await coz.CozAsync("EUR", 5.5m, null));   // ELLE: verilen kur
        Assert.Equal(1m, await coz.CozAsync("TRY", null, null));     // ELLE: TRY → 1
        // Ayar satırı VAR ama kilit kapalı → yine aynen kabul.
        await scope.ServiceProvider.GetRequiredService<TenantSettingsService>()
            .SaveAsync(new TenantSettingsModel { FirmaUnvan = "Kilitsiz", KurElleGirisKilitli = false });
        Assert.Equal(5.5m, await coz.CozAsync("EUR", 5.5m, null));
    }

    [Fact]
    public async Task Kur_kilidi_acikken_acik_kur_reddedilir_otomatik_yol_calisir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<TenantSettingsService>()
            .SaveAsync(new TenantSettingsModel { KurElleGirisKilitli = true });
        await SabitKurAsync(sp, "EUR", 40m);
        var coz = sp.GetRequiredService<KurCozucu>();

        await Assert.ThrowsAsync<ValidationException>(() => coz.CozAsync("EUR", 5.5m, null));
        // Pozitiflik guard'ından ÖNCE reddediliyor: 0/negatif kur da aynı kilit mesajıyla düşer.
        await Assert.ThrowsAsync<ValidationException>(() => coz.CozAsync("EUR", 0m, null));
        // Otomatik yol (kur boş) BOZULMAZ — kilit yalnız ELLE girişi kapatır.
        Assert.Equal(40m, await coz.CozAsync("EUR", null, null));    // ELLE: sabit kur 40
        Assert.Equal(1m, await coz.CozAsync("TRY", null, null));
    }

    /// <summary>
    /// Guard GİRİŞ noktasında olduğu için gerçek para ucundan da geçilemiyor: kilit açıkken açık kurlu
    /// tahsilat reddedilir ve HİÇBİR yan etki kalmaz (defter/CashTransaction boş). Ardından aynı işlem
    /// kursuz yapılınca sabit kurdan geçer — elle: 100 EUR × 40 = 4000 → cari bakiye −4000.
    /// </summary>
    [Fact]
    public async Task Kur_kilidi_para_ucunda_da_gecerli_sifir_yan_etki()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<TenantSettingsService>()
            .SaveAsync(new TenantSettingsModel { KurElleGirisKilitli = true });
        await SabitKurAsync(sp, "EUR", 40m);
        var cari = await CariAsync(sp);
        var cash = sp.GetRequiredService<CashService>();

        await Assert.ThrowsAsync<ValidationException>(
            () => cash.CollectAsync(new CashInput { CariId = cari, Tutar = 100m, Doviz = "EUR", Kur = 35m }));
        Assert.Equal(0m, await cash.GetCariBalanceAsync(cari));
        Assert.Empty(await cash.ListAsync());

        await cash.CollectAsync(new CashInput { CariId = cari, Tutar = 100m, Doviz = "EUR" });
        Assert.Equal(-4000m, await cash.GetCariBalanceAsync(cari));
    }

    /// <summary>
    /// ADVERSARIAL (bu PR'da bulunan gerçek açık): TOPLU tahsilat/ödeme ve TOPLU gider, açık kuru
    /// KurCozucu'ya UĞRAMADAN kendi yerel pozitiflik kontrolüyle alıyordu. Tekil yol kilitliyken toplu
    /// ekrandan kilit dolanılabiliyordu. Artık toplu yol da aynı kapıdan geçer; red ATOMİKTİR
    /// (hiçbir satır yazılmaz) ve satır/kalem öneki korunur.
    /// </summary>
    [Fact]
    public async Task Kur_kilidi_toplu_yollari_da_kapsar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<TenantSettingsService>()
            .SaveAsync(new TenantSettingsModel { KurElleGirisKilitli = true });
        await SabitKurAsync(sp, "EUR", 40m);
        var c1 = await CariAsync(sp, "T1");
        var c2 = await CariAsync(sp, "T2");
        var cash = sp.GetRequiredService<CashService>();

        // 2. satır açık kurlu → tüm toplu işlem reddedilir, hiçbir satır yazılmaz.
        var hata = await Assert.ThrowsAsync<ValidationException>(() => cash.BatchCollectAsync(
        [
            new CashInput { CariId = c1, Tutar = 100m, Doviz = "EUR" },
            new CashInput { CariId = c2, Tutar = 100m, Doviz = "EUR", Kur = 35m }
        ]));
        Assert.StartsWith("Satır 2:", hata.Message);
        Assert.Equal(0m, await cash.GetCariBalanceAsync(c1));
        Assert.Equal(0m, await cash.GetCariBalanceAsync(c2));
        Assert.Empty(await cash.ListAsync());

        // Kursuz toplu yol BOZULMAZ — elle: 100 EUR × 40 = 4000 (her iki cari).
        await cash.BatchCollectAsync(
        [
            new CashInput { CariId = c1, Tutar = 100m, Doviz = "EUR" },
            new CashInput { CariId = c2, Tutar = 100m, Doviz = "EUR" }
        ]);
        Assert.Equal(-4000m, await cash.GetCariBalanceAsync(c1));
        Assert.Equal(-4000m, await cash.GetCariBalanceAsync(c2));

        // Toplu GİDER de aynı kapıdan geçer.
        var giderler = sp.GetRequiredService<ExpenseService>();
        var giderHata = await Assert.ThrowsAsync<ValidationException>(() => giderler.BatchCreateAsync(
        [
            new ExpenseInput { Tip = ExpenseType.Genel, NetTutar = 50m, Doviz = "EUR", Kur = 35m }
        ]));
        Assert.StartsWith("Kalem 1:", giderHata.Message);
        Assert.Empty(await giderler.ListAsync());
    }

    /// <summary>REGRESYON: kilit KAPALIYKEN toplu yollar eskisi gibi açık kuru aynen kullanır
    /// (elle: 100 EUR × 35 = 3500) ve pozitiflik reddi satır önekini korur.</summary>
    [Fact]
    public async Task Kilit_kapaliyken_toplu_acik_kur_degismedi()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cari = await CariAsync(sp);
        var cash = sp.GetRequiredService<CashService>();

        await cash.BatchCollectAsync([new CashInput { CariId = cari, Tutar = 100m, Doviz = "EUR", Kur = 35m }]);
        Assert.Equal(-3500m, await cash.GetCariBalanceAsync(cari));

        var hata = await Assert.ThrowsAsync<ValidationException>(() => cash.BatchCollectAsync(
        [
            new CashInput { CariId = cari, Tutar = 10m, Doviz = "EUR", Kur = 35m },
            new CashInput { CariId = cari, Tutar = 10m, Doviz = "EUR", Kur = 0m }
        ]));
        Assert.StartsWith("Satır 2:", hata.Message);
        Assert.Equal(-3500m, await cash.GetCariBalanceAsync(cari)); // atomik: değişmedi
    }

    /// <summary>Tenant izolasyonu (racar_app + RLS): t1'in kilidi t2'yi bağlamaz, t2'nin ayar satırı
    /// t1 tarafından görülmez.</summary>
    [Fact]
    public async Task Kur_kilidi_tenant_izole()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<TenantSettingsService>()
                .SaveAsync(new TenantSettingsModel { KurElleGirisKilitli = true });

        using (var s1 = host.ScopeFor(t1))
            await Assert.ThrowsAsync<ValidationException>(
                () => s1.ServiceProvider.GetRequiredService<KurCozucu>().CozAsync("EUR", 7m, null));

        using var s2 = host.ScopeFor(t2);
        Assert.Equal(7m, await s2.ServiceProvider.GetRequiredService<KurCozucu>().CozAsync("EUR", 7m, null));
        Assert.False((await s2.ServiceProvider.GetRequiredService<TenantSettingsService>().GetAsync()).KurElleGirisKilitli);
    }

    /// <summary>Ayarlar tenant-owned: t2 t1'in FAZ-82 alanlarını ham DB üzerinden de göremez
    /// (racar_app bağlantısı + RLS).</summary>
    [Fact]
    public async Task Tenant_izolasyonu_ham_db()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<TenantSettingsService>().SaveAsync(
                new TenantSettingsModel { VarsayilanFiyatTuru = "Toplam", VarsayilanYakitSeviyesi = 11 });

        using var s2 = host.ScopeFor(t2);
        await using var db = await s2.ServiceProvider
            .GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Empty(await db.TenantSettings.AsNoTracking().ToListAsync());
    }

    /// <summary>Yetki: ayar yazma yalnız ManageUsers (Admin/Yönetici). Muhasebe rolü FAZ-82
    /// alanlarını değiştiremez — kur kilidi gibi bir iş kuralını finans rolü tek başına açamaz.</summary>
    [Fact]
    public async Task Ayar_yazma_yetkisi_manage_users()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();

        using var muhasebe = host.ScopeFor(tenant, role: UserRole.Muhasebe);
        await Assert.ThrowsAsync<ValidationException>(
            () => muhasebe.ServiceProvider.GetRequiredService<TenantSettingsService>()
                .SaveAsync(new TenantSettingsModel { KurElleGirisKilitli = true }));

        using var op = host.ScopeFor(tenant, role: UserRole.Operator);
        await Assert.ThrowsAsync<ValidationException>(
            () => op.ServiceProvider.GetRequiredService<TenantSettingsService>()
                .SaveAsync(new TenantSettingsModel { VarsayilanYakitSeviyesi = 2 }));
    }
}
