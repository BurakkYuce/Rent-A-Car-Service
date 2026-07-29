using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Fleet;
using RentACar.Application.RateMatrices;
using RentACar.Application.RentalRules;
using RentACar.Application.VehicleGroups;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR-7: halka açık müsaitlik+fiyat araması. BAĞIMSIZ ORACLE — beklenen tutarlar ELLE kurulmuş
/// tarife matrisinden türetilir (motor kodundan DEĞİL). Ziyaretçi bağlamı `Role: null`.
/// </summary>
[Collection("postgres")]
public sealed class PublicAvailabilityTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas = new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>EKO grubu + onaylı tarife (Gun1..Gun7 kademeleri) + 1 müsait araç.</summary>
    private static async Task SeedAsync(TestHost host, Guid tenantId, string grupAd = "Ekonomik",
        string grupKod = "EKO", bool webRezKapat = false)
    {
        using var scope = host.ScopeFor(tenantId);
        var sp = scope.ServiceProvider;

        await sp.GetRequiredService<VehicleGroupService>().CreateAsync(new VehicleGroupInput
        { Kod = grupKod, Ad = grupAd, KoltukSayisi = 5, KapiSayisi = 4, KasaTuru = "Sedan" });

        await sp.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
        {
            Kod = grupKod + "-WEB", Ad = grupKod + " Web", AracGrupKod = grupKod,
            Gun1 = 1000m, Gun2 = 950m, Gun3 = 900m, Gun4 = 875m, Gun5 = 850m, Gun6 = 825m, Gun7 = 800m,
            OnayDurumu = TarifeOnayDurumu.Onayli
        });

        var aracId = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        {
            Plaka = "34PA" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(),
            Durum = VehicleStatus.Musait, Grup = grupAd, WebRezKapat = webRezKapat
        });

        // PR-11 yayın kapısı: fotoğrafsız grup halka açık yüzeylerin HİÇBİRİNE girmez (vitrin,
        // arama, detay, sitemap). Bu dosyanın konusu FİYAT doğruluğu olduğu için foto sadece kapıyı
        // açmak üzere eklenir; kapının kendisi YayinKapisiTests'te test edilir.
        await sp.GetRequiredService<VehiclePhotoService>().AddAsync(aracId, TinyPng);
    }

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAoAAAAICAIAAABPmPnhAAAAFElEQVR4nGM8YWTEgBsw4ZEb0tIAKaUBPDvSacQAAAAASUVORK5CYII=");

    private static FleetShowcaseService PublicSvc(TestHost host, Guid tenantId, out IServiceScope scope)
    {
        scope = host.ScopeFor(tenantId, role: null); // PublicTenantContext'in gerçek şekli
        return scope.ServiceProvider.GetRequiredService<FleetShowcaseService>();
    }

    [Fact]
    public async Task Fiyat_ve_gun_sayisi_motordan_okunur_elle_hesaplanan_degerle_eslesir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        await SeedAsync(host, tenantId);

        var svc = PublicSvc(host, tenantId, out var scope);
        using var _ = scope;
        var sonuc = await svc.SearchAvailabilityAsync(Bas, Bas.AddDays(5), null);

        // ELLE ORACLE: 5 gün → Gun5 kademesi = 850 net/gün. Toplam net = 850×5 = 4250 (sigorta/km yok).
        // Varsayılan KDV %20 (TenantSettings boş → KdvMath.VarsayilanOran) → brüt gün = 1020, toplam = 5100.
        var r = Assert.Single(sonuc);
        Assert.Equal(5, r.Gun);
        Assert.Equal(850.00m, r.GunlukUcretKdvHaric);
        Assert.Equal(1020.00m, r.GunlukUcretKdvDahil);
        Assert.Equal(4250.00m, r.ToplamKdvHaric);
        Assert.Equal(5100.00m, r.ToplamKdvDahil);
        Assert.Equal("TRY", r.ParaBirimi);
        Assert.Equal("EKO", r.GrupKod);
    }

    [Fact]
    public async Task Uzun_kiralama_dusuk_gun_kademesini_secer()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        await SeedAsync(host, tenantId);

        var svc = PublicSvc(host, tenantId, out var scope);
        using var _ = scope;
        var kisa = await svc.SearchAvailabilityAsync(Bas, Bas.AddDays(1), null);
        var uzun = await svc.SearchAvailabilityAsync(Bas, Bas.AddDays(10), null);

        // ELLE ORACLE: 1 gün → Gun1 = 1000; 10 gün → kademe Gun7'ye clamp = 800 (Gun1'den FARKLI).
        Assert.Equal(1000.00m, Assert.Single(kisa).GunlukUcretKdvHaric);
        var u = Assert.Single(uzun);
        Assert.Equal(10, u.Gun);
        Assert.Equal(800.00m, u.GunlukUcretKdvHaric);
        Assert.Equal(8000.00m, u.ToplamKdvHaric); // 800×10
    }

    [Fact]
    public async Task WebRezKapat_tek_aracli_grup_sonucta_yok()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        await SeedAsync(host, tenantId, webRezKapat: true);

        var svc = PublicSvc(host, tenantId, out var scope);
        using var _ = scope;
        Assert.Empty(await svc.SearchAvailabilityAsync(Bas, Bas.AddDays(3), null));
    }

    [Fact]
    public async Task Tarifesi_olmayan_grup_sonucta_yok()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();

        using (var scope = host.ScopeFor(tenantId))
        {
            var sp = scope.ServiceProvider;
            await sp.GetRequiredService<VehicleGroupService>().CreateAsync(new VehicleGroupInput
            { Kod = "TARIFESIZ", Ad = "Tarifesiz Grup" });
            await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
            { Plaka = "34TZ0001", Durum = VehicleStatus.Musait, Grup = "Tarifesiz Grup" });
        }

        var svc = PublicSvc(host, tenantId, out var s2);
        using var _ = s2;
        Assert.Empty(await svc.SearchAvailabilityAsync(Bas, Bas.AddDays(3), null)); // GunlukUcret=0 → gösterilmez
    }

    [Fact]
    public async Task Turkce_case_farkli_grup_adi_yine_de_eslesir()
    {
        // PR-4.5'in TurkishText düzeltmesi arama yolunda da geçerli: Vehicle.Grup="dizel filo" (küçük),
        // VehicleGroup.Ad="DİZEL FİLO" (Türkçe büyük İ) — OrdinalIgnoreCase bunu KAÇIRIRDI.
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        using (var scope = host.ScopeFor(tenantId))
        {
            var sp = scope.ServiceProvider;
            await sp.GetRequiredService<VehicleGroupService>().CreateAsync(new VehicleGroupInput
            { Kod = "DZL", Ad = "DİZEL FİLO" });
            await sp.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
            { Kod = "DZL-WEB", Ad = "Dzl", AracGrupKod = "DZL", Gun1 = 500m, OnayDurumu = TarifeOnayDurumu.Onayli });
            var aracId = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
            { Plaka = "34DZ0001", Durum = VehicleStatus.Musait, Grup = "dizel filo" });
            await sp.GetRequiredService<VehiclePhotoService>().AddAsync(aracId, TinyPng); // PR-11 kapısı
        }

        var svc = PublicSvc(host, tenantId, out var s2);
        using var _ = s2;
        var r = Assert.Single(await svc.SearchAvailabilityAsync(Bas, Bas.AddDays(1), null));
        Assert.Equal("DİZEL FİLO", r.Ad);
        Assert.Equal(500.00m, r.GunlukUcretKdvHaric);
    }

    [Fact]
    public async Task Musait_arac_yoksa_bos_liste_doner()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var svc = PublicSvc(host, Guid.NewGuid(), out var scope);
        using var _ = scope;
        Assert.Empty(await svc.SearchAvailabilityAsync(Bas, Bas.AddDays(3), null));
    }

    [Fact]
    public async Task Sonuc_GroupId_vitrin_detay_sayfasiyla_eslesir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        await SeedAsync(host, tenantId);

        var svc = PublicSvc(host, tenantId, out var scope);
        using var _ = scope;
        var r = Assert.Single(await svc.SearchAvailabilityAsync(Bas, Bas.AddDays(2), null));

        // Arama sonucundaki GroupId, PR-4'ün /araclar/{id} detay sayfasını AÇMALI (link kırık olmasın).
        var detay = await svc.GetGroupDetailAsync(r.GroupId);
        Assert.NotNull(detay);
        Assert.Equal(r.Ad, detay!.Ad);
    }

    [Fact]
    public async Task Arama_tenant_izoledir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        await SeedAsync(host, t1);

        var svc2 = PublicSvc(host, t2, out var scope);
        using var _ = scope;
        Assert.Empty(await svc2.SearchAvailabilityAsync(Bas, Bas.AddDays(3), null));
    }

    [Fact]
    public async Task Kdv_orani_tenant_ayarindan_okunur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        await SeedAsync(host, tenantId);

        // Tenant KDV'sini %10'a çek — brüt rakam bu orandan türemeli (sabit 0.20 DEĞİL).
        using (var scope = host.ScopeFor(tenantId))
        {
            var repo = scope.ServiceProvider.GetRequiredService<RentACar.Application.TenantSettings.ITenantSettingsRepository>();
            await repo.UpsertAsync(s => s.VarsayilanKdvOrani = 0.10m);
        }

        var svc = PublicSvc(host, tenantId, out var s2);
        using var _ = s2;
        var r = Assert.Single(await svc.SearchAvailabilityAsync(Bas, Bas.AddDays(1), null));

        // ELLE ORACLE: 1 gün → 1000 net; %10 KDV → 1100 brüt.
        Assert.Equal(1000.00m, r.GunlukUcretKdvHaric);
        Assert.Equal(1100.00m, r.GunlukUcretKdvDahil);
    }
}
