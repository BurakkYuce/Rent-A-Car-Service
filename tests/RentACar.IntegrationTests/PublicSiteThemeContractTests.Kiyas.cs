using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Fleet;
using RentACar.Application.Vehicles;
using RentACar.Application.WebSite;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.PublicSite.Components.Themes;

namespace RentACar.IntegrationTests;

/// <summary>
/// Kıyas teması: JS'siz GET süzgeci (FleetFilter — vites/yakıt/koltuk/sınıf, sayfa süzer, tema basar), koşul rozetleri
/// ve şeffaflık satırları. BAĞIMSIZ ORACLE: beklenen değerler elle kurulan ilanlardan yazıldı.
/// </summary>
public sealed partial class PublicSiteThemeContractTests
{
    private static ListingCard Car(string slug, decimal daily, params (string Label, string Value)[] features)
        => new(slug, slug, null, null, daily, true, [], 1, null, null, "/t")
        {
            AllFeatures = [.. features.Select(f => new FeatureItem(f.Label, f.Value))],
        };

    private static Dictionary<string, string[]> Pick(params (string Key, string[] Values)[] picks)
    {
        var d = FleetFilter.Dimensions.ToDictionary(x => x.Key, _ => Array.Empty<string>());
        foreach (var (k, v) in picks) d[k] = v;
        return d;
    }

    [Fact]
    public void Suzgec_boyutlari_veriden_ve_secim_boyut_icinde_veya_boyutlar_arasinda_ve()
    {
        var egea = Car("egea", 1650m, ("Vites", "Manuel"), ("Yakıt", "Dizel"), ("Koltuk", "5 kişilik"));
        var clio = Car("clio", 1750m, ("VİTES", "Otomatik"), ("Yakıt", "Benzin"), ("Koltuk", "5 kişilik"));
        var tucson = Car("tucson", 3450m, ("Vites", "otomatik"), ("Yakıt Tipi", "Benzin"), ("Koltuk", "7 kişilik"), ("Renk", "Gri"));
        var all = new[] { egea, clio, tucson };

        // Boyutlar: vites (Manuel 1, Otomatik 2 — büyük/küçük harf aynı değer), yakıt (Benzin 2, Dizel 1),
        // koltuk (5 kişilik 2, 7 kişilik 1). Sınıf yok (veri yok); renk süzgeç boyutu değil.
        var facets = FleetFilter.Facets(all, Pick());
        Assert.Equal(["vites", "yakit", "koltuk"], facets.Select(f => f.Key));
        Assert.Equal([("Manuel", 1), ("Otomatik", 2)], facets[0].Options.Select(o => (o.Value, o.Count)));
        Assert.Equal([("Benzin", 2), ("Dizel", 1)], facets[1].Options.Select(o => (o.Value, o.Count)));
        Assert.Equal(["5 kişilik", "7 kişilik"], facets[2].Options.Select(o => o.Value));

        // Tek değerli boyut sunulmaz.
        Assert.Empty(FleetFilter.Facets([egea, Car("x", 1m, ("Vites", "Manuel"))], Pick()));

        bool Match(ListingCard c, params (string, string[])[] p) => FleetFilter.Matches(c, FleetFilter.Effective(all, Pick(p)));
        Assert.Equal(["clio", "tucson"], all.Where(c => Match(c, ("vites", ["OTOMATİK"]))).Select(c => c.Slug));
        Assert.Equal(["egea", "clio", "tucson"], all.Where(c => Match(c, ("vites", ["Manuel", "Otomatik"]))).Select(c => c.Slug));
        Assert.Equal(["tucson"], all.Where(c => Match(c, ("vites", ["Otomatik"]), ("koltuk", ["7 kişilik"]))).Select(c => c.Slug));
        // Tanınmayan değer yok sayılır (elle yazılmış adres listeyi boşaltmaz).
        Assert.Equal(3, all.Count(c => Match(c, ("vites", ["Yarı otomatik"]))));

        Assert.Equal("Manuel, Dizel, 5 kişilik", FleetFilter.SpecLine(egea));
        Assert.Null(FleetFilter.SpecLine(Car("y", 1m, ("Renk", "Mavi"))));
        Assert.Equal("/musaitlik?bas=2026-01-02&sube=Lara%20Ofis",
            new FleetFilterModel([], [new("bas", "2026-01-02"), new("sube", "Lara Ofis")], 0).ClearHref);
    }

    [Fact]
    public void Rozetler_veri_turunden_siniflanir()
    {
        Assert.Empty(SiteFactsText.Badges(new SiteFacts()));
        Assert.Equal(
            [("Depozitosuz", "iyi"), ("Sınırsız km", "iyi"), ("21+ yaş", "dikkat"), ("2+ yıl ehliyet", "dikkat")],
            SiteFactsText.Badges(new SiteFacts(DepozitoTutari: 0m, KmSinirsiz: true, MinYas: 21, MinEhliyetYili: 2)));
        Assert.Equal([("Depozito ₺5.000", "notr"), ("1.000 km/gün", "notr")],
            SiteFactsText.Badges(new SiteFacts(DepozitoTutari: 5000m, GunlukKmSiniri: 1000)));
    }

    /// <summary>İkinci yayında ilan: Renault Clio, Vites "Otomatik", günlük 1.800 TL (isteğe bağlı 30 günlük toplam),
    /// fotoğraflı. Slug döner.</summary>
    private async Task<string> SeedAutomaticListingAsync(Guid t, decimal? monthly = null)
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(t);
        var sp = s.ServiceProvider;
        var vehicleId = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        {
            Plaka = "35KY" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(),
            Marka = "Renault", Tip = "Clio", Vites = Transmission.Otomatik, Yakit = FuelType.Benzin,
            ModelYili = 2024, Durum = VehicleStatus.Musait, GrupBilincliBos = true,
        });
        await sp.GetRequiredService<VehiclePhotoService>().AddAsync(vehicleId, TinyPng);
        var listings = sp.GetRequiredService<WebListingService>();
        var signature = (await listings.PoolAsync()).Single(k => k.Araclar.Any(v => v.Id == vehicleId)).Imza;
        var listingId = await listings.StepOneSignatureAsync([signature]);
        await listings.StepTwoAsync(listingId, 1800m, null, monthly, true);
        await listings.StepThreeAsync(listingId, [new OzellikSatiri("Vites", "Otomatik")]);
        return (await listings.GetAsync(listingId))!.Ilan.Slug;
    }

    [Fact]
    public async Task Kiyas_suzgeci_GET_ile_suzer_arama_alanlarini_tasir_ve_temizlenir()
    {
        var tenant = await SeedTenantAsync("kiyas");
        var manual = await SeedContentAsync(tenant);           // Fiat Egea, Vites: Manuel, 1.500/gün
        var automatic = await SeedAutomaticListingAsync(tenant); // Renault Clio, Vites: Otomatik, 1.800/gün

        using var factory = new PublicSiteFactory(fx, tenant);
        var client = factory.Client();

        var browse = WebUtility.HtmlDecode(await client.GetStringAsync("/musaitlik"));
        Assert.Contains("data-tema=\"kiyas\"", browse);
        Assert.Contains("name=\"vites\" value=\"Manuel\"", browse);
        Assert.Contains("name=\"vites\" value=\"Otomatik\"", browse);
        Assert.Contains($"href=\"/araclar/{manual}\"", browse);
        Assert.Contains($"href=\"/araclar/{automatic}\"", browse);

        var dated = $"/musaitlik?bas={Day(30)}&bit={Day(33)}";
        var filtered = WebUtility.HtmlDecode(await client.GetStringAsync(dated + "&vites=Otomatik"));
        Assert.Contains($"href=\"/araclar/{automatic}\"", filtered);
        Assert.DoesNotContain($"href=\"/araclar/{manual}\"", filtered);
        Assert.Contains("1 araç müsait", filtered);
        Assert.Matches(@"name=""vites"" value=""Otomatik"" checked", filtered);
        Assert.Contains($"type=\"hidden\" name=\"bas\" value=\"{Day(30)}\"", filtered);  // arama korunur
        Assert.Contains("₺1.800 × 3 gün", filtered);                                     // gün × fiyat dökümü
        Assert.Contains("₺5.400", filtered);                                             // 1.800 × 3 (elle)
        Assert.Contains($"href=\"/musaitlik?bas={Day(30)}&bit={Day(33)}\">Temizle", filtered);
        // Kullanıcı kararı: kartta fiyat (toplam + döküm) adla aynı blokta, teknik satırdan ve rozetlerden ÖNCE.
        var card = filtered[filtered.IndexOf("class=\"ky-kart\"", StringComparison.Ordinal)..];
        Assert.True(card.IndexOf("ky-tutar", StringComparison.Ordinal) < card.IndexOf("ky-teknik", StringComparison.Ordinal));
        Assert.True(card.IndexOf("ky-dokum", StringComparison.Ordinal) < card.IndexOf("ky-teknik", StringComparison.Ordinal));

        // Tanınmayan değer yok sayılır: iki ilan da listede.
        var unknown = WebUtility.HtmlDecode(await client.GetStringAsync("/musaitlik?vites=Yar%C4%B1+otomatik"));
        Assert.Contains($"href=\"/araclar/{manual}\"", unknown);
        Assert.Contains($"href=\"/araclar/{automatic}\"", unknown);

        // Ana sayfa: boyut başına en düşük günlük fiyat kutucuğu (Manuel 1.500, Otomatik 1.800 — elle).
        var home = WebUtility.HtmlDecode(await client.GetStringAsync("/"));
        Assert.Contains("href=\"/musaitlik?vites=Otomatik\"", home);
        Assert.Matches(@"Otomatik</span>\s*<span class=""ky-kutu-fiyat"">₺1\.800", home);
        Assert.Matches(@"Manuel</span>\s*<span class=""ky-kutu-fiyat"">₺1\.500", home);
        Assert.Contains("Fiyatlar KDV dahil", home);  // iki ilan da KDV dahil
        Assert.Single(Regex.Matches(home, @"<h1[\s>]"));
    }
}
