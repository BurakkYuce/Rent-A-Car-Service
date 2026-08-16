using RentACar.Domain.Entities;

namespace RentACar.Application.Blog;

/// <summary>Liste kartı — BLOB kolonları TAŞIMAZ (kapak ayrı uçtan serve edilir).</summary>
public sealed record BlogListItem(
    Guid Id, string Baslik, string Slug, string? Ozet, BlogPostDurum Durum,
    DateTimeOffset? YayinTarihi, bool KapakVar,
    string? AltBaslik = null, string? KapakAlt = null, bool AramaDisi = false);

/// <summary>Detay sayfası — BLOB kolonları TAŞIMAZ (kapak ayrı uçtan; liste ile aynı gerekçe).</summary>
public sealed record BlogDetail(
    Guid Id, string Baslik, string Slug, string? Ozet, string Icerik,
    BlogPostDurum Durum, DateTimeOffset? YayinTarihi, bool KapakVar,
    string? AltBaslik = null, string? SeoBaslik = null, string? MetaAciklama = null,
    string? AnahtarKelimeler = null, string? Yazar = null, string? KapakAlt = null,
    bool AramaDisi = false)
{
    /// <summary>Arama başlığı — SEO başlığı verilmemişse görünen başlığa düşer (TEK yerde karar).</summary>
    public string AramaBasligi => string.IsNullOrWhiteSpace(SeoBaslik) ? Baslik : SeoBaslik!;

    /// <summary>Anahtar kelimeler listesi (virgülle ayrılmış metinden; boşlar atılır).</summary>
    public IReadOnlyList<string> Kelimeler =>
        string.IsNullOrWhiteSpace(AnahtarKelimeler)
            ? []
            : [.. AnahtarKelimeler.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
};

/// <summary>Kapak serve — TEK blob-dokunan yol. `UpdatedAtUtc` ETag üretimi için (kapak DEĞİŞEBİLİR →
/// VehiclePhoto'nun `immutable` cache'i burada YANLIŞ olurdu).</summary>
public sealed record BlogCover(byte[] Bytes, byte[]? Thumb, string ContentType, DateTimeOffset UpdatedAtUtc);

/// <summary>
/// PR-6: blog kalıcılığı. TASARIM KURALI — blob kolonları (`KapakBytes`/`KapakThumbBytes`) YALNIZ
/// <see cref="GetPublishedCoverAsync"/>/<see cref="GetCoverAsync"/> SELECT'ine girer; liste ve detay
/// DTO projeksiyonudur. Aksi halde 20 yazılık bir liste sayfası 20 tam boyutlu görseli Postgres'ten
/// belleğe çeker ve hiçbirini kullanmaz (bytea+TOAST kararının faturası) — `VehiclePhotoRepository.
/// ListMetaAsync` ile aynı ilke.
/// </summary>
public interface IBlogRepository
{
    // ---- Staff (tüm durumlar) ----
    Task<IReadOnlyList<BlogListItem>> ListAllAsync(CancellationToken ct = default);
    Task<BlogPost?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> SlugExistsAsync(string slug, Guid? excludeId = null, CancellationToken ct = default);
    Task AddAsync(BlogPost post, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<BlogPost> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>Staff önizlemesi — durum FARK ETMEZ (taslak kapağı da admin'e görünür).</summary>
    Task<BlogCover?> GetCoverAsync(Guid id, CancellationToken ct = default);

    // ---- Public (yalnız Yayinda) ----
    Task<IReadOnlyList<BlogListItem>> ListPublishedAsync(CancellationToken ct = default);
    Task<BlogDetail?> GetPublishedBySlugAsync(string slug, CancellationToken ct = default);
    /// <summary>`Durum == Yayinda` filtresi ZORUNLU: RLS Taslak/Yayinda ayrımını BİLMEZ (ikisi de aynı
    /// tenant'ın satırı), bu app-seviyesi bir durumdur — postId'yi bilen biri taslak kapağını çekememeli.</summary>
    Task<BlogCover?> GetPublishedCoverAsync(Guid id, CancellationToken ct = default);
}
