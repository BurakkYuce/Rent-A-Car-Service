using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.VehicleGroups;
using RentACar.Application.Vehicles;
using RentACar.Application.WebSite;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR-13 — halka açık site ilan sihirbazı (araç seç → fiyat → teknik özellikler).
///
/// Bağımsız oracle: beklenenler senaryodan kurulur ("12 Egea seçtim, beraber modda 1 ilan olmalı"),
/// servisin kendi hesabından türetilmez.
///
/// En kritik davranışlar:
/// <list type="bullet">
///   <item><b>Türkçe imza</b> — "FIAT"/"Fıat" AYNI ilana düşmeli; <c>ToLowerInvariant</c> bunu kaçırır.</item>
///   <item><b>Manuel ≠ Otomatik</b> — kullanıcının kendi örneği "Egea Manuel Dizel".</item>
///   <item><b>İkiz ilan koruması</b> — aynı imzalı ilan varsa yeni yaratılmaz, mevcuda katılır.</item>
///   <item><b>Karışık alan satır üretmez</b> — site "Beyaz" derken siyah araç vermek olmaz.</item>
/// </list>
/// </summary>
[Collection("postgres")]
public sealed class WebIlanSihirbazTests(PostgresFixture fx)
{
    private static VehicleInput Arac(string plaka, string marka = "Fiat", string tip = "Egea",
        Transmission vites = Transmission.Manuel, FuelType yakit = FuelType.Dizel, int? yil = 2023,
        string? renk = null, string? grup = null, int? vitrinAdet = null) => new()
        {
            Plaka = plaka, Marka = marka, Tip = tip, Vites = vites, Yakit = yakit,
            ModelYili = yil, Renk = renk, Grup = grup, VitrinAdet = vitrinAdet,
            GrupBilincliBos = grup is null, Durum = VehicleStatus.Musait,
        };

    private static async Task<Guid> EkleAsync(IServiceScope s, VehicleInput input)
        => await s.ServiceProvider.GetRequiredService<VehicleService>().CreateAsync(input);

    /// <summary>10x8 gerçek PNG (VehiclePhotoTests ile aynı fixture) — magic-byte + gerçek decode geçer.</summary>
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAoAAAAICAIAAABPmPnhAAAAFElEQVR4nGM8YWTEgBsw4ZEb0tIAKaUBPDvSacQAAAAASUVORK5CYII=");

    // ---- Adım 1: beraber / ayrı ----

    [Fact]
    public async Task Beraber_modda_ayni_araclar_TEK_ilan_olur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<WebListingService>();

        for (var i = 1; i <= 4; i++) await EkleAsync(s, Arac($"34 EGE {i:000}"));

        var havuz = await svc.PoolAsync();
        var kume = Assert.Single(havuz);
        Assert.Equal(4, kume.Araclar.Count);

        await svc.StepOneSignatureAsync([kume.Imza]);

        var ilan = Assert.Single(await svc.ListAsync());
        Assert.Equal(4, ilan.AracSayisi); // 4 araç, 1 kart
    }

    [Fact]
    public async Task Ayri_modda_her_arac_KENDI_ilani_olur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<WebListingService>();
        var araclar = new List<Guid>();
        for (var i = 1; i <= 3; i++) araclar.Add(await EkleAsync(s, Arac($"34 AYR {i:000}")));

        await svc.StepOneAsync(araclar, together: false);

        var ilanlar = await svc.ListAsync();
        Assert.Equal(3, ilanlar.Count);
        Assert.All(ilanlar, i => Assert.Equal(1, i.AracSayisi));
    }

    [Fact]
    public async Task TURKCE_imza_FIAT_ve_Fiat_ayni_ilana_duser()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<WebListingService>();

        await EkleAsync(s, Arac("34 TRK 001", marka: "FIAT"));
        await EkleAsync(s, Arac("34 TRK 002", marka: "Fıat")); // noktasız ı — ToLowerInvariant KAÇIRIR
        await EkleAsync(s, Arac("34 TRK 003", marka: "fiat"));

        var kume = Assert.Single(await svc.PoolAsync());
        Assert.Equal(3, kume.Araclar.Count); // üçü de AYNI küme
    }

    [Fact]
    public async Task Manuel_ve_Otomatik_AYRI_ilan_olur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<WebListingService>();

        await EkleAsync(s, Arac("34 VTS 001", vites: Transmission.Manuel));
        await EkleAsync(s, Arac("34 VTS 002", vites: Transmission.Otomatik));

        // Kullanıcının kendi örneği: "Fiat Egea Manuel Dizel" — vites imzada.
        Assert.Equal(2, (await svc.PoolAsync()).Count);
    }

    [Fact]
    public async Task Farkli_MODEL_YILI_ayni_ilana_duser_baslikta_aralik_gorunur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<WebListingService>();

        await EkleAsync(s, Arac("34 YIL 001", yil: 2022));
        await EkleAsync(s, Arac("34 YIL 002", yil: 2023));

        // Yıl imzada YOK (kart enflasyonu olmasın) — aralık olarak gösterilir.
        var kume = Assert.Single(await svc.PoolAsync());
        Assert.Equal("2022–2023", kume.YilAralik);
    }

    [Fact]
    public async Task Ayni_imzali_ilan_varsa_IKIZ_yaratilmaz_mevcuda_katilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<WebListingService>();

        await EkleAsync(s, Arac("34 IKZ 001"));
        var imza = (await svc.PoolAsync()).Single().Imza;
        var ilkIlan = await svc.StepOneSignatureAsync([imza]);

        // Sonradan aynı modelden bir araç daha alındı ve yayınlandı.
        await EkleAsync(s, Arac("34 IKZ 002"));
        var ikinciIlan = await svc.StepOneSignatureAsync([(await svc.PoolAsync()).Single().Imza]);

        // Çok-şubeli tenant'ta iki operatör aynı modeli yayınlarsa sitede İKİ ÖZDEŞ KART çıkardı.
        Assert.Equal(ilkIlan, ikinciIlan);
        var ilan = Assert.Single(await svc.ListAsync());
        Assert.Equal(2, ilan.AracSayisi);
    }

    [Fact]
    public async Task Ilana_bagli_arac_havuzda_TEKRAR_gorunmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<WebListingService>();

        await EkleAsync(s, Arac("34 HVZ 001"));
        await svc.StepOneSignatureAsync([(await svc.PoolAsync()).Single().Imza]);

        Assert.Empty(await svc.PoolAsync());
        Assert.Equal(0, await svc.UnlistedVehicleCountAsync());
    }

    [Fact]
    public async Task Kapsam_disi_arac_id_si_SESSIZCE_yok_sayilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<WebListingService>();

        // Form'dan gelen id'ye güvenilmez: whitelist havuzdan doğrulanır.
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.StepOneAsync([Guid.NewGuid()], together: false));
    }

    // ---- Adım 2: fiyat ----

    [Fact]
    public async Task Fiyat_kaydedilir_ve_KARDES_taslaklara_kopyalanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<WebListingService>();
        var araclar = new List<Guid>();
        for (var i = 1; i <= 3; i++) araclar.Add(await EkleAsync(s, Arac($"34 FYT {i:000}")));

        var ilanId = await svc.StepOneAsync(araclar, together: false); // 3 ayrı taslak
        var kopyalanan = await svc.StepTwoAsync(ilanId, 1500m, 9000m, 32000m, vatIncluded: true);

        Assert.Equal(2, kopyalanan); // kullanıcı kararı: "tek kez doldur, hepsine kopyala"
        Assert.All(await svc.ListAsync(), i => Assert.Equal(1500m, i.GunlukFiyat));
    }

    [Fact]
    public async Task Sifir_gunluk_fiyat_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<WebListingService>();
        await EkleAsync(s, Arac("34 SFR 001"));
        var ilanId = await svc.StepOneSignatureAsync([(await svc.PoolAsync()).Single().Imza]);

        await Assert.ThrowsAsync<ValidationException>(() => svc.StepTwoAsync(ilanId, 0m, null, null, true));
    }

    // ---- Adım 3: teknik özellikler ----

    [Fact]
    public async Task Ozellikler_araclardan_URETILIR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<WebListingService>();
        await s.ServiceProvider.GetRequiredService<VehicleGroupService>()
            .CreateAsync(new VehicleGroupInput { Kod = "EKO", Ad = "Ekonomi", KoltukSayisi = 5, KapiSayisi = 4 });
        await EkleAsync(s, Arac("34 OZL 001", grup: "Ekonomi", renk: "Beyaz"));

        var ilanId = await svc.StepOneSignatureAsync([(await svc.PoolAsync()).Single().Imza]);
        var satirlar = await svc.SuggestedFeaturesAsync(ilanId);

        Assert.Contains(satirlar, x => x.Etiket == "Marka" && x.Deger == "Fiat");
        Assert.Contains(satirlar, x => x.Etiket == "Vites" && x.Deger == "Manuel");
        Assert.Contains(satirlar, x => x.Etiket == "Renk" && x.Deger == "Beyaz");
        Assert.Contains(satirlar, x => x.Etiket == "Koltuk Sayısı" && x.Deger == "5"); // GRUPTAN
    }

    [Fact]
    public async Task Araclar_arasi_FARKLI_alan_icin_satir_URETILMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<WebListingService>();
        await EkleAsync(s, Arac("34 RNK 001", renk: "Beyaz"));
        await EkleAsync(s, Arac("34 RNK 002", renk: "Siyah"));

        var ilanId = await svc.StepOneSignatureAsync([(await svc.PoolAsync()).Single().Imza]);
        var satirlar = await svc.SuggestedFeaturesAsync(ilanId);

        // "İlk aracın rengi"ni yazmak, site Beyaz derken müşteriye siyah araç vermek olurdu.
        Assert.DoesNotContain(satirlar, x => x.Etiket == "Renk");
        Assert.Contains(satirlar, x => x.Etiket == "Marka"); // imzadaki alanlar güvenli
    }

    [Fact]
    public async Task Adim_uc_FOTOLU_ilani_YAYINA_alir_ve_ozel_satir_eklenebilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<WebListingService>();
        await EkleAsync(s, Arac("34 YAY 001"));
        var ilanId = await svc.StepOneSignatureAsync([(await svc.PoolAsync()).Single().Imza]);
        await svc.StepTwoAsync(ilanId, 1500m, null, null, true);
        await svc.AddPhotoAsync(ilanId, TinyPng);              // yayın şartı

        Assert.True(await svc.StepThreeAsync(ilanId, [
            new OzellikSatiri("Marka", "Fiat"),
            new OzellikSatiri("Bluetooth", "Var"),            // "+" ile eklenen özel satır
            new OzellikSatiri("Gizli", "Değer", Gorunur: false),
        ]));

        var d = await svc.GetAsync(ilanId);
        Assert.Equal(WebIlanDurum.Yayinda, d!.Ilan.Durum);
        Assert.Equal(3, d.Ozellikler.Count);
        Assert.False(d.Ozellikler.Single(o => o.Etiket == "Gizli").Gorunur);
    }

    /// <summary>
    /// CANLIDA YAŞANAN HATANIN KİLİDİ: sihirbaz, fotoğrafsız bir ilanı "Yayında" işaretliyordu; oysa
    /// vitrinin yayın şartlarından biri fotoğraf (FleetShowcaseService). Sonuç: personele "yayınlandı"
    /// deniyor, sitede hiçbir şey görünmüyordu — üstelik sihirbazda fotoğraf yükleme yolu da yoktu.
    /// Yeni kural: özellikler HER ZAMAN kaydedilir (emek kaybolmaz), yayın fotoğrafa bağlıdır.
    /// </summary>
    [Fact]
    public async Task Adim_uc_FOTOSUZ_ilani_YAYINA_ALMAZ_ama_ozellikleri_KAYDEDER()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<WebListingService>();
        await EkleAsync(s, Arac("34 FOT 001"));
        var ilanId = await svc.StepOneSignatureAsync([(await svc.PoolAsync()).Single().Imza]);
        await svc.StepTwoAsync(ilanId, 1500m, null, null, true);

        Assert.False(await svc.StepThreeAsync(ilanId, [new OzellikSatiri("Marka", "Fiat")]));

        var d = await svc.GetAsync(ilanId);
        Assert.Equal(WebIlanDurum.Taslak, d!.Ilan.Durum);            // yayına ALINMADI
        Assert.Single(d.Ozellikler);                                  // …ama emek KORUNDU
        Assert.Contains("Foto yok", (await svc.ListAsync()).Single().Eksikler);

        // Fotoğraf eklenince aynı çağrı yayına alır (çare erişilebilir).
        await svc.AddPhotoAsync(ilanId, TinyPng);
        Assert.True(await svc.StepThreeAsync(ilanId, [new OzellikSatiri("Marka", "Fiat")]));
        Assert.Equal(WebIlanDurum.Yayinda, (await svc.GetAsync(ilanId))!.Ilan.Durum);
    }

    [Fact]
    public async Task Foto_BERABER_modda_tek_araca_eklenir_bayt_kopyalanmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<WebListingService>();
        for (var i = 1; i <= 4; i++) await EkleAsync(s, Arac($"34 KAP {i:000}"));
        var ilanId = await svc.StepOneSignatureAsync([(await svc.PoolAsync()).Single().Imza]);

        await svc.AddPhotoAsync(ilanId, TinyPng);
        await svc.AddPhotoAsync(ilanId, TinyPng);

        // 4 araçlık ilana 2 foto: İKİSİ DE AYNI araçta (vitrin tek kart gösteriyor, tek kapak yeter).
        var fotolar = await svc.PhotosAsync(ilanId);
        Assert.Equal(2, fotolar.Count);
        Assert.Single(fotolar.Select(f => f.VehicleId).Distinct());
    }

    [Fact]
    public async Task Foto_silme_UYE_OLMAYAN_araci_hedefleyemez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<WebListingService>();
        await EkleAsync(s, Arac("34 UYE 001"));
        var yabanciArac = await EkleAsync(s, Arac("34 UYE 002", tip: "Fiorino"));
        await s.ServiceProvider.GetRequiredService<VehiclePhotoService>().AddAsync(yabanciArac, TinyPng);

        var ilanId = await svc.StepOneSignatureAsync(
            [(await svc.PoolAsync()).Single(k => k.Baslik.Contains("Egea")).Imza]);
        await svc.AddPhotoAsync(ilanId, TinyPng);
        var yabanciFoto = (await s.ServiceProvider.GetRequiredService<VehiclePhotoService>()
            .ListMetaAsync(yabanciArac)).Single();

        // İlan id'si üzerinden BAŞKA aracın fotoğrafı silinemez (yetki var, hedef yanlış).
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.DeletePhotoAsync(ilanId, yabanciArac, yabanciFoto.Id));

        // Kendi fotoğrafı silinir.
        var kendi = Assert.Single(await svc.PhotosAsync(ilanId));
        await svc.DeletePhotoAsync(ilanId, kendi.VehicleId, kendi.PhotoId);
        Assert.Empty(await svc.PhotosAsync(ilanId));
    }

    [Fact]
    public async Task Ozellik_satir_siniri_zorlanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<WebListingService>();
        await EkleAsync(s, Arac("34 SNR 001"));
        var ilanId = await svc.StepOneSignatureAsync([(await svc.PoolAsync()).Single().Imza]);

        var cokSatir = Enumerable.Range(0, FeatureSnapshot.MaxRows + 1)
            .Select(i => new OzellikSatiri($"E{i}", $"D{i}")).ToList();
        await Assert.ThrowsAsync<ValidationException>(() => svc.StepThreeAsync(ilanId, cokSatir));

        await Assert.ThrowsAsync<ValidationException>(() => svc.StepThreeAsync(ilanId,
            [new OzellikSatiri(new string('x', FeatureSnapshot.MaxLabel + 1), "d")]));
    }

    [Fact]
    public async Task Bos_etiket_veya_deger_satiri_AYIKLANIR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<WebListingService>();
        await EkleAsync(s, Arac("34 BOS 001"));
        var ilanId = await svc.StepOneSignatureAsync([(await svc.PoolAsync()).Single().Imza]);

        // "+ Özellik ekle" ile açılan boş satırlar doldurulmadan gönderilebilir.
        await svc.StepThreeAsync(ilanId, [
            new OzellikSatiri("Marka", "Fiat"),
            new OzellikSatiri("  ", "  "),
            new OzellikSatiri("Etiket", "   "),
        ]);

        Assert.Single((await svc.GetAsync(ilanId))!.Ozellikler);
    }

    // ---- Yayın hazırlığı / tanılama ----

    [Fact]
    public async Task Yayin_eksikleri_TAMAMI_listelenir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<WebListingService>();
        await EkleAsync(s, Arac("34 EKS 001"));
        await svc.StepOneSignatureAsync([(await svc.PoolAsync()).Single().Imza]);

        var satir = Assert.Single(await svc.ListAsync());
        Assert.False(satir.Yayinda);
        Assert.Contains("Foto yok", satir.Eksikler);
        Assert.Contains("Fiyat girilmedi", satir.Eksikler);
        Assert.Contains(satir.Eksikler, e => e.StartsWith("Taslak"));
    }

    [Fact]
    public async Task Adet_VitrinAdet_toplamidir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<WebListingService>();
        await EkleAsync(s, Arac("34 ADT 001", vitrinAdet: 10));
        await EkleAsync(s, Arac("34 ADT 002"));             // null → 1
        await EkleAsync(s, Arac("34 ADT 003", vitrinAdet: 2));

        await svc.StepOneSignatureAsync([(await svc.PoolAsync()).Single().Imza]);

        var satir = Assert.Single(await svc.ListAsync());
        Assert.Equal(3, satir.AracSayisi);
        Assert.Equal(13, satir.Adet); // 10 + 1 + 2
    }

    [Fact]
    public async Task Ilan_silinince_araclar_yeniden_YAYINLANABILIR_olur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<WebListingService>();
        await EkleAsync(s, Arac("34 SIL 001"));
        var ilanId = await svc.StepOneSignatureAsync([(await svc.PoolAsync()).Single().Imza]);

        await svc.DeleteAsync(ilanId);

        Assert.Empty(await svc.ListAsync());
        Assert.Equal(1, await svc.UnlistedVehicleCountAsync()); // araç silinmedi, yalnız yayından kalktı
    }

    [Fact]
    public async Task Ozellikler_arac_degisince_BAYAT_isaretlenir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<WebListingService>();
        var araclar = s.ServiceProvider.GetRequiredService<VehicleService>();
        var aracId = await EkleAsync(s, Arac("34 BYT 001", renk: "Beyaz"));
        var ilanId = await svc.StepOneSignatureAsync([(await svc.PoolAsync()).Single().Imza]);
        await svc.StepThreeAsync(ilanId, await svc.SuggestedFeaturesAsync(ilanId));

        Assert.False((await svc.ListAsync()).Single().OzellikBayat);

        // ERP'de araç düzeltildi — ilan hâlâ eski değeri gösteriyor.
        await araclar.UpdateAsync(aracId, Arac("34 BYT 001", renk: "Siyah"));

        Assert.True((await svc.ListAsync()).Single().OzellikBayat);
    }

    // ---- Yetki / izolasyon ----

    [Fact]
    public async Task Sihirbaz_yetki_ister()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using (var admin = host.ScopeFor(tenant)) await EkleAsync(admin, Arac("34 YTK 001"));

        using var muhasebe = host.ScopeFor(tenant, role: UserRole.Muhasebe); // OperationsWrite YOK
        await Assert.ThrowsAsync<NoPermissionException>(
            () => muhasebe.ServiceProvider.GetRequiredService<WebListingService>().PoolAsync());
    }

    [Fact]
    public async Task Ilanlar_tenant_izoledir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
        {
            var svc1 = s1.ServiceProvider.GetRequiredService<WebListingService>();
            await EkleAsync(s1, Arac("34 IZO 001"));
            await svc1.StepOneSignatureAsync([(await svc1.PoolAsync()).Single().Imza]);
        }

        using var s2 = host.ScopeFor(t2);
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<WebListingService>().ListAsync());
    }
}
