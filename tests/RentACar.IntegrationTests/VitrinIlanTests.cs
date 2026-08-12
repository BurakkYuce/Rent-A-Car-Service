using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Fleet;
using RentACar.Application.PublicSite;
using RentACar.Application.Vehicles;
using RentACar.Application.WebSite;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR-14 — halka açık site İLAN bazlı. Bu dosya, emekli edilen üç dosyanın (FleetShowcaseServiceTests,
/// PublicAvailabilityTests, YayinKapisiTests) yerine geçer: vitrin artık SINIF değil ilan gösteriyor
/// ve fiyat <c>RentalQuoteEngine</c>'den DEĞİL ilandan geliyor (kullanıcı kararı).
///
/// Bağımsız oracle: fiyat beklentileri ELLE kurulur ("haftalık 9.000 girdim, 10 günde günlük 1.285,71
/// olmalı" = 9000/7), servisin kendi hesabından türetilmez.
///
/// Ziyaretçi bağlamı <c>role: null</c> — PublicTenantContext'in gerçek şekli.
/// </summary>
[Collection("postgres")]
public sealed class VitrinIlanTests(PostgresFixture fx)
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAoAAAAICAIAAABPmPnhAAAAFElEQVR4nGM8YWTEgBsw4ZEb0tIAKaUBPDvSacQAAAAASUVORK5CYII=");

    /// <summary>Yayına hazır bir ilan kurar: araç(lar) + foto + sihirbazın üç adımı.</summary>
    private async Task<(Guid IlanId, string Slug, List<Guid> AracIdler)> IlanKurAsync(
        TestHost host, Guid t, int aracSayisi = 1, decimal gunluk = 1500m,
        decimal? haftalik = null, decimal? aylik = null, bool kdvDahil = true,
        bool foto = true, string marka = "Fiat", string tip = "Egea")
    {
        using var s = host.ScopeFor(t);
        var araclar = s.ServiceProvider.GetRequiredService<VehicleService>();
        var fotograflar = s.ServiceProvider.GetRequiredService<VehiclePhotoService>();
        var ilanlar = s.ServiceProvider.GetRequiredService<WebIlanService>();

        var aracIdler = new List<Guid>();
        for (var i = 0; i < aracSayisi; i++)
        {
            var id = await araclar.CreateAsync(new VehicleInput
            {
                Plaka = "34VI" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(),
                Marka = marka, Tip = tip, Vites = Vites.Manuel, Yakit = FuelType.Dizel,
                ModelYili = 2023, Durum = VehicleStatus.Musait, GrupBilincliBos = true,
            });
            aracIdler.Add(id);
            if (foto && i == 0) await fotograflar.AddAsync(id, TinyPng); // FOTO YALNIZ İLKİNDE
        }

        var imza = (await ilanlar.HavuzAsync()).Single(k => k.Araclar.Count == aracSayisi).Imza;
        var ilanId = await ilanlar.AdimBirImzaAsync([imza]);
        await ilanlar.AdimIkiAsync(ilanId, gunluk, haftalik, aylik, kdvDahil);
        await ilanlar.AdimUcAsync(ilanId, [new OzellikSatiri("Marka", marka), new OzellikSatiri("Gizli", "x", Gorunur: false)]);
        return (ilanId, (await ilanlar.GetAsync(ilanId))!.Ilan.Slug, aracIdler);
    }

    private static FleetShowcaseService Vitrin(TestHost host, Guid t, out IServiceScope scope)
    {
        scope = host.ScopeFor(t, role: null);
        return scope.ServiceProvider.GetRequiredService<FleetShowcaseService>();
    }

    // ---- Yayın kapısı ----

    [Fact]
    public async Task Yayindaki_ilan_vitrinde_gorunur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var (_, slug, _) = await IlanKurAsync(host, t, aracSayisi: 3);

        var svc = Vitrin(host, t, out var scope); using (scope)
        {
            var kart = Assert.Single(await svc.ListShowcaseGroupsAsync());
            Assert.Equal("Fiat Egea Manuel Dizel", kart.Baslik);
            Assert.Equal(slug, kart.Slug);
            Assert.Equal(3, kart.Adet);
            Assert.Equal(1500m, kart.GunlukFiyat);
            Assert.NotNull(kart.CoverPhotoId);
        }
    }

    [Fact]
    public async Task Fotosuz_ilan_vitrine_GIRMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        await IlanKurAsync(host, t, foto: false);

        var svc = Vitrin(host, t, out var scope); using (scope)
            Assert.Empty(await svc.ListShowcaseGroupsAsync());
    }

    [Fact]
    public async Task Taslak_ilan_vitrine_GIRMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        using (var s = host.ScopeFor(t))
        {
            var araclar = s.ServiceProvider.GetRequiredService<VehicleService>();
            var id = await araclar.CreateAsync(new VehicleInput
            { Plaka = "34TSL001", Marka = "Fiat", Tip = "Egea", Durum = VehicleStatus.Musait, GrupBilincliBos = true });
            await s.ServiceProvider.GetRequiredService<VehiclePhotoService>().AddAsync(id, TinyPng);
            var ilanlar = s.ServiceProvider.GetRequiredService<WebIlanService>();
            var ilanId = await ilanlar.AdimBirImzaAsync([(await ilanlar.HavuzAsync()).Single().Imza]);
            await ilanlar.AdimIkiAsync(ilanId, 1500m, null, null, true); // adım-3 YAPILMADI → Taslak
        }

        var svc = Vitrin(host, t, out var scope); using (scope)
            Assert.Empty(await svc.ListShowcaseGroupsAsync());
    }

    [Fact]
    public async Task Pasife_alinan_ilan_vitrinden_DUSER()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var (ilanId, _, aracIdler) = await IlanKurAsync(host, t);

        using (var s = host.ScopeFor(t))
            await s.ServiceProvider.GetRequiredService<WebIlanService>().SetDurumAsync(ilanId, WebIlanDurum.Pasif);

        var svc = Vitrin(host, t, out var scope); using (scope)
            Assert.Empty(await svc.ListShowcaseGroupsAsync());
    }

    [Fact]
    public async Task SATILAN_arac_vitrinde_SAYILMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var (_, _, aracIdler) = await IlanKurAsync(host, t, aracSayisi: 2);

        // FOTOSUZ olan ikinci aracı sat (kapak aracı ilkidir) — kart kalmalı ama adet 1'e düşmeli.
        using (var s = host.ScopeFor(t))
        {
            var araclar = s.ServiceProvider.GetRequiredService<VehicleService>();
            var satilan = (await araclar.GetAsync(aracIdler[1]))!;
            await araclar.UpdateAsync(satilan.Id, new VehicleInput
            {
                Plaka = satilan.Plaka, Marka = satilan.Marka, Tip = satilan.Tip,
                Vites = satilan.Vites, Yakit = satilan.Yakit,
                Durum = VehicleStatus.Satildi, GrupBilincliBos = true,
            });
        }

        var svc = Vitrin(host, t, out var scope); using (scope)
            Assert.Equal(1, Assert.Single(await svc.ListShowcaseGroupsAsync()).Adet);
    }

    /// <summary>
    /// Görünürlük kapısı DETAY sayfasında sınanır: kart yalnız ilk üç çipi bastığı için ayrıca
    /// "başlıkta geçeni tekrar etme" ayıklamasından da geçer (bkz.
    /// <see cref="Kart_cipleri_BASLIKTA_gecen_degeri_tekrar_etmez"/>) — kapıyı orada ölçmek iki
    /// ayrı kuralı tek iddiada karıştırırdı.
    /// </summary>
    [Fact]
    public async Task Yalnizca_GORUNUR_ozellikler_siteye_cikar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var (_, slug, _) = await IlanKurAsync(host, t);

        var svc = Vitrin(host, t, out var scope); using (scope)
        {
            var detay = await svc.GetIlanDetayAsync(slug);
            Assert.NotNull(detay);
            Assert.Contains(detay!.Ozellikler, o => o.Etiket == "Marka");
            Assert.DoesNotContain(detay.Ozellikler, o => o.Etiket == "Gizli"); // personel kapatmıştı
        }
    }

    /// <summary>
    /// KART çipleri, değeri BAŞLIKTA zaten geçen özellikleri tekrar etmez.
    ///
    /// <para><b>Neden kural var:</b> sihirbaz özellikleri araç kaydından tohumluyor
    /// (Marka/Model/Vites/Yıl/Renk) ve başlık da aynı alanlardan kuruluyordu. Canlı denemede
    /// "Fiat Egea Manuel" başlıklı kartın çipleri "Fiat · Egea · Manuel" çıkıyordu: kart yalnız
    /// ilk üç çipi bastığı için ziyaretçiye YENİ hiçbir bilgi kalmıyor, yıl/renk/bagaj kesiliyordu.</para>
    ///
    /// <para>Bağımsız oracle: başlık "Fiat Egea Manuel Dizel"; "Fiat" ve "Dizel" ELENMELİ,
    /// "BEYAZ" ve "510 litre" KALMALI. DETAY sayfası ise tam listeyi sürdürür.</para>
    /// </summary>
    [Fact]
    public async Task Kart_cipleri_BASLIKTA_gecen_degeri_tekrar_etmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var (ilanId, slug, _) = await IlanKurAsync(host, t);

        using (var s = host.ScopeFor(t))
            await s.ServiceProvider.GetRequiredService<WebIlanService>().AdimUcAsync(ilanId,
            [
                new OzellikSatiri("Marka", "Fiat"),      // başlıkta VAR  → çipe girmez
                new OzellikSatiri("Yakıt", "Dizel"),     // başlıkta VAR  → çipe girmez
                new OzellikSatiri("Renk", "BEYAZ"),      // başlıkta YOK  → çipte kalır
                new OzellikSatiri("Bagaj", "510 litre")  // başlıkta YOK  → çipte kalır
            ]);

        var svc = Vitrin(host, t, out var scope); using (scope)
        {
            var kart = Assert.Single(await svc.ListShowcaseGroupsAsync());
            Assert.Equal("Fiat Egea Manuel Dizel", kart.Baslik);
            var cipler = kart.Ozellikler.Select(o => o.Deger).ToList();
            Assert.Equal(["BEYAZ", "510 litre"], cipler);

            // Detay TAM listeyi gösterir — ayıklama yalnız karta özel.
            var detay = await svc.GetIlanDetayAsync(slug);
            Assert.Equal(4, detay!.Ozellikler.Count);
            Assert.Contains(detay.Ozellikler, o => o.Deger == "Fiat");
        }
    }

    /// <summary>
    /// Aynı ayıklama MÜSAİTLİK sonucu kartlarında da geçerli (ikisi de KART yüzeyi) — biri
    /// ayıklayıp diğeri ayıklamasaydı ziyaretçi aynı aracı iki sayfada iki farklı çip setiyle görürdü.
    /// </summary>
    [Fact]
    public async Task Musaitlik_kartlari_da_ayni_cip_kuralini_uygular()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var (ilanId, _, _) = await IlanKurAsync(host, t);

        using (var s = host.ScopeFor(t))
            await s.ServiceProvider.GetRequiredService<WebIlanService>().AdimUcAsync(ilanId,
            [
                new OzellikSatiri("Marka", "Fiat"),
                new OzellikSatiri("Renk", "BEYAZ")
            ]);

        var svc = Vitrin(host, t, out var scope); using (scope)
        {
            var bas = DateTimeOffset.UtcNow.AddDays(30);
            var sonuc = Assert.Single(await svc.SearchAvailabilityAsync(bas, bas.AddDays(3), null));
            Assert.Equal(["BEYAZ"], sonuc.Ozellikler.Select(o => o.Deger));
        }
    }

    // ---- Detay + adres geçişi ----

    [Fact]
    public async Task Detay_SLUG_ile_acilir_yayinlanmamis_ilan_NULL()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var (ilanId, slug, _) = await IlanKurAsync(host, t);

        var svc = Vitrin(host, t, out var scope); using (scope)
        {
            Assert.NotNull(await svc.GetIlanDetayAsync(slug));
            Assert.Equal(slug, await svc.SlugByIdAsync(ilanId)); // eski GUID linki 301 için çözülür
            Assert.Null(await svc.GetIlanDetayAsync("olmayan-slug"));
        }

        using (var s = host.ScopeFor(t))
            await s.ServiceProvider.GetRequiredService<WebIlanService>().SetDurumAsync(ilanId, WebIlanDurum.Pasif);

        var svc2 = Vitrin(host, t, out var scope2); using (scope2)
        {
            Assert.Null(await svc2.GetIlanDetayAsync(slug));   // 404 → eski link içerik AÇMAZ
            Assert.Null(await svc2.SlugByIdAsync(ilanId));     // yönlendirme de yapılmaz → vitrine düşer
        }
    }

    [Fact]
    public async Task Ayni_baslikli_ilanlar_FARKLI_slug_alir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        using var s = host.ScopeFor(t);
        var araclar = s.ServiceProvider.GetRequiredService<VehicleService>();
        var ilanlar = s.ServiceProvider.GetRequiredService<WebIlanService>();

        var idler = new List<Guid>();
        for (var i = 1; i <= 3; i++)
            idler.Add(await araclar.CreateAsync(new VehicleInput
            {
                Plaka = $"34SLG{i:000}", Marka = "Fiat", Tip = "Egea", Vites = Vites.Manuel,
                Yakit = FuelType.Dizel, Durum = VehicleStatus.Musait, GrupBilincliBos = true,
            }));

        await ilanlar.AdimBirAsync(idler, beraber: false); // "ayrı" mod: 3 ilan, AYNI başlık

        // Blog'dan farklı: çakışma hata DEĞİL, otomatik son-ek. Aksi halde "ayrı" mod hiç çalışmazdı.
        var sluglar = new List<string>();
        foreach (var satir in await ilanlar.ListAsync())
            sluglar.Add((await ilanlar.GetAsync(satir.Id))!.Ilan.Slug);

        Assert.Equal(3, sluglar.Distinct().Count());
        Assert.Contains("fiat-egea-manuel-dizel", sluglar);
        Assert.Contains("fiat-egea-manuel-dizel-2", sluglar);
    }

    // ---- Fiyat: gün kademeleri (TUZAK-2) ----

    [Theory]
    [InlineData(3, 1500)]     // 1–7 gün → günlük fiyat
    [InlineData(10, 1285.71)] // 8–29 gün → haftalık TOPLAM / 7  = 9000/7
    [InlineData(35, 1066.67)] // 30+ gün → aylık TOPLAM / 30      = 32000/30
    public async Task Gun_kademesi_TOPLAM_alanlarindan_gunluge_cevrilir(int gun, decimal beklenenGunluk)
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        await IlanKurAsync(host, t, gunluk: 1500m, haftalik: 9000m, aylik: 32000m);

        var svc = Vitrin(host, t, out var scope); using (scope)
        {
            var bas = DateTimeOffset.UtcNow.AddDays(1);
            var r = Assert.Single(await svc.SearchAvailabilityAsync(bas, bas.AddDays(gun), null));

            // ELLE ORACLE: alanlar TOPLAM'dır (RateMatrix.GunHaftalik gibi GÜNLÜK ücret DEĞİL) —
            // karıştırılsaydı fiyat ~7 kat yanlış çıkardı.
            Assert.Equal(gun, r.Gun);
            Assert.Equal(beklenenGunluk, r.GunlukFiyat);
            Assert.Equal(Math.Round(beklenenGunluk * gun, 2), r.Toplam);
        }
    }

    [Fact]
    public async Task Ust_kademe_bossa_bir_alta_DUSER()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        await IlanKurAsync(host, t, gunluk: 1500m, haftalik: 9000m, aylik: null); // aylık YOK

        var svc = Vitrin(host, t, out var scope); using (scope)
        {
            var bas = DateTimeOffset.UtcNow.AddDays(1);
            var r = Assert.Single(await svc.SearchAvailabilityAsync(bas, bas.AddDays(35), null));
            Assert.Equal(1285.71m, r.GunlukFiyat); // 35 gün ama aylık yok → haftalıktan (9000/7)
        }
    }

    [Fact]
    public async Task Aramada_fiyat_MOTORDAN_DEGIL_ilandan_gelir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        await IlanKurAsync(host, t, gunluk: 1234m);

        // Tenant'ta HİÇ tarife matrisi yok — eski modelde arama boş dönerdi ("Tarife yok").
        var svc = Vitrin(host, t, out var scope); using (scope)
        {
            var bas = DateTimeOffset.UtcNow.AddDays(1);
            var r = Assert.Single(await svc.SearchAvailabilityAsync(bas, bas.AddDays(3), null));
            Assert.Equal(1234m, r.GunlukFiyat);
        }
    }

    [Fact]
    public async Task Aramada_adet_yalniz_MUSAIT_araclari_sayar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        await IlanKurAsync(host, t, aracSayisi: 3);

        var svc = Vitrin(host, t, out var scope); using (scope)
        {
            var bas = DateTimeOffset.UtcNow.AddDays(1);
            var r = Assert.Single(await svc.SearchAvailabilityAsync(bas, bas.AddDays(3), null));
            Assert.Equal(3, r.Adet);
        }
    }

    // ---- Talep zinciri (TUZAK-1) ----

    [Fact]
    public async Task Talep_fiyati_SUNUCUDAN_alinir_form_degerine_guvenilmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var (ilanId, _, aracIdler) = await IlanKurAsync(host, t, gunluk: 1500m, kdvDahil: true);

        using var s = host.ScopeFor(t, role: null);
        await s.ServiceProvider.GetRequiredService<PublicBookingRequestService>().CreateAsync(
            new PublicBookingRequestInput
            {
                AdSoyad = "Ali Veli", Telefon = "0555 000 11 22", IlanId = ilanId,
                BasTar = DateTimeOffset.UtcNow.AddDays(5), BitTar = DateTimeOffset.UtcNow.AddDays(8),
            });

        using var staff = host.ScopeFor(t);
        var talep = Assert.Single(await staff.ServiceProvider
            .GetRequiredService<PublicBookingRequestService>().ListAsync());

        // Fiyat girdide HİÇ YOK — servis ilandan çözdü. Ziyaretçi "fiyat=1" gönderemez.
        Assert.Equal(1500m, talep.GosterilenGunlukUcretKdvDahil);
        Assert.True(talep.GosterilenKdvDahil);
        Assert.Equal("Fiat Egea Manuel Dizel", talep.IlanBaslik);
    }

    [Fact]
    public async Task Talep_donusunce_rezervasyon_fiyati_ILANDAN_gelir_SIFIR_DEGIL()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var (ilanId, _, aracIdler) = await IlanKurAsync(host, t, gunluk: 1500m, kdvDahil: false); // NET fiyat

        var aracId = aracIdler[0];

        using (var s = host.ScopeFor(t, role: null))
            await s.ServiceProvider.GetRequiredService<PublicBookingRequestService>().CreateAsync(
                new PublicBookingRequestInput
                {
                    AdSoyad = "Ali Veli", Telefon = "0555 000 33 44", IlanId = ilanId,
                    BasTar = DateTimeOffset.UtcNow.AddDays(5), BitTar = DateTimeOffset.UtcNow.AddDays(8),
                });

        using var staff = host.ScopeFor(t);
        var svc = staff.ServiceProvider.GetRequiredService<PublicBookingRequestService>();
        var talepId = (await svc.ListAsync()).Single().Id;
        var rezId = await svc.DonusturAsync(talepId, aracId);

        // REGRESYON KİLİDİ (TUZAK-1): bu satır olmadan PricingService tarifeden çözmeye çalışır,
        // tenant'ta tarife olmadığı için 0 kalırdı → müşteri sitede 1.500 görür, sözleşmede 0 yazardı.
        // Ayrıca FiyatTuru "Otomatik" gönderilseydi manuel fiyat ZORLA sıfırlanırdı.
        // owner rolünde BYPASSRLS YOK → rezervasyon tenant kapsamlı servisten okunur.
        var rez = await staff.ServiceProvider
            .GetRequiredService<RentACar.Application.Bookings.ReservationService>().GetAsync(rezId);
        Assert.Equal(1500m, rez!.GunlukUcret);
    }

    [Fact]
    public async Task KDV_dahil_ilan_fiyati_sozlesmeye_NET_gecer()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var (ilanId, _, aracIdler) = await IlanKurAsync(host, t, gunluk: 1200m, kdvDahil: true);

        var aracId = aracIdler[0];

        using (var s = host.ScopeFor(t, role: null))
            await s.ServiceProvider.GetRequiredService<PublicBookingRequestService>().CreateAsync(
                new PublicBookingRequestInput
                {
                    AdSoyad = "Ali Veli", Telefon = "0555 000 55 66", IlanId = ilanId,
                    BasTar = DateTimeOffset.UtcNow.AddDays(5), BitTar = DateTimeOffset.UtcNow.AddDays(8),
                });

        using var staff = host.ScopeFor(t);
        var svc = staff.ServiceProvider.GetRequiredService<PublicBookingRequestService>();
        var rezId = await svc.DonusturAsync((await svc.ListAsync()).Single().Id, aracId);

        // ELLE ORACLE: ERP zinciri NET çalışır. 1200 brüt / 1,20 = 1000 net. Brütü olduğu gibi
        // geçirmek sözleşmeyi KDV oranı kadar ŞİŞİRİRDİ.
        var rez = await staff.ServiceProvider
            .GetRequiredService<RentACar.Application.Bookings.ReservationService>().GetAsync(rezId);
        Assert.Equal(1000m, rez!.GunlukUcret);
    }

    // ---- İzolasyon ----

    [Fact]
    public async Task Vitrin_tenant_izoledir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        await IlanKurAsync(host, t1);

        var svc = Vitrin(host, t2, out var scope); using (scope)
            Assert.Empty(await svc.ListShowcaseGroupsAsync());
    }
}
