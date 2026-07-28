using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

public enum BlogPostDurum
{
    Taslak = 0,
    Yayinda = 1
}

/// <summary>
/// Halka açık site blog yazısı (PR-6). Kapak görseli <see cref="VehiclePhoto"/> ile AYNI bytea+thumbnail
/// desenini kullanır (paylaşılan ImageValidation/ImageProcessing), tek fark: çoklu galeri değil TEK kapak
/// (TenantSettings.LogoBytes gibi) — Sira/MoveAsync YOK.
///
/// İçerik DÜZ METİN saklanır (Markdown/HTML editör YOK): PublicSite'ta paragraflara bölünüp normal Razor
/// ifadesiyle basılır (`&lt;p&gt;@p&lt;/p&gt;`), MarkupString KULLANILMAZ → Razor otomatik HTML-encode eder,
/// XSS risk sınıfı tasarım gereği ortadan kalkar.
/// </summary>
public class BlogPost : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public string Baslik { get; set; } = string.Empty;

    /// <summary>URL parçası — `TurkishText.Normalize` ile başlıktan türetilir, (TenantId, Slug) benzersiz.
    /// Yazı İLK KEZ yayınlandıktan (<see cref="YayinTarihi"/> dolduktan) SONRA DONAR: aksi halde başlık
    /// düzenlemesi mevcut linkleri ve sitemap'i kırar.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Liste kartında gösterilen kısa özet (opsiyonel).</summary>
    public string? Ozet { get; set; }

    public string Icerik { get; set; } = string.Empty;

    public byte[]? KapakBytes { get; set; }

    /// <summary>Küçük resim (VehiclePhoto ile aynı üretim: uzun kenar 400px, JPEG q80, EXIF-döndürülmüş).</summary>
    public byte[]? KapakThumbBytes { get; set; }

    /// <summary>"image/png" | "image/jpeg" | "image/webp" — magic-byte'tan belirlenir.</summary>
    public string? KapakContentType { get; set; }

    public BlogPostDurum Durum { get; set; }

    /// <summary>null = HİÇ yayınlanmadı (slug hâlâ değiştirilebilir). Dolduğu an slug donar; liste
    /// sıralama anahtarı. `default(DateTimeOffset)` sentinel'i yerine bilinçli nullable.</summary>
    public DateTimeOffset? YayinTarihi { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
