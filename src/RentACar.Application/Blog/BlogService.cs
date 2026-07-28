using System.Text;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Blog;

/// <summary>
/// Blog iş mantığı (PR-6). Yazma = içerik yönetimi → <see cref="Permission.OperationsWrite"/>.
/// Public okuma metotları (<see cref="ListPublishedAsync"/>/<see cref="GetPublishedBySlugAsync"/>/
/// <see cref="GetPublishedCoverAsync"/>) GUARD'SIZ — `VehiclePhotoService`'in public serve deseni:
/// halka açık site anonim ziyaretçiyle çağırır, izolasyon RLS'in kendisidir.
///
/// Slug kuralı: başlıktan <see cref="TurkishText.Normalize"/> ile türetilir (Türkçe İ/I/ı dahil doğru
/// transliterasyon — `ToLower()` KÜLTÜRE DUYARLI olduğu için KULLANILMAZ). Yazı bir kez yayınlandıktan
/// (<see cref="BlogPost.YayinTarihi"/> dolduktan) sonra slug DONAR.
/// </summary>
public sealed class BlogService(IBlogRepository repository, ICurrentUser currentUser)
{
    private const long MaxKapakBytes = 2 * 1024 * 1024; // 2 MB — VehiclePhoto ile aynı

    // ---- Staff ----

    public Task<IReadOnlyList<BlogListItem>> ListAllAsync(CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        return repository.ListAllAsync(ct);
    }

    public Task<BlogPost?> GetAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        return repository.FindAsync(id, ct);
    }

    public async Task<Guid> CreateAsync(BlogInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        var (baslik, icerik, ozet) = Normalize(input);
        var slug = await ResolveSlugAsync(input.Slug, baslik, excludeId: null, ct);

        var post = new BlogPost
        {
            Baslik = baslik,
            Slug = slug,
            Ozet = ozet,
            Icerik = icerik,
            Durum = input.Durum,
            YayinTarihi = input.Durum == BlogPostDurum.Yayinda ? DateTimeOffset.UtcNow : null,
        };
        await repository.AddAsync(post, ct);
        return post.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, BlogInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        var (baslik, icerik, ozet) = Normalize(input);

        var mevcut = await repository.FindAsync(id, ct);
        if (mevcut is null) return false;

        // Slug DONMUŞ mu? Bir kez yayınlandıysa (YayinTarihi dolu) değiştirilemez — mevcut linkler/sitemap kırılmasın.
        var slug = mevcut.YayinTarihi is not null
            ? mevcut.Slug
            : await ResolveSlugAsync(input.Slug, baslik, excludeId: id, ct);

        return await repository.UpdateAsync(id, p =>
        {
            p.Baslik = baslik;
            p.Slug = slug;
            p.Ozet = ozet;
            p.Icerik = icerik;
            p.Durum = input.Durum;
            // İLK yayında damgalanır; sonraki düzenlemelerde KORUNUR (yeniden yayınlamak tarihi ileri atmaz).
            if (input.Durum == BlogPostDurum.Yayinda && p.YayinTarihi is null) p.YayinTarihi = DateTimeOffset.UtcNow;
            p.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        return repository.DeleteAsync(id, ct);
    }

    /// <summary>Kapak yükle — `VehiclePhotoService.AddAsync` ile AYNI doğrulama/thumbnail yolu
    /// (paylaşılan ImageValidation/ImageProcessing). `null` bytes → kapağı kaldırır.</summary>
    public async Task<bool> SetKapakAsync(Guid id, byte[]? bytes, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);

        if (bytes is null || bytes.Length == 0)
            return await repository.UpdateAsync(id, p =>
            {
                p.KapakBytes = null; p.KapakThumbBytes = null; p.KapakContentType = null;
                p.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }, ct);

        var kind = ImageValidation.Detect(bytes);
        if (kind == ImageKind.Unknown) throw new ValidationException("PNG, JPEG veya WebP yükleyin.");
        if (bytes.Length > MaxKapakBytes) throw new ValidationException("Kapak görseli en fazla 2 MB olabilir.");

        var thumb = ImageProcessing.TryCreateThumbnail(bytes); // başarısızsa null — yükleme yine tamamlanır
        return await repository.UpdateAsync(id, p =>
        {
            p.KapakBytes = bytes;
            p.KapakThumbBytes = thumb;
            p.KapakContentType = ImageValidation.ContentType(kind);
            p.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    /// <summary>Staff önizlemesi — taslak kapağı da döner (public uç AYRI, Durum filtreli).</summary>
    public Task<BlogCover?> GetCoverAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        return repository.GetCoverAsync(id, ct);
    }

    // ---- Public (GUARD'SIZ — halka açık site anonim çağırır; izolasyon RLS) ----

    public Task<IReadOnlyList<BlogListItem>> ListPublishedAsync(CancellationToken ct = default)
        => repository.ListPublishedAsync(ct);

    public Task<BlogDetail?> GetPublishedBySlugAsync(string slug, CancellationToken ct = default)
        => repository.GetPublishedBySlugAsync(slug, ct);

    public Task<BlogCover?> GetPublishedCoverAsync(Guid id, CancellationToken ct = default)
        => repository.GetPublishedCoverAsync(id, ct);

    // ---- Yardımcılar ----

    private static (string Baslik, string Icerik, string? Ozet) Normalize(BlogInput input)
    {
        var baslik = (input.Baslik ?? string.Empty).Trim();
        var icerik = (input.Icerik ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(baslik)) throw new ValidationException("Başlık zorunludur.");
        if (string.IsNullOrWhiteSpace(icerik)) throw new ValidationException("İçerik zorunludur.");
        var ozet = string.IsNullOrWhiteSpace(input.Ozet) ? null : input.Ozet.Trim();
        return (baslik, icerik, ozet);
    }

    private async Task<string> ResolveSlugAsync(string? istenen, string baslik, Guid? excludeId, CancellationToken ct)
    {
        var slug = Slugify(string.IsNullOrWhiteSpace(istenen) ? baslik : istenen);
        if (slug.Length == 0) throw new ValidationException("Başlıktan geçerli bir adres üretilemedi — adresi elle girin.");
        if (await repository.SlugExistsAsync(slug, excludeId, ct))
            throw new ValidationException($"'{slug}' adresli bir yazı zaten var.");
        return slug;
    }

    /// <summary>Türkçe-doğru slug: `TurkishText.Normalize` (İ/I/ı dahil) → alfanümerik dışı her şey tire →
    /// ardışık/kenar tireler sadeleşir.</summary>
    internal static string Slugify(string s)
    {
        var normalized = TurkishText.Normalize(s);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (char.IsAsciiLetterOrDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }
}
