using RentACar.Domain.Entities;

namespace RentACar.Application.Blog;

/// <summary>Blog yazısı oluştur/güncelle girdisi. <see cref="Slug"/> boşsa başlıktan türetilir; yazı
/// ilk kez yayınlandıktan sonra slug DEĞİŞTİRİLEMEZ (servis yok sayar — link/sitemap kırılmasın).</summary>
public sealed class BlogInput
{
    public string Baslik { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string? Ozet { get; set; }
    public string Icerik { get; set; } = string.Empty;
    public BlogPostDurum Durum { get; set; } = BlogPostDurum.Taslak;

    // ---- SEO alanları (hepsi opsiyonel; boş bırakılan alan için sayfa mevcut davranışına düşer) ----
    public string? AltBaslik { get; set; }
    public string? SeoBaslik { get; set; }
    public string? MetaAciklama { get; set; }
    public string? AnahtarKelimeler { get; set; }
    public string? Yazar { get; set; }
    public string? KapakAlt { get; set; }
    public bool AramaDisi { get; set; }
}
