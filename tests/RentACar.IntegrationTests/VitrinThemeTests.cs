using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Branches;
using RentACar.Application.Fleet;
using RentACar.Application.TenantSettings;
using RentACar.Application.Vehicles;
using RentACar.Application.WebSite;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.PublicSite.Components.Themes.Vitrin;

namespace RentACar.IntegrationTests;

/// <summary>
/// Vitrin teması: segment vurgusu (saf sınıflandırma) ve ana sayfa / müsaitlik / detay yerleşimi (gerçek PublicSite
/// boru hattı). BAĞIMSIZ ORACLE: beklenen segment üyeleri ve sayılar elle kurulan senaryodan yazıldı (sınıflandırıcı
/// çağrılarak üretilmedi).
/// </summary>
[Collection("postgres")]
public sealed class VitrinThemeTests(PostgresFixture fx)
{
    private static FleetShowcaseCard Card(string slug, Guid? photo, params (string Label, string Value)[] features)
        => new(Guid.NewGuid(), slug, slug, null, photo, 1000m, true, 1, [],
            TumOzellikler: [.. features.Select(f => new OzellikGoster(f.Label, f.Value))]);

    [Fact]
    public void Segmentler_yalniz_ilgili_etiketlerden_ve_kelime_kelime_eslesir()
    {
        var elektrik = Card("zoe", Guid.NewGuid(), ("Yakıt", "Elektrik"));
        var suvHibrit = Card("tucson", Guid.NewGuid(), ("YAKIT", "Plug-in Hibrit"), ("Kasa Tipi", "SUV"));
        var luks = Card("s-serisi", Guid.NewGuid(), ("Segment", "LÜKS"));
        // "Lüks paket" bir DONANIM satırı: aracı lüks sınıfına sokmaz. "Suvari" kelimesi "suv" değildir.
        var donanim = Card("egea", Guid.NewGuid(), ("Donanım", "Lüks paket"), ("Kasa Tipi", "Suvari"), ("Yakıt", "Benzin"));

        var s = VitrinSegments.From([elektrik, suvHibrit, luks, donanim]);

        Assert.Equal(["luks", "suv", "elektrik"], s.Select(x => x.Key));
        Assert.Equal(["s-serisi"], s[0].Kartlar.Select(c => c.Slug));
        Assert.Equal(["tucson"], s[1].Kartlar.Select(c => c.Slug));
        Assert.Equal(["zoe", "tucson"], s[2].Kartlar.Select(c => c.Slug));
        Assert.Equal(["Lüks", "SUV", "Elektrikli ve hibrit"], s.Select(x => x.Ad));
    }

    [Fact]
    public void Segment_adi_veriden_ve_filonun_tamamini_kapsayan_segment_basilmaz()
    {
        var a = Card("a", Guid.NewGuid(), ("Yakıt", "Hibrit"));
        var b = Card("b", Guid.NewGuid(), ("Yakıt", "Dizel"));
        Assert.Equal("Hibrit", Assert.Single(VitrinSegments.From([a, b])).Ad);
        Assert.Equal("Elektrikli", Assert.Single(VitrinSegments.From([Card("e", null, ("Yakıt", "Elektrikli")), b])).Ad);

        // Her araç hibrit: kutu hiçbir şeyi öne çıkarmaz → yok. Özellik yoksa da yok (T2).
        Assert.Empty(VitrinSegments.From([a, Card("c", null, ("Yakıt", "Hibrit"))]));
        Assert.Empty(VitrinSegments.From([Card("x", null), Card("y", null)]));
        Assert.Empty(VitrinSegments.From([]));
    }

    /// <summary>Kart çipleri başlıkta geçen değeri atar ("… Otomatik Hibrit" başlığında "Hibrit" çipi yok); sınıflandırma
    /// tam listeye bakar. Tam liste yoksa (eski çağıran) çiplere düşer.</summary>
    [Fact]
    public void Siniflandirma_tam_ozellik_listesine_bakar_yoksa_ciplere_duser()
    {
        var full = new FleetShowcaseCard(Guid.NewGuid(), "x", "Volvo XC60 Otomatik Hibrit", null, null, 1m, true, 1,
            Ozellikler: [], TumOzellikler: [new("Yakıt", "Hibrit")]);
        var chipsOnly = new FleetShowcaseCard(Guid.NewGuid(), "y", "Y", null, null, 1m, true, 1,
            Ozellikler: [new("Kasa Tipi", "SUV")]);
        var other = Card("z", null, ("Yakıt", "Dizel"));
        Assert.Equal(["elektrik"], VitrinSegments.From([full, other]).Select(s => s.Key));
        Assert.Equal(["suv"], VitrinSegments.From([chipsOnly, other]).Select(s => s.Key));
    }

    [Fact]
    public void Kapak_sahnedeki_ve_baska_kutudaki_fotografi_tekrarlamaz()
    {
        Guid p1 = Guid.NewGuid(), p2 = Guid.NewGuid(), p3 = Guid.NewGuid();
        var a = Card("a", p1, ("Yakıt", "Hibrit"), ("Kasa Tipi", "SUV"));
        var b = Card("b", p2, ("Yakıt", "Hibrit"), ("Kasa Tipi", "SUV"));
        var c = Card("c", p3, ("Yakıt", "Dizel"));

        var s = VitrinSegments.From([a, b, c], usedPhoto: p1);
        // SUV kutusu sahnedeki p1'i atlar → p2; hibrit kutusu p2'yi de atlar ama başka fotoğraf yok → ilk fotoğraflı (p1).
        Assert.Equal(p2, s.Single(x => x.Key == "suv").Kapak!.CoverPhotoId);
        Assert.Equal(p1, s.Single(x => x.Key == "elektrik").Kapak!.CoverPhotoId);
    }

    // ---- Canlı boru hattı ----

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAoAAAAICAIAAABPmPnhAAAAFElEQVR4nGM8YWTEgBsw4ZEb0tIAKaUBPDvSacQAAAAASUVORK5CYII=");

    /// <summary>Vitrin temalı firma: iki yayında ilan — "Fiat Egea Manuel Dizel" (Kasa Tipi=Sedan) ve "Renault Zoe
    /// Otomatik Elektrik" (Yakıt=Elektrik; değer başlıkta da geçtiği için kart çipi OLMAZ). Slug'lar döner.</summary>
    private async Task<(Guid Tenant, string Egea, string Zoe)> SeedAsync()
    {
        await using (var db = new AppDbContext(
                         new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options,
                         NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            var t = new Tenant { Code = "vt" + Guid.NewGuid().ToString("N")[..10], Name = "Vitrin Deneme", IsActive = true };
            db.Tenants.Add(t);
            await db.SaveChangesAsync();
            var tenant = t.Id;

            using var host = new TestHost(fx.AppConnectionString);
            using var s = host.ScopeFor(tenant);
            var sp = s.ServiceProvider;
            var settings = sp.GetRequiredService<TenantSettingsService>();
            var m = await settings.GetAsync();
            m.SiteTemasi = "vitrin";
            await settings.SaveAsync(m);
            await sp.GetRequiredService<BranchService>().CreateAsync(new BranchInput
            {
                Kod = "VTM", Ad = "Vitrin Merkez", Il = "İstanbul", Ilce = "Beşiktaş", Aktif = true,
            });

            var vehicles = sp.GetRequiredService<VehicleService>();
            var photos = sp.GetRequiredService<VehiclePhotoService>();
            var egeaId = await vehicles.CreateAsync(new VehicleInput
            {
                Plaka = "34VT" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(),
                Marka = "Fiat", Tip = "Egea", Vites = Transmission.Manuel, Yakit = FuelType.Dizel,
                ModelYili = 2024, Durum = VehicleStatus.Musait, GrupBilincliBos = true,
            });
            var zoeId = await vehicles.CreateAsync(new VehicleInput
            {
                Plaka = "34VZ" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(),
                Marka = "Renault", Tip = "Zoe", Vites = Transmission.Otomatik, Yakit = FuelType.Elektrik,
                ModelYili = 2024, Durum = VehicleStatus.Musait, GrupBilincliBos = true,
            });
            await photos.AddAsync(egeaId, TinyPng);
            await photos.AddAsync(zoeId, TinyPng);

            var listings = sp.GetRequiredService<WebListingService>();
            var pool = await listings.PoolAsync();
            async Task<string> Publish(Guid vehicleId, decimal price, OzellikSatiri feature)
            {
                var id = await listings.StepOneSignatureAsync([pool.Single(k => k.Araclar.Any(v => v.Id == vehicleId)).Imza]);
                await listings.StepTwoAsync(id, price, null, null, true);
                await listings.StepThreeAsync(id, [feature]);
                return (await listings.GetAsync(id))!.Ilan.Slug;
            }
            var egea = await Publish(egeaId, 1500m, new OzellikSatiri("Kasa Tipi", "Sedan"));
            var zoe = await Publish(zoeId, 2500m, new OzellikSatiri("Yakıt", "Elektrik"));
            return (tenant, egea, zoe);
        }
    }

    [Fact]
    public async Task Ana_sayfa_serit_segment_ve_kurumsal_blok_musaitlik_izgara_detay_galeri()
    {
        var (tenant, egea, zoe) = await SeedAsync();

        // Kart çipi başlıkta geçen değeri atar; tam liste korur (sınıflandırmanın dayanağı).
        using (var host = new TestHost(fx.AppConnectionString))
        using (var s = host.ScopeFor(tenant))
        {
            var cards = await s.ServiceProvider.GetRequiredService<FleetShowcaseService>().ListShowcaseGroupsAsync();
            var zoeCard = cards.Single(c => c.Slug == zoe);
            Assert.DoesNotContain(zoeCard.Ozellikler, o => o.Deger == "Elektrik");
            Assert.Contains(zoeCard.TumOzellikler!, o => o is { Etiket: "Yakıt", Deger: "Elektrik" });
        }

        using var factory = new PublicSiteFactory(fx, tenant);
        var client = factory.Client();

        var home = WebUtility.HtmlDecode(await client.GetStringAsync("/"));
        Assert.Contains("data-tema=\"vitrin\"", home);
        // Filo şeridi: odaklanabilir bölge, ne olduğunu ve nasıl gezildiğini söyleyen ad; iki kartta ipucu metni yok.
        Assert.Contains("class=\"v-serit\" role=\"region\" tabindex=\"0\" aria-label=\"Filomuz, 2 araç, yana kaydırılır\"", home);
        Assert.DoesNotContain("Yana kaydırın", home);
        Assert.Contains("<li class=\"v-serit-oge v-serit-son\"><a href=\"/musaitlik\">", home);
        // Segment: yalnız elektrikli (Zoe), Egea'nın sedan kasası bir segment değil.
        Assert.Contains("data-segment=\"elektrik\"", home);
        Assert.DoesNotContain("data-segment=\"suv\"", home);
        Assert.DoesNotContain("data-segment=\"luks\"", home);
        Assert.Contains("Elektrikli</h3>", home);
        Assert.Contains($"<li><a href=\"/araclar/{zoe}\">", home);
        // Kurumsal blok kanal yoksa iletişim sayfasına bağlanır (T2: olmayan numara basılmaz).
        Assert.Contains("Şirketiniz için kiralama", home);
        Assert.Contains("<p class=\"v-kurumsal-yedek\"><a href=\"/iletisim\">", home);
        Assert.DoesNotContain("tel:", home);

        var browse = await client.GetStringAsync("/musaitlik");
        Assert.Contains("class=\"ilanlar v-izgara\"", browse);
        Assert.DoesNotContain("v-serit", browse);

        // Tek fotoğraflı ilan: şerit yerine tam genişlik kapak (LCP: tembel değil, yüksek öncelik).
        var detail = await client.GetStringAsync($"/araclar/{egea}");
        Assert.Contains("<figure class=\"v-galeri-tek\">", detail);
        Assert.Contains("fetchpriority=\"high\"", detail);
        Assert.DoesNotContain("role=\"region\"", detail);
    }
}
