using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// V0 — ortak arama formu her temada DOLU açılır (alış yarın İstanbul günü, iade +3 gün, saatler 10:00) ve saatli
/// arama gün sayısını saatle hesaplar; saatsiz (ya da tek saatli) eski bağlantı eski sonucu verir. Beklenenler elle:
/// ilan günlük 1.500 TL; 3 gün = 4.500 TL; 3 gün + 4 saat, 3 saatlik kısmi gün eşiğini aştığı için 4 gün = 6.000 TL.
/// </summary>
public sealed partial class PublicSiteThemeContractTests
{
    private static readonly TimeZoneInfo IstanbulZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    private static string IstanbulDay(int offset)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, IstanbulZone).DateTime).AddDays(offset)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? InputValue(string html, string name)
        => Regex.Match(html, $@"<input name=""{name}"" type=""(?:date|time)"" value=""([^""]*)""") is { Success: true } m
            ? m.Groups[1].Value : null;

    [Theory]
    [MemberData(nameof(ThemeKeys))]
    public async Task Arama_formu_dolu_acilir_ve_saatli_arama_gunu_saatle_sayar(string key)
    {
        var tenant = await SeedTenantAsync(key);
        await SeedContentAsync(tenant);
        using var factory = new PublicSiteFactory(fx, tenant);
        var client = factory.Client();

        // Ana sayfa: varsayılan dolu (gece yarısı sınırına denk gelirse iki günden biri).
        var first = (IstanbulDay(1), IstanbulDay(4));
        var home = await client.GetStringAsync("/");
        var last = (IstanbulDay(1), IstanbulDay(4));
        var got = (InputValue(home, "bas"), InputValue(home, "bit"));
        Assert.True(got == first || got == last, $"{key}: varsayılan tarihler {got}");
        Assert.Equal("10:00", InputValue(home, "basSaat"));
        Assert.Equal("10:00", InputValue(home, "bitSaat"));

        var dated = $"/musaitlik?bas={Day(30)}&bit={Day(33)}";

        // Saatsiz eski bağlantı ve tek saatli bağlantı: eski davranış (yalnız günler).
        foreach (var path in new[] { dated, dated + "&basSaat=10:00" })
        {
            var visible = WebUtility.HtmlDecode(await client.GetStringAsync(path));
            Assert.Contains(", 3 gün.", visible);
            Assert.Contains("₺4.500", visible);
            Assert.Equal(Day(30), InputValue(visible, "bas"));
            Assert.Equal(Day(33), InputValue(visible, "bit"));
        }

        // İki saat de geçerli: 10:00 → 14:00, 3 gün + 4 saat = 4 gün; form seçilen saatleri korur.
        using var r = await client.GetAsync(dated + "&basSaat=10:00&bitSaat=14:00");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var timed = WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());
        Assert.Contains(", 4 gün.", timed);
        Assert.Contains("₺6.000", timed);
        Assert.Equal("10:00", InputValue(timed, "basSaat"));
        Assert.Equal("14:00", InputValue(timed, "bitSaat"));

        // Dolu ama biçimsiz saat SESSİZCE düşmez (PR #412 L1): hata görünür, aralık fiyatı hesaplanmaz, tarihler
        // formda korunur; saat alanı biçimsiz değeri basamaz → varsayılan.
        var bad = WebUtility.HtmlDecode(await client.GetStringAsync(dated + "&basSaat=25:00&bitSaat=x"));
        Assert.Contains("Saat biçimi geçersiz", bad);
        Assert.DoesNotContain(", 3 gün.", bad);
        Assert.Equal(Day(30), InputValue(bad, "bas"));
        Assert.Equal("10:00", InputValue(bad, "basSaat"));
    }
}
