using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Fleet;
using RentACar.Application.RateMatrices;
using RentACar.Application.VehicleGroups;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR-11 — halka açık site YAYIN KAPISI (foto + fiyat) ve vitrin adedi.
///
/// Bağımsız oracle: beklenen değerler senaryodan kurulur ("2 araç yükledim, 2 görmeliyim"),
/// servisin kendi hesabından türetilmez.
///
/// En kritik testler <c>Sezonluk_*</c> — <b>KAPI ⊇ ARAMA</b> kuralının kilidi: vitrin/detay kapısı
/// aramanın kabul ettiği her durumu kabul etmeli, yoksa arama kart basar ve kartın "Detay" linki
/// 404 verir (rent-a-car'da Haziran–Eylül tarifesi Mart'ta girilir — bu senaryo istisna değil, kural).
/// </summary>
[Collection("postgres")]
public sealed class YayinKapisiTests(PostgresFixture fx)
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAoAAAAICAIAAABPmPnhAAAAFElEQVR4nGM8YWTEgBsw4ZEb0tIAKaUBPDvSacQAAAAASUVORK5CYII=");

    private static async Task<Guid> GrupAsync(TestHost host, Guid t, string ad, int? webSira = null)
    {
        using var s = host.ScopeFor(t);
        return await s.ServiceProvider.GetRequiredService<VehicleGroupService>()
            .CreateAsync(new VehicleGroupInput { Kod = ad.ToUpperInvariant(), Ad = ad, WebSira = webSira, Aktif = true });
    }

    private static async Task<Guid> AracAsync(TestHost host, Guid t, string grup, int? vitrinAdet = null)
    {
        using var s = host.ScopeFor(t);
        return await s.ServiceProvider.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        {
            Plaka = "34YK" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(),
            Durum = VehicleStatus.Musait, Grup = grup, VitrinAdet = vitrinAdet,
        });
    }

    private static async Task FotoAsync(TestHost host, Guid t, Guid aracId)
    {
        using var s = host.ScopeFor(t);
        await s.ServiceProvider.GetRequiredService<VehiclePhotoService>().AddAsync(aracId, TinyPng);
    }

    /// <summary>Onaylı tarife. <paramref name="grupKod"/> null → WILDCARD (tüm gruplara uyar).</summary>
    private static async Task TarifeAsync(TestHost host, Guid t, string? grupKod, decimal gun1 = 100m,
        DateTimeOffset? basTar = null, DateTimeOffset? bitTar = null)
    {
        using var s = host.ScopeFor(t);
        await s.ServiceProvider.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
        {
            Kod = "TR" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(),
            Ad = "Tarife", AracGrupKod = grupKod, Gun1 = gun1, ParaBirimi = "TRY",
            BasTar = basTar, BitTar = bitTar,
            OnayDurumu = TarifeOnayDurumu.Onayli, Aktif = true,
        });
    }

    private static FleetShowcaseService Vitrin(TestHost host, Guid t, out IServiceScope scope)
    {
        scope = host.ScopeFor(t, role: null); // ziyaretçi bağlamı
        return scope.ServiceProvider.GetRequiredService<FleetShowcaseService>();
    }

    // ---- Kapı: foto şartı ----

    [Fact]
    public async Task Fotosuz_grup_vitrine_GIRMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        await GrupAsync(host, t, "Ekonomi");
        await AracAsync(host, t, "Ekonomi");
        await TarifeAsync(host, t, "EKONOMI");

        var svc = Vitrin(host, t, out var scope); using (scope)
            Assert.Empty(await svc.ListShowcaseGroupsAsync()); // tarife var, foto yok → pending
    }

    [Fact]
    public async Task Foto_ve_tarife_tamamsa_grup_yayina_girer()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        await GrupAsync(host, t, "Ekonomi");
        var arac = await AracAsync(host, t, "Ekonomi");
        await FotoAsync(host, t, arac);
        await TarifeAsync(host, t, "EKONOMI");

        var svc = Vitrin(host, t, out var scope); using (scope)
            Assert.Single(await svc.ListShowcaseGroupsAsync());
    }

    [Fact]
    public async Task Kapak_fotosu_OLAN_aractan_secilir_temsilciden_degil()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        await GrupAsync(host, t, "Ekonomi");
        await AracAsync(host, t, "Ekonomi");            // fotosuz (eskiden temsilci bu olabilir → kart fotosuz kalırdı)
        var fotolu = await AracAsync(host, t, "Ekonomi");
        await FotoAsync(host, t, fotolu);
        await TarifeAsync(host, t, "EKONOMI");

        var svc = Vitrin(host, t, out var scope); using (scope)
        {
            var kartlar = await svc.ListShowcaseGroupsAsync();
            Assert.NotNull(Assert.Single(kartlar).CoverPhotoId);
        }
    }

    // ---- Kapı: fiyat şartı ----

    [Fact]
    public async Task Tarifesiz_grup_vitrine_GIRMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        await GrupAsync(host, t, "Ekonomi");
        var arac = await AracAsync(host, t, "Ekonomi");
        await FotoAsync(host, t, arac);

        var svc = Vitrin(host, t, out var scope); using (scope)
            Assert.Empty(await svc.ListShowcaseGroupsAsync());
    }

    [Fact]
    public async Task WILDCARD_tarifeli_grup_YANLIS_ELENMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        await GrupAsync(host, t, "Ekonomi");
        var arac = await AracAsync(host, t, "Ekonomi");
        await FotoAsync(host, t, arac);
        await TarifeAsync(host, t, grupKod: null); // AracGrupKod = null → TÜM gruplara uyar

        // Naif "bu gruba özel satır var mı" kontrolü bu grubu yanlışlıkla elerdi — motorun kendi
        // yüklemi paylaşıldığı için wildcard doğru yorumlanır.
        var svc = Vitrin(host, t, out var scope); using (scope)
            Assert.Single(await svc.ListShowcaseGroupsAsync());
    }

    [Fact]
    public async Task Tum_kademeleri_SIFIR_olan_tarife_yayina_sokmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        await GrupAsync(host, t, "Ekonomi");
        var arac = await AracAsync(host, t, "Ekonomi");
        await FotoAsync(host, t, arac);
        await TarifeAsync(host, t, "EKONOMI", gun1: 0m);

        // Satır VAR ama fiyat yok: arama `GunlukUcret <= 0` ile eler → vitrinde görünüp aramada
        // kaybolan grup ve kırık "Detay" linki oluşurdu.
        var svc = Vitrin(host, t, out var scope); using (scope)
            Assert.Empty(await svc.ListShowcaseGroupsAsync());
    }

    [Fact]
    public async Task Onaylanmamis_tarife_yayina_sokmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        await GrupAsync(host, t, "Ekonomi");
        var arac = await AracAsync(host, t, "Ekonomi");
        await FotoAsync(host, t, arac);
        using (var s = host.ScopeFor(t))
        {
            await s.ServiceProvider.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
            {
                Kod = "BEKLIYOR", Ad = "Onaysız", AracGrupKod = "EKONOMI", Gun1 = 100m,
                OnayDurumu = TarifeOnayDurumu.Bekliyor, Aktif = true,
            });
        }

        var svc = Vitrin(host, t, out var scope); using (scope)
            Assert.Empty(await svc.ListShowcaseGroupsAsync());
    }

    // ---- KAPI ⊇ ARAMA: sezonluk tarife ----

    [Fact]
    public async Task Sezonluk_gelecek_tarifeli_grup_VITRINDE_gorunur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        await GrupAsync(host, t, "Ekonomi");
        var arac = await AracAsync(host, t, "Ekonomi");
        await FotoAsync(host, t, arac);
        // Bugün Mart varsayımı: tarife 3 ay sonra başlıyor (canlıda tam da böyle girilir).
        var bas = DateTimeOffset.UtcNow.AddMonths(3);
        await TarifeAsync(host, t, "EKONOMI", basTar: bas, bitTar: bas.AddMonths(3));

        var svc = Vitrin(host, t, out var scope); using (scope)
            Assert.Single(await svc.ListShowcaseGroupsAsync()); // kart fiyat basmaz; gelecek sezon gösterilir
    }

    [Fact]
    public async Task Sezonluk_grup_detay_sayfasi_200_doner_ARAMA_LINKI_KIRILMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var grupId = await GrupAsync(host, t, "Ekonomi");
        var arac = await AracAsync(host, t, "Ekonomi");
        await FotoAsync(host, t, arac);
        var bas = DateTimeOffset.UtcNow.AddMonths(3);
        await TarifeAsync(host, t, "EKONOMI", basTar: bas, bitTar: bas.AddMonths(3));

        var svc = Vitrin(host, t, out var scope); using (scope)
        {
            // Sezon içi arama bu grubu bulur; kartın "Detay" linki 404 vermemeli.
            var sonuc = await svc.SearchAvailabilityAsync(bas.AddDays(10), bas.AddDays(13), null);
            Assert.Single(sonuc);
            Assert.NotNull(await svc.GetGroupDetailAsync(grupId));
        }
    }

    [Fact]
    public async Task Sezonluk_grup_SEZON_DISI_aramada_cikmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        await GrupAsync(host, t, "Ekonomi");
        var arac = await AracAsync(host, t, "Ekonomi");
        await FotoAsync(host, t, arac);
        var bas = DateTimeOffset.UtcNow.AddMonths(3);
        await TarifeAsync(host, t, "EKONOMI", basTar: bas, bitTar: bas.AddMonths(3));

        var svc = Vitrin(host, t, out var scope); using (scope)
        {
            // Yarın için arama: tarife henüz geçerli değil → fiyat yok → kart yok. Vitrinde OLMASI
            // çelişki değil: vitrin fiyat basmaz, arama basar.
            var yarin = DateTimeOffset.UtcNow.AddDays(1);
            Assert.Empty(await svc.SearchAvailabilityAsync(yarin, yarin.AddDays(3), null));
        }
    }

    [Fact]
    public async Task GECMISTE_kalmis_tarife_yayina_sokmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        await GrupAsync(host, t, "Ekonomi");
        var arac = await AracAsync(host, t, "Ekonomi");
        await FotoAsync(host, t, arac);
        // Geçen yılın tarifesi: "herhangi bir tarihte geçerli" kuralı bunu da kabul ederdi (yanlış).
        await TarifeAsync(host, t, "EKONOMI",
            basTar: DateTimeOffset.UtcNow.AddYears(-1), bitTar: DateTimeOffset.UtcNow.AddMonths(-6));

        var svc = Vitrin(host, t, out var scope); using (scope)
            Assert.Empty(await svc.ListShowcaseGroupsAsync());
    }

    // ---- Kapı: detay + sitemap yüzeyleri ----

    [Fact]
    public async Task Yayinlanmamis_grubun_detay_sayfasi_404()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var grupId = await GrupAsync(host, t, "Ekonomi");
        await AracAsync(host, t, "Ekonomi"); // foto YOK

        var svc = Vitrin(host, t, out var scope); using (scope)
            Assert.Null(await svc.GetGroupDetailAsync(grupId)); // eski sitemap linki içerik açmasın
    }

    // ---- VitrinAdet ----

    [Fact]
    public async Task Adet_tekil_kayitlarda_arac_sayisidir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        await GrupAsync(host, t, "Ekonomi");
        var a1 = await AracAsync(host, t, "Ekonomi");
        await AracAsync(host, t, "Ekonomi");
        await AracAsync(host, t, "Ekonomi");
        await FotoAsync(host, t, a1);
        await TarifeAsync(host, t, "EKONOMI");

        var svc = Vitrin(host, t, out var scope); using (scope)
            Assert.Equal(3, (await svc.ListShowcaseGroupsAsync()).Single().Adet); // 3 × (null → 1)
    }

    [Fact]
    public async Task Adet_tek_kayitta_VitrinAdet_kadardir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        await GrupAsync(host, t, "Ekonomi");
        var arac = await AracAsync(host, t, "Ekonomi", vitrinAdet: 12);
        await FotoAsync(host, t, arac);
        await TarifeAsync(host, t, "EKONOMI");

        var svc = Vitrin(host, t, out var scope); using (scope)
            Assert.Equal(12, (await svc.ListShowcaseGroupsAsync()).Single().Adet);
    }

    [Fact]
    public async Task Adet_karisik_modda_toplamdir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        await GrupAsync(host, t, "Ekonomi");
        var a1 = await AracAsync(host, t, "Ekonomi", vitrinAdet: 10);
        await AracAsync(host, t, "Ekonomi");           // null → 1
        await AracAsync(host, t, "Ekonomi", vitrinAdet: 2);
        await FotoAsync(host, t, a1);
        await TarifeAsync(host, t, "EKONOMI");

        var svc = Vitrin(host, t, out var scope); using (scope)
            Assert.Equal(13, (await svc.ListShowcaseGroupsAsync()).Single().Adet); // 10 + 1 + 2
    }

    [Fact]
    public async Task VitrinAdet_sinirlari_zorlanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<VehicleService>();

        await Assert.ThrowsAsync<Application.Common.ValidationException>(
            () => svc.CreateAsync(new VehicleInput { Plaka = "34 VA 001", VitrinAdet = 0 }));
        // "12" yerine "1200" typo'su vitrinde "1200 araç" basardı.
        await Assert.ThrowsAsync<Application.Common.ValidationException>(
            () => svc.CreateAsync(new VehicleInput { Plaka = "34 VA 002", VitrinAdet = 1000 }));
    }

    [Fact]
    public async Task VitrinAdet_MUSAITLIGI_ETKILEMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        await GrupAsync(host, t, "Ekonomi");
        var arac = await AracAsync(host, t, "Ekonomi", vitrinAdet: 12);
        await FotoAsync(host, t, arac);
        await TarifeAsync(host, t, "EKONOMI");

        using var scope = host.ScopeFor(t, role: null);
        var musait = await scope.ServiceProvider.GetRequiredService<Application.Availability.AvailabilityService>()
            .FindAvailableAsync(DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(3), null, null);

        // REGRESYON KİLİDİ: adet yalnız GÖSTERİM. Müsaitlik hâlâ 1 fiziksel araç görür — aksi halde
        // rentals_no_overlap GiST kısıtıyla çelişen bir kapasite iddiası doğardı.
        Assert.Single(musait);
    }

    // ---- Personel hazırlık paneli ----

    [Fact]
    public async Task Yayin_durumu_TUM_eksikleri_birlikte_gosterir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        await GrupAsync(host, t, "Ekonomi");
        await AracAsync(host, t, "Ekonomi"); // ne foto ne tarife

        using var scope = host.ScopeFor(t);
        var durum = (await scope.ServiceProvider.GetRequiredService<FleetShowcaseService>()
            .ListYayinDurumuAsync()).Single();

        Assert.False(durum.Yayinda);
        // Tek sebep gösterilseydi personel fotoyu yükler, kart yine çıkmaz, nedenini bilemezdi.
        Assert.Contains("Foto yok", durum.Eksikler);
        Assert.Contains("Tarife yok", durum.Eksikler);
    }

    [Fact]
    public async Task Yayin_durumu_aracsiz_grubu_da_listeler()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        await GrupAsync(host, t, "Ekonomi");

        using var scope = host.ScopeFor(t);
        var durum = (await scope.ServiceProvider.GetRequiredService<FleetShowcaseService>()
            .ListYayinDurumuAsync()).Single();

        Assert.Contains("Uygun araç yok", durum.Eksikler);
        Assert.Equal(0, durum.AracSayisi);
    }

    [Fact]
    public async Task Yayin_durumu_karisik_modu_isaretler()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        await GrupAsync(host, t, "Ekonomi");
        await AracAsync(host, t, "Ekonomi", vitrinAdet: 10);
        await AracAsync(host, t, "Ekonomi");

        using var scope = host.ScopeFor(t);
        var durum = (await scope.ServiceProvider.GetRequiredService<FleetShowcaseService>()
            .ListYayinDurumuAsync()).Single();

        Assert.True(durum.KarisikMod); // 11 gösterilecek; personel bunun bilinçli olduğunu doğrulamalı
        Assert.Equal(11, durum.Adet);
    }

    [Fact]
    public async Task Yayin_kapisi_tenant_izoledir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        await GrupAsync(host, t1, "Ekonomi");
        var arac = await AracAsync(host, t1, "Ekonomi");
        await FotoAsync(host, t1, arac);
        await TarifeAsync(host, t1, "EKONOMI");

        var svc = Vitrin(host, t2, out var scope); using (scope)
            Assert.Empty(await svc.ListShowcaseGroupsAsync()); // T1'in yayını T2'ye SIZMAZ
    }
}
