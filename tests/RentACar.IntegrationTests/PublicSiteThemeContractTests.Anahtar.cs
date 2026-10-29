using System.Net;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Branches;
using RentACar.Application.Fleet;
using RentACar.Application.TenantSettings;
using RentACar.Domain.Entities;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.PublicSite.Components.Themes;

namespace RentACar.IntegrationTests;

/// <summary>
/// Anahtar teması (varsayılan): firma bilgileri ilk ekranda ve koşul listelerinde — dolu alan basılır, BOŞ alan hiçbir
/// öğe üretmez (T2: boş kutu yok); "bugün açık" satırı şube çalışma saatinden ve yalnız kesin okunabildiğinde.
/// BAĞIMSIZ ORACLE: beklenen metinler ve saatler elle yazıldı (biçimlendirici çağrılarak üretilmedi).
/// </summary>
public sealed partial class PublicSiteThemeContractTests
{
    private static Branch Hours(string? shortText, string? weekly = null)
        => new() { Ad = "X", Kod = "X", CalismaSaatleri = shortText, HaftalikCalismaSaatleri = weekly };

    [Theory]
    [InlineData("09:00-18:00", null, DayOfWeek.Sunday, "09:00–18:00")]
    [InlineData("Her gün 08:00-21:00", null, DayOfWeek.Tuesday, "08:00–21:00")]
    [InlineData("Hafta içi 08:30-19:00, Cumartesi 09:00-17:00", null, DayOfWeek.Monday, "08:30–19:00")]
    [InlineData("Hafta içi 08:30-19:00, Cumartesi 09:00-17:00", null, DayOfWeek.Saturday, "09:00–17:00")]
    [InlineData("Hafta içi 08:30-19:00, Cumartesi 09:00-17:00", null, DayOfWeek.Sunday, null)]
    [InlineData("Mo-Sa 09:00-19:00", null, DayOfWeek.Wednesday, "09:00–19:00")]
    [InlineData("Mo-Sa 09:00-19:00", null, DayOfWeek.Sunday, null)]
    [InlineData("Mo,We 9.00-18.00", null, DayOfWeek.Wednesday, "09:00–18:00")]
    [InlineData("Mo,We 9.00-18.00", null, DayOfWeek.Tuesday, null)]
    [InlineData("10:00-20:00", "Pazartesi: 09:00-18:00\nPazar kapalı", DayOfWeek.Monday, "09:00–18:00")]
    [InlineData("10:00-20:00", "Pazartesi: 09:00-18:00\nPazar kapalı", DayOfWeek.Tuesday, "10:00–20:00")]
    [InlineData("10:00-20:00", "Pazartesi: 09:00-18:00\nPazar kapalı", DayOfWeek.Sunday, null)]
    [InlineData("Pzt-Cuma 08:00-17:30", null, DayOfWeek.Friday, "08:00–17:30")]
    [InlineData("7/24 açık", null, DayOfWeek.Sunday, "24 saat")]
    [InlineData("Randevu ile", null, DayOfWeek.Monday, null)]
    [InlineData("25:00-26:00", null, DayOfWeek.Monday, null)]
    [InlineData(null, null, DayOfWeek.Monday, null)]
    public void Bugunku_calisma_saati_yalniz_kesin_okunabildiginde(string? shortText, string? weekly, DayOfWeek day, string? expected)
        => Assert.Equal(expected, BranchOffices.TodayHours(Hours(shortText, weekly), day));

    /// <summary>Kullanıcı kararı: Anahtar'da açılış animasyonu yok — arama formu hiçbir karede örtülmez.</summary>
    [Theory]
    [InlineData("anahtar")]
    [InlineData("kiyas")]
    public void Acilis_animasyonu_yok(string key)
    {
        var css = ReadCss(ThemeFile(SiteThemes.Resolve(key)));
        Assert.DoesNotContain("@keyframes", css);
        Assert.DoesNotMatch(@"(?<![\w-])animation(-name)?\s*:", css);
    }

    [Fact]
    public void Firma_bilgisi_bos_alan_satir_ve_cip_uretmez()
    {
        Assert.Empty(SiteFactsText.Lines(null));
        Assert.Empty(SiteFactsText.Lines(new SiteFacts()));
        Assert.Empty(SiteFactsText.Chips(new SiteFacts()));
        Assert.Null(SiteFactsText.Founded(new SiteFacts()));
        // Bağlantısız puan basılmaz (servis zaten reddeder; tema da güvenmez).
        Assert.Null(SiteFactsText.Rating(new SiteFacts(GooglePuani: 4.5m)));

        var full = new SiteFacts(DepozitoTutari: 5000m, DepozitoAciklama: "Nakit olur.", GunlukKmSiniri: 300, MinYas: 21,
            MinEhliyetYili: 2, SigortaOzeti: "Kasko dahil.", IptalKosulu: "48 saat öncesine kadar ücretsiz.",
            GooglePuani: 4.8m, GoogleYorumSayisi: 1312, GoogleProfilUrl: "https://maps.app.goo.gl/x", KurulusYili: 2012);
        Assert.Equal(
            [
                new FactLine("Depozito", "₺5.000. Nakit olur."), new FactLine("Kilometre", "Günlük 300 km"),
                new FactLine("Sürücü", "En az 21 yaş, en az 2 yıllık ehliyet"), new FactLine("Sigorta", "Kasko dahil."),
                new FactLine("İptal", "48 saat öncesine kadar ücretsiz."),
            ],
            SiteFactsText.Lines(full));
        Assert.Equal(["Depozito", "Kilometre", "İptal"], SiteFactsText.Short(full).Select(l => l.Etiket));
        Assert.Equal(["Depozito ₺5.000", "Günlük 300 km", "En az 21 yaş"], SiteFactsText.Chips(full));
        Assert.Equal("2012 yılından beri", SiteFactsText.Founded(full));
        Assert.Equal("4,8", SiteFactsText.Rating(full));
        Assert.Equal("1.312 Google yorumu", SiteFactsText.RatingNote(full));

        var other = new SiteFacts(DepozitoTutari: 0m, KmSinirsiz: true, MinEhliyetYili: 3);
        Assert.Equal(["Depozito yok", "Sınırsız km"], SiteFactsText.Chips(other));
        Assert.Equal(
            [new FactLine("Depozito", "Alınmıyor"), new FactLine("Kilometre", "Sınırsız"), new FactLine("Sürücü", "En az 3 yıllık ehliyet")],
            SiteFactsText.Lines(other));
    }

    [Fact]
    public async Task Anahtar_firma_bilgisi_doluyken_ilk_ekranda_ve_detayda_basilir()
    {
        var tenant = await SeedTenantAsync("anahtar");
        var slug = await SeedContentAsync(tenant);
        using (var host = new TestHost(fx.AppConnectionString))
        using (var s = host.ScopeFor(tenant))
        {
            var svc = s.ServiceProvider.GetRequiredService<TenantSettingsService>();
            var m = await svc.GetAsync();
            m.SiteDepozitoTutari = 5000m;
            m.SiteGunlukKmSiniri = 300;
            m.SiteMinYas = 21;
            m.SiteSigortaOzeti = "Kasko dahil.";
            m.SiteIptalKosulu = "48 saat öncesine kadar ücretsiz iptal.";
            m.SiteGooglePuani = 4.8m;
            m.SiteGoogleYorumSayisi = 312;
            m.SiteGoogleProfilUrl = "https://maps.app.goo.gl/ornek";
            m.SiteKurulusYili = 2012;
            await svc.SaveAsync(m);
            await s.ServiceProvider.GetRequiredService<BranchService>().CreateAsync(new BranchInput
            {
                Kod = "ALS", Ad = "Alsancak", Il = "İzmir", Ilce = "Konak", Aktif = true, CalismaSaatleri = "Her gün 08:00-21:00",
            });
        }

        using var factory = new PublicSiteFactory(fx, tenant);
        var client = factory.Client();

        var home = WebUtility.HtmlDecode(await client.GetStringAsync("/"));
        Assert.Contains("2012 yılından beri", home);
        Assert.Contains("<strong>4,8</strong>", home);
        Assert.Contains("312 Google yorumu", home);
        Assert.Contains("href=\"https://maps.app.goo.gl/ornek\"", home);
        Assert.Contains("Alsancak bugün 08:00–21:00 açık", home);  // iki şube: hangisi olduğu söylenir
        Assert.Contains("Depozito ₺5.000", home);                  // tezgâh çipi
        Assert.Contains("Kiralamadan önce", home);
        Assert.Contains("<dt>Sigorta</dt><dd>Kasko dahil.</dd>", home);
        Assert.Contains("Fiyatları göster", home);

        var list = WebUtility.HtmlDecode(await client.GetStringAsync($"/musaitlik?bas={Day(30)}&bit={Day(33)}"));
        Assert.Contains("Tüm araçlarda geçerli koşullar", list);
        Assert.Contains("Günlük 300 km", list);

        var detail = WebUtility.HtmlDecode(await client.GetStringAsync($"/araclar/{slug}"));
        Assert.Contains("<dt>İptal</dt><dd>48 saat öncesine kadar ücretsiz iptal.</dd>", detail);
        Assert.Contains("<dt>Depozito</dt><dd>₺5.000</dd>", detail);
        Assert.Contains("class=\"detay-hizli an-hizli\"", detail);
    }

    [Fact]
    public async Task Anahtar_firma_bilgisi_bosken_hicbir_oge_basilmaz()
    {
        var tenant = await SeedTenantAsync(null); // kayıtlı tema yok → varsayılan anahtar
        var slug = await SeedContentAsync(tenant); // şubede saat yok, firma bilgisi yok

        using var factory = new PublicSiteFactory(fx, tenant);
        var client = factory.Client();

        var home = await client.GetStringAsync("/");
        Assert.Contains("data-tema=\"anahtar\"", home);
        foreach (var absent in new[] { "an-guven", "an-cipler", "an-yorum", "kosul-baslik", "yılından beri", "Google yorum", "bugün ", "Bugün " })
            Assert.DoesNotContain(absent, WebUtility.HtmlDecode(home));

        var list = await client.GetStringAsync("/musaitlik");
        Assert.DoesNotContain("an-cipler", list);

        var detail = await client.GetStringAsync($"/araclar/{slug}");
        Assert.DoesNotContain("an-kosul-kisa", detail);
        Assert.DoesNotContain("an-kosul-panel", detail);
    }
}
