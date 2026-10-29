using System.Globalization;
using RentACar.Application.Fleet;

namespace RentACar.PublicSite.Components.Themes;

/// <summary>"Kiralamadan önce" listesinin bir satırı: etiket + firmanın beyanı.</summary>
public sealed record FactLine(string Label, string Text);

/// <summary>
/// Firmanın beyanlarından (<see cref="SiteFacts"/>) temaların ortak metinleri (Anahtar, Kıyas). Yalnız biçimler; metin
/// firmanın girdiğidir (T1: firma adına vaat yazılmaz). Boş alan satır/çip ÜRETMEZ (T2) — boş kutu basılmaz.
/// </summary>
public static class SiteFactsText
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Tam liste (ana sayfa "Kiralamadan önce", ilan detayı paneli): depozito, km, sürücü, sigorta, iptal.</summary>
    public static IReadOnlyList<FactLine> Lines(SiteFacts? f)
    {
        if (f is null) return [];
        var list = new List<FactLine>(5);
        if (Deposit(f) is { } deposit) list.Add(new("Depozito", deposit));
        if (Km(f) is { } km) list.Add(new("Kilometre", km));
        if (Driver(f) is { } driver) list.Add(new("Sürücü", driver));
        if (Text(f.SigortaOzeti) is { } insurance) list.Add(new("Sigorta", insurance));
        if (Text(f.IptalKosulu) is { } cancel) list.Add(new("İptal", cancel));
        return list;
    }

    /// <summary>Kısa satırlar (mobil ilk ekran, ilan detayı): depozito, km, iptal — kararı en çok etkileyen üçü.</summary>
    public static IReadOnlyList<FactLine> Short(SiteFacts? f)
        => [.. Lines(f).Where(l => l.Label is "Depozito" or "Kilometre" or "İptal")];

    /// <summary>Çipler (arama kutusunun altı, liste özeti): "Depozito ₺5.000", "Günlük 300 km", "En az 21 yaş".</summary>
    public static IReadOnlyList<string> Chips(SiteFacts? f)
    {
        if (f is null) return [];
        var list = new List<string>(3);
        if (f.DepozitoTutari is { } amount) list.Add(amount == 0 ? "Depozito yok" : $"Depozito {PriceText.Tl(amount)}");
        if (Km(f) is { } km) list.Add(f.KmSinirsiz ? "Sınırsız km" : km);
        if (f.MinYas is { } age) list.Add($"En az {age} yaş");
        return list;
    }

    /// <summary>Rozetler (Kıyas): olumlu = depozitosuz, sınırsız km; dikkat = asgari yaş/ehliyet şartı; nötr = depozito
    /// tutarı, günlük km sınırı. Sınıflandırma yalnız verinin türünden — firma adına nitelik yazılmaz.</summary>
    public static IReadOnlyList<(string Text, string Kind)> Badges(SiteFacts? f)
    {
        if (f is null) return [];
        var list = new List<(string, string)>(4);
        if (f.DepozitoTutari is { } amount) list.Add(amount == 0 ? ("Depozitosuz", "iyi") : ($"Depozito {PriceText.Tl(amount)}", "notr"));
        if (f.KmSinirsiz) list.Add(("Sınırsız km", "iyi"));
        else if (f.GunlukKmSiniri is { } km) list.Add(($"{PriceText.Count(km)} km/gün", "notr"));
        if (f.MinYas is { } age) list.Add(($"{age}+ yaş", "dikkat"));
        if (f.MinEhliyetYili is { } years and > 0) list.Add(($"{years}+ yıl ehliyet", "dikkat"));
        return list;
    }

    /// <summary>"2012 yılından beri" (ek sesli uyumu yıla bakmadan doğru: "yılından").</summary>
    public static string? Founded(SiteFacts? f) => f?.KurulusYili is { } y ? $"{y} yılından beri" : null;

    /// <summary>Google puanı "4,8" — yalnız bağlantı da varsa (servis kuralı; burada da bağlantısız basılmaz).</summary>
    public static string? Rating(SiteFacts? f)
        => f?.GooglePuani is { } r && !string.IsNullOrWhiteSpace(f.GoogleProfilUrl) ? r.ToString("0.0", Tr) : null;

    /// <summary>"312 Google yorumu" ya da yorum sayısı yoksa "Google puanı".</summary>
    public static string RatingNote(SiteFacts f)
        => f.GoogleYorumSayisi is { } n ? $"{PriceText.Count(n)} Google yorumu" : "Google puanı";

    /// <summary>"Ekim 2026" — puanın girildiği ay (puan zamanla değişir; ziyaretçi tarihini görsün).</summary>
    public static string? RatingMonth(SiteFacts? f) => f?.GooglePuanTarihi?.ToString("MMMM yyyy", Tr);

    private static string? Deposit(SiteFacts f)
    {
        var note = Text(f.DepozitoAciklama);
        if (f.DepozitoTutari is not { } amount) return note;
        var head = amount == 0 ? "Alınmıyor" : PriceText.Tl(amount);
        return note is null ? head : $"{head}. {note}";
    }

    private static string? Km(SiteFacts f)
        => f.KmSinirsiz ? "Sınırsız" : f.GunlukKmSiniri is { } km ? $"Günlük {PriceText.Count(km)} km" : null;

    private static string? Driver(SiteFacts f)
    {
        var age = f.MinYas is { } a ? $"En az {a} yaş" : null;
        var licence = f.MinEhliyetYili is { } y and > 0 ? $"{y} yıllık ehliyet" : null;
        return (age, licence) switch
        {
            ({ } x, { } l) => $"{x}, en az {l}",
            ({ } x, null) => x,
            (null, { } l) => $"En az {l}",
            _ => null,
        };
    }

    private static string? Text(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
