using System.Net;
using Microsoft.EntityFrameworkCore;
using static RentACar.IntegrationTests.SystemApiTestKit;

namespace RentACar.IntegrationTests;

/// <summary>
/// V0 — halka açık site bilgileri <c>/api/ui/v1/ayarlar</c> tam değiştirme PUT'unda: kayıt + geri okuma, alan bağlı 400'ler,
/// ManageUsers dışı 403. Beklenen değerler testte elle yazıldı (servis kodundan türetilmez).
/// </summary>
public sealed partial class UiSystemAdminTests
{
    private static object SiteInfoBody(string? version, decimal? deposit = 5000m, int? km = 300, bool unlimited = false,
        int? minAge = 21, int? licenceYears = 2, decimal? rating = 4.8m, int? reviews = 312,
        string? profile = "https://maps.app.goo.gl/abc123", int? founded = 2012)
        => new
        {
            firmaUnvan = "Deneme A.Ş.", faturaSeriKodu = "RNT", surum = version,
            siteDepozitoTutari = deposit, siteDepozitoAciklama = "Kredi kartı şart değil, nakit depozito olur.",
            siteGunlukKmSiniri = km, siteKmSinirsiz = unlimited, siteMinYas = minAge, siteMinEhliyetYili = licenceYears,
            siteSigortaOzeti = "Kasko dahil, muafiyet 10.000 TL.", siteIptalKosulu = "48 saat öncesine kadar ücretsiz iptal.",
            siteGooglePuani = rating, siteGoogleYorumSayisi = reviews, siteGoogleProfilUrl = profile, siteKurulusYili = founded,
        };

    [Fact]
    public async Task Site_info_round_trips_and_stamps_rating_time()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var first = await Json(await admin.C.GetAsync(Settings));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, first.GetProperty("siteGooglePuani").ValueKind);
        Assert.False(first.GetProperty("siteKmSinirsiz").GetBoolean());

        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var saved = await Json(await Send(admin, HttpMethod.Put, Settings, SiteInfoBody(first.GetProperty("surum").GetString())));
        var after = DateTimeOffset.UtcNow.AddSeconds(1);
        Assert.Equal(5000m, saved.GetProperty("siteDepozitoTutari").GetDecimal());
        Assert.Equal("Kredi kartı şart değil, nakit depozito olur.", saved.GetProperty("siteDepozitoAciklama").GetString());
        Assert.Equal(300, saved.GetProperty("siteGunlukKmSiniri").GetInt32());
        Assert.Equal(21, saved.GetProperty("siteMinYas").GetInt32());
        Assert.Equal(2, saved.GetProperty("siteMinEhliyetYili").GetInt32());
        Assert.Equal("Kasko dahil, muafiyet 10.000 TL.", saved.GetProperty("siteSigortaOzeti").GetString());
        Assert.Equal("48 saat öncesine kadar ücretsiz iptal.", saved.GetProperty("siteIptalKosulu").GetString());
        Assert.Equal(4.8m, saved.GetProperty("siteGooglePuani").GetDecimal());
        Assert.Equal(312, saved.GetProperty("siteGoogleYorumSayisi").GetInt32());
        Assert.Equal("https://maps.app.goo.gl/abc123", saved.GetProperty("siteGoogleProfilUrl").GetString());
        Assert.Equal(2012, saved.GetProperty("siteKurulusYili").GetInt32());
        var stamp = saved.GetProperty("siteGooglePuanTarihi").GetDateTimeOffset();
        Assert.InRange(stamp, before, after);

        // Puan değişmeden yapılan kayıt damgayı değiştirmez; km sınırsıza geçer.
        saved = await Json(await Send(admin, HttpMethod.Put, Settings,
            SiteInfoBody(saved.GetProperty("surum").GetString(), km: null, unlimited: true)));
        Assert.True(saved.GetProperty("siteKmSinirsiz").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, saved.GetProperty("siteGunlukKmSiniri").ValueKind);
        Assert.Equal(stamp, saved.GetProperty("siteGooglePuanTarihi").GetDateTimeOffset());

        // Puan silinince damga da silinir.
        saved = await Json(await Send(admin, HttpMethod.Put, Settings,
            SiteInfoBody(saved.GetProperty("surum").GetString(), rating: null, reviews: null, profile: null)));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, saved.GetProperty("siteGooglePuanTarihi").ValueKind);
        Assert.Null(await _kit.ReadAsync(e.TenantId, db => db.TenantSettings.AsNoTracking().Select(x => x.SiteGooglePuani).FirstAsync()));
    }

    [Fact]
    public async Task Site_info_validation_maps_to_fields_and_writes_nothing()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var version = (await Json(await admin.C.GetAsync(Settings))).GetProperty("surum").GetString();

        foreach (var (body, field) in new (object, string)[]
                 {
                     (SiteInfoBody(version, rating: 5.1m), "siteGooglePuani"),
                     (SiteInfoBody(version, rating: 0.9m), "siteGooglePuani"),
                     (SiteInfoBody(version, rating: 4.75m), "siteGooglePuani"),
                     (SiteInfoBody(version, rating: null, reviews: 10), "siteGooglePuani"),
                     (SiteInfoBody(version, profile: null), "siteGoogleProfilUrl"),
                     (SiteInfoBody(version, profile: "http://maps.app.goo.gl/abc123"), "siteGoogleProfilUrl"),
                     (SiteInfoBody(version, profile: "https://google.com.evil.example/x"), "siteGoogleProfilUrl"),
                     (SiteInfoBody(version, profile: "javascript:alert(1)"), "siteGoogleProfilUrl"),
                     (SiteInfoBody(version, profile: "https://g.page/" + new string('a', 520)), "siteGoogleProfilUrl"),
                     (SiteInfoBody(version, unlimited: true), "siteGunlukKmSiniri"),
                     (SiteInfoBody(version, km: 0), "siteGunlukKmSiniri"),
                     (SiteInfoBody(version, minAge: 17), "siteMinYas"),
                     (SiteInfoBody(version, licenceYears: -1), "siteMinEhliyetYili"),
                     (SiteInfoBody(version, deposit: -1m), "siteDepozitoTutari"),
                     (SiteInfoBody(version, deposit: 10.005m), "siteDepozitoTutari"),
                     (SiteInfoBody(version, founded: DateTime.UtcNow.Year + 2), "siteKurulusYili"),
                     (SiteInfoBody(version, founded: 1899), "siteKurulusYili"),
                 })
            await Problem(await Send(admin, HttpMethod.Put, Settings, body), HttpStatusCode.BadRequest, "dogrulama", field);

        Assert.Null(await _kit.ReadAsync(e.TenantId, db => db.TenantSettings.AsNoTracking().Select(x => x.SiteDepozitoTutari).FirstOrDefaultAsync()));
    }

    [Theory]
    [InlineData(Who.Manager)]
    [InlineData(Who.OperatorA)]
    [InlineData(Who.Accounting)]
    public async Task Site_info_requires_manage_users(Who who)
    {
        var e = await _kit.SetupAsync();
        var s = await _kit.LoginAsync(e, who);
        await Problem(await Send(s, HttpMethod.Put, Settings, SiteInfoBody("1")), HttpStatusCode.Forbidden, "yetki_yok");
        Assert.Null(await _kit.ReadAsync(e.TenantId, db => db.TenantSettings.AsNoTracking().Select(x => x.SiteDepozitoTutari).FirstOrDefaultAsync()));
    }
}
