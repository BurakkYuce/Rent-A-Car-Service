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
    private async Task<(Guid IlanId, string Slug, List<Guid> AracIdler)> SetupListingAsync(
        TestHost host, Guid t, int vehicleCount = 1, decimal daily = 1500m,
        decimal? weekly = null, decimal? monthly = null, bool vatIncluded = true,
        bool photo = true, string brand = "Fiat", string tip = "Egea")
    {
        using var s = host.ScopeFor(t);
        var vehicles = s.ServiceProvider.GetRequiredService<VehicleService>();
        var photos = s.ServiceProvider.GetRequiredService<VehiclePhotoService>();
        var listings = s.ServiceProvider.GetRequiredService<WebListingService>();

        var vehicleIds = new List<Guid>();
        for (var i = 0; i < vehicleCount; i++)
        {
            var id = await vehicles.CreateAsync(new VehicleInput
            {
                Plaka = "34VI" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(),
                Marka = brand, Tip = tip, Vites = Transmission.Manuel, Yakit = FuelType.Dizel,
                ModelYili = 2023, Durum = VehicleStatus.Musait, GrupBilincliBos = true,
            });
            vehicleIds.Add(id);
            if (photo && i == 0) await photos.AddAsync(id, TinyPng); // FOTO YALNIZ İLKİNDE
        }

        var signature = (await listings.PoolAsync()).Single(k => k.Araclar.Count == vehicleCount).Imza;
        var listingId = await listings.StepOneSignatureAsync([signature]);
        await listings.StepTwoAsync(listingId, daily, weekly, monthly, vatIncluded);
        await listings.StepThreeAsync(listingId, [new OzellikSatiri("Marka", brand), new OzellikSatiri("Gizli", "x", Gorunur: false)]);
        return (listingId, (await listings.GetAsync(listingId))!.Ilan.Slug, vehicleIds);
    }

    private static FleetShowcaseService Showcase(TestHost host, Guid t, out IServiceScope scope)
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
        var (_, slug, _) = await SetupListingAsync(host, t, vehicleCount: 3);

        var svc = Showcase(host, t, out var scope); using (scope)
        {
            var card = Assert.Single(await svc.ListShowcaseGroupsAsync());
            Assert.Equal("Fiat Egea Manuel Dizel", card.Baslik);
            Assert.Equal(slug, card.Slug);
            Assert.Equal(3, card.Adet);
            Assert.Equal(1500m, card.GunlukFiyat);
            Assert.NotNull(card.CoverPhotoId);
        }
    }

    [Fact]
    public async Task Fotosuz_ilan_vitrine_GIRMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        await SetupListingAsync(host, t, photo: false);

        var svc = Showcase(host, t, out var scope); using (scope)
            Assert.Empty(await svc.ListShowcaseGroupsAsync());
    }

    [Fact]
    public async Task Taslak_ilan_vitrine_GIRMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        using (var s = host.ScopeFor(t))
        {
            var vehicles = s.ServiceProvider.GetRequiredService<VehicleService>();
            var id = await vehicles.CreateAsync(new VehicleInput
            { Plaka = "34TSL001", Marka = "Fiat", Tip = "Egea", Durum = VehicleStatus.Musait, GrupBilincliBos = true });
            await s.ServiceProvider.GetRequiredService<VehiclePhotoService>().AddAsync(id, TinyPng);
            var listings = s.ServiceProvider.GetRequiredService<WebListingService>();
            var listingId = await listings.StepOneSignatureAsync([(await listings.PoolAsync()).Single().Imza]);
            await listings.StepTwoAsync(listingId, 1500m, null, null, true); // adım-3 YAPILMADI → Taslak
        }

        var svc = Showcase(host, t, out var scope); using (scope)
            Assert.Empty(await svc.ListShowcaseGroupsAsync());
    }

    [Fact]
    public async Task Pasife_alinan_ilan_vitrinden_DUSER()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var (listingId, _, vehicleIds) = await SetupListingAsync(host, t);

        using (var s = host.ScopeFor(t))
            await s.ServiceProvider.GetRequiredService<WebListingService>().SetStatusAsync(listingId, WebIlanDurum.Pasif);

        var svc = Showcase(host, t, out var scope); using (scope)
            Assert.Empty(await svc.ListShowcaseGroupsAsync());
    }

    [Fact]
    public async Task SATILAN_arac_vitrinde_SAYILMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var (_, _, vehicleIds) = await SetupListingAsync(host, t, vehicleCount: 2);

        // FOTOSUZ olan ikinci aracı sat (kapak aracı ilkidir) — kart kalmalı ama adet 1'e düşmeli.
        using (var s = host.ScopeFor(t))
        {
            var vehicles = s.ServiceProvider.GetRequiredService<VehicleService>();
            var sold = (await vehicles.GetAsync(vehicleIds[1]))!;
            await vehicles.UpdateAsync(sold.Id, new VehicleInput
            {
                Plaka = sold.Plaka, Marka = sold.Marka, Tip = sold.Tip,
                Vites = sold.Vites, Yakit = sold.Yakit,
                Durum = VehicleStatus.Satildi, GrupBilincliBos = true,
            });
        }

        var svc = Showcase(host, t, out var scope); using (scope)
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
        var (_, slug, _) = await SetupListingAsync(host, t);

        var svc = Showcase(host, t, out var scope); using (scope)
        {
            var detail = await svc.GetListingDetailAsync(slug);
            Assert.NotNull(detail);
            Assert.Contains(detail!.Ozellikler, o => o.Etiket == "Marka");
            Assert.DoesNotContain(detail.Ozellikler, o => o.Etiket == "Gizli"); // personel kapatmıştı
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
        var (listingId, slug, _) = await SetupListingAsync(host, t);

        using (var s = host.ScopeFor(t))
            await s.ServiceProvider.GetRequiredService<WebListingService>().StepThreeAsync(listingId,
            [
                new OzellikSatiri("Marka", "Fiat"),      // başlıkta VAR  → çipe girmez
                new OzellikSatiri("Yakıt", "Dizel"),     // başlıkta VAR  → çipe girmez
                new OzellikSatiri("Renk", "BEYAZ"),      // başlıkta YOK  → çipte kalır
                new OzellikSatiri("Bagaj", "510 litre")  // başlıkta YOK  → çipte kalır
            ]);

        var svc = Showcase(host, t, out var scope); using (scope)
        {
            var card = Assert.Single(await svc.ListShowcaseGroupsAsync());
            Assert.Equal("Fiat Egea Manuel Dizel", card.Baslik);
            var chips = card.Ozellikler.Select(o => o.Deger).ToList();
            Assert.Equal(["BEYAZ", "510 litre"], chips);

            // Detay TAM listeyi gösterir — ayıklama yalnız karta özel.
            var detail = await svc.GetListingDetailAsync(slug);
            Assert.Equal(4, detail!.Ozellikler.Count);
            Assert.Contains(detail.Ozellikler, o => o.Deger == "Fiat");
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
        var (listingId, _, _) = await SetupListingAsync(host, t);

        using (var s = host.ScopeFor(t))
            await s.ServiceProvider.GetRequiredService<WebListingService>().StepThreeAsync(listingId,
            [
                new OzellikSatiri("Marka", "Fiat"),
                new OzellikSatiri("Renk", "BEYAZ")
            ]);

        var svc = Showcase(host, t, out var scope); using (scope)
        {
            var start = DateTimeOffset.UtcNow.AddDays(30);
            var result = Assert.Single(await svc.SearchAvailabilityAsync(start, start.AddDays(3), null));
            Assert.Equal(["BEYAZ"], result.Ozellikler.Select(o => o.Deger));
        }
    }

    // ---- Detay + adres geçişi ----

    [Fact]
    public async Task Detay_SLUG_ile_acilir_yayinlanmamis_ilan_NULL()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var (listingId, slug, _) = await SetupListingAsync(host, t);

        var svc = Showcase(host, t, out var scope); using (scope)
        {
            Assert.NotNull(await svc.GetListingDetailAsync(slug));
            Assert.Equal(slug, await svc.SlugByIdAsync(listingId)); // eski GUID linki 301 için çözülür
            Assert.Null(await svc.GetListingDetailAsync("olmayan-slug"));
        }

        using (var s = host.ScopeFor(t))
            await s.ServiceProvider.GetRequiredService<WebListingService>().SetStatusAsync(listingId, WebIlanDurum.Pasif);

        var svc2 = Showcase(host, t, out var scope2); using (scope2)
        {
            Assert.Null(await svc2.GetListingDetailAsync(slug));   // 404 → eski link içerik AÇMAZ
            Assert.Null(await svc2.SlugByIdAsync(listingId));     // yönlendirme de yapılmaz → vitrine düşer
        }
    }

    [Fact]
    public async Task Ayni_baslikli_ilanlar_FARKLI_slug_alir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        using var s = host.ScopeFor(t);
        var vehicles = s.ServiceProvider.GetRequiredService<VehicleService>();
        var listings = s.ServiceProvider.GetRequiredService<WebListingService>();

        var ids = new List<Guid>();
        for (var i = 1; i <= 3; i++)
            ids.Add(await vehicles.CreateAsync(new VehicleInput
            {
                Plaka = $"34SLG{i:000}", Marka = "Fiat", Tip = "Egea", Vites = Transmission.Manuel,
                Yakit = FuelType.Dizel, Durum = VehicleStatus.Musait, GrupBilincliBos = true,
            }));

        await listings.StepOneAsync(ids, together: false); // "ayrı" mod: 3 ilan, AYNI başlık

        // Blog'dan farklı: çakışma hata DEĞİL, otomatik son-ek. Aksi halde "ayrı" mod hiç çalışmazdı.
        var slugs = new List<string>();
        foreach (var row in await listings.ListAsync())
            slugs.Add((await listings.GetAsync(row.Id))!.Ilan.Slug);

        Assert.Equal(3, slugs.Distinct().Count());
        Assert.Contains("fiat-egea-manuel-dizel", slugs);
        Assert.Contains("fiat-egea-manuel-dizel-2", slugs);
    }

    // ---- Fiyat: gün kademeleri (TUZAK-2) ----

    [Theory]
    [InlineData(3, 1500)]     // 1–7 gün → günlük fiyat
    [InlineData(10, 1285.71)] // 8–29 gün → haftalık TOPLAM / 7  = 9000/7
    [InlineData(35, 1066.67)] // 30+ gün → aylık TOPLAM / 30      = 32000/30
    public async Task Gun_kademesi_TOPLAM_alanlarindan_gunluge_cevrilir(int day, decimal expectedDaily)
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        await SetupListingAsync(host, t, daily: 1500m, weekly: 9000m, monthly: 32000m);

        var svc = Showcase(host, t, out var scope); using (scope)
        {
            var start = DateTimeOffset.UtcNow.AddDays(1);
            var r = Assert.Single(await svc.SearchAvailabilityAsync(start, start.AddDays(day), null));

            // ELLE ORACLE: alanlar TOPLAM'dır (RateMatrix.GunHaftalik gibi GÜNLÜK ücret DEĞİL) —
            // karıştırılsaydı fiyat ~7 kat yanlış çıkardı.
            Assert.Equal(day, r.Gun);
            Assert.Equal(expectedDaily, r.GunlukFiyat);
            Assert.Equal(Math.Round(expectedDaily * day, 2), r.Toplam);
        }
    }

    [Fact]
    public async Task Ust_kademe_bossa_bir_alta_DUSER()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        await SetupListingAsync(host, t, daily: 1500m, weekly: 9000m, monthly: null); // aylık YOK

        var svc = Showcase(host, t, out var scope); using (scope)
        {
            var start = DateTimeOffset.UtcNow.AddDays(1);
            var r = Assert.Single(await svc.SearchAvailabilityAsync(start, start.AddDays(35), null));
            Assert.Equal(1285.71m, r.GunlukFiyat); // 35 gün ama aylık yok → haftalıktan (9000/7)
        }
    }

    [Fact]
    public async Task Aramada_fiyat_MOTORDAN_DEGIL_ilandan_gelir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        await SetupListingAsync(host, t, daily: 1234m);

        // Tenant'ta HİÇ tarife matrisi yok — eski modelde arama boş dönerdi ("Tarife yok").
        var svc = Showcase(host, t, out var scope); using (scope)
        {
            var start = DateTimeOffset.UtcNow.AddDays(1);
            var r = Assert.Single(await svc.SearchAvailabilityAsync(start, start.AddDays(3), null));
            Assert.Equal(1234m, r.GunlukFiyat);
        }
    }

    [Fact]
    public async Task Aramada_adet_yalniz_MUSAIT_araclari_sayar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        await SetupListingAsync(host, t, vehicleCount: 3);

        var svc = Showcase(host, t, out var scope); using (scope)
        {
            var start = DateTimeOffset.UtcNow.AddDays(1);
            var r = Assert.Single(await svc.SearchAvailabilityAsync(start, start.AddDays(3), null));
            Assert.Equal(3, r.Adet);
        }
    }

    // ---- Talep zinciri (TUZAK-1) ----

    [Fact]
    public async Task Talep_fiyati_SUNUCUDAN_alinir_form_degerine_guvenilmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var (listingId, _, vehicleIds) = await SetupListingAsync(host, t, daily: 1500m, vatIncluded: true);

        using var s = host.ScopeFor(t, role: null);
        await s.ServiceProvider.GetRequiredService<PublicBookingRequestService>().CreateAsync(
            new PublicBookingRequestInput
            {
                AdSoyad = "Ali Veli", Telefon = "0555 000 11 22", IlanId = listingId,
                BasTar = DateTimeOffset.UtcNow.AddDays(5), BitTar = DateTimeOffset.UtcNow.AddDays(8),
            });

        using var staff = host.ScopeFor(t);
        var request = Assert.Single(await staff.ServiceProvider
            .GetRequiredService<PublicBookingRequestService>().ListAsync());

        // Fiyat girdide HİÇ YOK — servis ilandan çözdü. Ziyaretçi "fiyat=1" gönderemez.
        Assert.Equal(1500m, request.GosterilenGunlukUcretKdvDahil);
        Assert.True(request.GosterilenKdvDahil);
        Assert.Equal("Fiat Egea Manuel Dizel", request.IlanBaslik);
    }

    [Fact]
    public async Task Talep_donusunce_rezervasyon_fiyati_ILANDAN_gelir_SIFIR_DEGIL()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var (listingId, _, vehicleIds) = await SetupListingAsync(host, t, daily: 1500m, vatIncluded: false); // NET fiyat

        var vehicleId = vehicleIds[0];

        using (var s = host.ScopeFor(t, role: null))
            await s.ServiceProvider.GetRequiredService<PublicBookingRequestService>().CreateAsync(
                new PublicBookingRequestInput
                {
                    AdSoyad = "Ali Veli", Telefon = "0555 000 33 44", IlanId = listingId,
                    BasTar = DateTimeOffset.UtcNow.AddDays(5), BitTar = DateTimeOffset.UtcNow.AddDays(8),
                });

        using var staff = host.ScopeFor(t);
        var svc = staff.ServiceProvider.GetRequiredService<PublicBookingRequestService>();
        var requestId = (await svc.ListAsync()).Single().Id;
        var resId = await svc.ConvertAsync(requestId, vehicleId);

        // REGRESYON KİLİDİ (TUZAK-1): bu satır olmadan PricingService tarifeden çözmeye çalışır,
        // tenant'ta tarife olmadığı için 0 kalırdı → müşteri sitede 1.500 görür, sözleşmede 0 yazardı.
        // Ayrıca FiyatTuru "Otomatik" gönderilseydi manuel fiyat ZORLA sıfırlanırdı.
        // owner rolünde BYPASSRLS YOK → rezervasyon tenant kapsamlı servisten okunur.
        var res = await staff.ServiceProvider
            .GetRequiredService<RentACar.Application.Bookings.ReservationService>().GetAsync(resId);
        Assert.Equal(1500m, res!.GunlukUcret);
    }

    [Fact]
    public async Task KDV_dahil_ilan_fiyati_sozlesmeye_NET_gecer()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var (listingId, _, vehicleIds) = await SetupListingAsync(host, t, daily: 1200m, vatIncluded: true);

        var vehicleId = vehicleIds[0];

        using (var s = host.ScopeFor(t, role: null))
            await s.ServiceProvider.GetRequiredService<PublicBookingRequestService>().CreateAsync(
                new PublicBookingRequestInput
                {
                    AdSoyad = "Ali Veli", Telefon = "0555 000 55 66", IlanId = listingId,
                    BasTar = DateTimeOffset.UtcNow.AddDays(5), BitTar = DateTimeOffset.UtcNow.AddDays(8),
                });

        using var staff = host.ScopeFor(t);
        var svc = staff.ServiceProvider.GetRequiredService<PublicBookingRequestService>();
        var resId = await svc.ConvertAsync((await svc.ListAsync()).Single().Id, vehicleId);

        // ELLE ORACLE: ERP zinciri NET çalışır. 1200 brüt / 1,20 = 1000 net. Brütü olduğu gibi
        // geçirmek sözleşmeyi KDV oranı kadar ŞİŞİRİRDİ.
        var res = await staff.ServiceProvider
            .GetRequiredService<RentACar.Application.Bookings.ReservationService>().GetAsync(resId);
        Assert.Equal(1000m, res!.GunlukUcret);
    }

    // ---- İzolasyon ----

    [Fact]
    public async Task Vitrin_tenant_izoledir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        await SetupListingAsync(host, t1);

        var svc = Showcase(host, t2, out var scope); using (scope)
            Assert.Empty(await svc.ListShowcaseGroupsAsync());
    }
}
