using Microsoft.AspNetCore.Http;
using RentACar.Application.Fleet;
using RentACar.PublicSite;

namespace RentACar.IntegrationTests;

/// <summary>
/// Saf kararlar (DB yok): <see cref="NoIndexHeaderMiddleware.Decide"/> ve kanonik kök biçimleyicisi.
/// Beklenenler elle yazıldı. HTTP üzerinden uçtan uca doğrulama: PublicSiteSeoTests.
/// </summary>
public sealed class PublicSiteNoIndexKararTests
{
    [Theory]
    [InlineData("/rezervasyon-talebi", "", "noindex")]
    [InlineData("/Rezervasyon-Talebi/", "", "noindex")]
    [InlineData("/rezervasyon-talebi/gonder", "", "noindex")]
    [InlineData("/talep-alindi", "?id=1", "noindex")]
    [InlineData("/cok-istek", "", "noindex")]
    [InlineData("/musaitlik", "?ilan=x", "noindex, follow")]
    [InlineData("/musaitlik/", "?bas=a&bit=b", "noindex, follow")]
    [InlineData("/musaitlik", "", null)]
    [InlineData("/musaitlik", "?", null)]
    [InlineData("/", "", null)]
    [InlineData("/araclar/fiat-egea", "", null)]
    [InlineData("/rezervasyon-talebi-rehberi", "", null)] // önek benzerliği işlem sayfası SAYILMAZ
    [InlineData("/blog/musaitlik", "?x=1", null)]
    public void Karar(string path, string query, string? expected)
        => Assert.Equal(expected, NoIndexHeaderMiddleware.Decide(new PathString(path),
            string.IsNullOrEmpty(query) ? QueryString.Empty : new QueryString(query)));

    [Theory]
    [InlineData("marka.com", "https://marka.com")]
    [InlineData(" marka.com/ ", "https://marka.com")]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void Kanonik_kok(string? host, string? expected)
        => Assert.Equal(expected, FleetShowcaseService.CanonicalRoot(host));
}
