using RentACar.Application.Common;

namespace RentACar.Application.TenantSettings;

/// <summary>
/// Halka açık firma sitesi bilgileri (V0): depozito, km, yaş/ehliyet, sigorta, iptal, Google puanı, kuruluş yılı.
/// Hepsi firmanın beyanıdır, hiçbir hesaba bağlı değildir; site boş alanı basmaz. Hatalar alana bağlıdır
/// (<c>errors[siteX]</c>).
/// </summary>
public static class SiteInfoRules
{
    public const decimal MaxDeposit = 10_000_000m;
    public const int MaxDailyKm = 10_000;

    /// <summary>Kısa bağlantı ve profil alan adları; <c>google.com</c> / <c>google.com.tr</c> alt alan adları da geçer.</summary>
    private static readonly string[] ShortHosts = ["g.page", "goo.gl", "maps.app.goo.gl", "g.co"];

    /// <summary>
    /// Google profil bağlantısı: mutlak <c>https</c>, varsayılan port, kullanıcı bilgisi yok, alan adı Google'ın.
    /// Sitede <c>&lt;a href&gt;</c> olarak basılır — başka şema (javascript:, http) ya da başka alan adı (sahte "yorum"
    /// sayfası) kabul edilmez.
    /// </summary>
    public static bool IsGoogleProfileUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps
            || !u.IsDefaultPort || u.UserInfo.Length > 0) return false;
        var host = u.IdnHost.ToLowerInvariant();
        return ShortHosts.Contains(host)
               || host is "google.com" or "google.com.tr"
               || host.EndsWith(".google.com", StringComparison.Ordinal)
               || host.EndsWith(".google.com.tr", StringComparison.Ordinal);
    }

    /// <summary>Model → varlık. Puan ya da yorum sayısı değiştiyse tarih damgası <paramref name="now"/> olur.</summary>
    public static void Apply(Domain.Entities.TenantSettings s, TenantSettingsModel m, DateTimeOffset now)
    {
        if (m.SiteDepozitoTutari is { } deposit
            && (deposit < 0 || deposit > MaxDeposit || decimal.Round(deposit, 2) != deposit))
            throw new ValidationException("Depozito 0 ile 10.000.000 TL arasında, en çok 2 ondalık olmalıdır.", "siteDepozitoTutari");
        if (m.SiteGunlukKmSiniri is < 1 or > MaxDailyKm)
            throw new ValidationException("Günlük km sınırı 1 ile 10.000 arasında olmalıdır.", "siteGunlukKmSiniri");
        if (m.SiteKmSinirsiz && m.SiteGunlukKmSiniri is not null)
            throw new ValidationException("Sınırsız km seçiliyken günlük km sınırı girilemez.", "siteGunlukKmSiniri");
        if (m.SiteMinYas is < 18 or > 99)
            throw new ValidationException("Asgari yaş 18 ile 99 arasında olmalıdır.", "siteMinYas");
        if (m.SiteMinEhliyetYili is < 0 or > 60)
            throw new ValidationException("Asgari ehliyet yılı 0 ile 60 arasında olmalıdır.", "siteMinEhliyetYili");
        if (m.SiteKurulusYili is { } year && (year < 1900 || year > now.Year))
            throw new ValidationException($"Kuruluş yılı 1900 ile {now.Year} arasında olmalıdır.", "siteKurulusYili");

        if (m.SiteGooglePuani is { } rating && (rating < 1m || rating > 5m || decimal.Round(rating, 1) != rating))
            throw new ValidationException("Google puanı 1,0 ile 5,0 arasında, en çok bir ondalık olmalıdır.", "siteGooglePuani");
        if (m.SiteGoogleYorumSayisi is < 0 or > 10_000_000)
            throw new ValidationException("Google yorum sayısı 0 ile 10.000.000 arasında olmalıdır.", "siteGoogleYorumSayisi");
        if (m.SiteGoogleYorumSayisi is not null && m.SiteGooglePuani is null)
            throw new ValidationException("Google yorum sayısı için puan da girilmelidir.", "siteGooglePuani");
        var link = Trim(m.SiteGoogleProfilUrl);
        if (link is not null && !IsGoogleProfileUrl(link))
            throw new ValidationException(
                "Google profil bağlantısı https:// ile başlayan bir Google adresi olmalıdır (google.com, g.page, maps.app.goo.gl).",
                "siteGoogleProfilUrl");
        // Doğrulanabilir olmayan puan basılmaz: ziyaretçi bağlantıdan kontrol edebilmeli.
        if (m.SiteGooglePuani is not null && link is null)
            throw new ValidationException("Google puanı için profil bağlantısı zorunludur.", "siteGoogleProfilUrl");

        if (m.SiteGooglePuani != s.SiteGooglePuani || m.SiteGoogleYorumSayisi != s.SiteGoogleYorumSayisi)
            s.SiteGooglePuanTarihiUtc = m.SiteGooglePuani is null ? null : now;
        s.SiteDepozitoTutari = m.SiteDepozitoTutari;
        s.SiteDepozitoAciklama = Trim(m.SiteDepozitoAciklama);
        s.SiteGunlukKmSiniri = m.SiteGunlukKmSiniri;
        s.SiteKmSinirsiz = m.SiteKmSinirsiz;
        s.SiteMinYas = m.SiteMinYas;
        s.SiteMinEhliyetYili = m.SiteMinEhliyetYili;
        s.SiteSigortaOzeti = Trim(m.SiteSigortaOzeti);
        s.SiteIptalKosulu = Trim(m.SiteIptalKosulu);
        s.SiteGooglePuani = m.SiteGooglePuani;
        s.SiteGoogleYorumSayisi = m.SiteGoogleYorumSayisi;
        s.SiteGoogleProfilUrl = link;
        s.SiteKurulusYili = m.SiteKurulusYili;
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
