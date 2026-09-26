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
    private const long MaxCoverBytes = 2 * 1024 * 1024; // 2 MB — VehiclePhoto ile aynı

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

    /// <summary>Arama sonucu açıklamasının pratik üst sınırı (Google ~155-160 karakterde kırpar).</summary>
    public const int MaxMetaDescription = 160;
    /// <summary>Arama başlığının pratik üst sınırı (~60 karakterden sonrası kırpılır).</summary>
    public const int MaxSeoTitle = 70;

    /// <summary>
    /// SEO alanlarını normalize eder ve UZUNLUK sınırlarını uygular.
    ///
    /// <para><b>Neden REDDETMİYOR da kırpmıyor:</b> ikisi de yapılmıyor — sınırı aşan değer olduğu
    /// gibi SAKLANIYOR, yalnız ekranda uyarı gösteriliyor. Sebep: bunlar tavsiye sınırlarıdır
    /// (arama motoru kırpar, içerik kaybolmaz) ve yazarın metnini sessizce kesmek ya da kaydını
    /// reddetmek gerçek bir hatayı değil bir stil tercihini dayatmak olurdu.</para>
    /// </summary>
    private static void ApplySeo(BlogPost p, BlogInput input)
    {
        static string? T(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

        p.AltBaslik = T(input.AltBaslik);
        p.SeoBaslik = T(input.SeoBaslik);
        p.MetaAciklama = T(input.MetaAciklama);
        p.Yazar = T(input.Yazar);
        p.KapakAlt = T(input.KapakAlt);
        p.AramaDisi = input.AramaDisi;

        // Anahtar kelimeler: virgülle ayrılır, uçlar kırpılır, BOŞLAR ve TEKRARLAR atılır.
        // Tekrar ayıklaması Türkçe-duyarlı: "Antalya" ile "antalya" AYNI kelimedir.
        p.AnahtarKelimeler = T(input.AnahtarKelimeler) is { } raw
            ? string.Join(", ", raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .DistinctBy(TurkishText.Normalize))
            : null;
    }

    public async Task<Guid> CreateAsync(BlogInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        var (title, content, summary) = Normalize(input);
        var slug = await ResolveSlugAsync(input.Slug, title, excludeId: null, ct);

        var post = new BlogPost
        {
            Baslik = title,
            Slug = slug,
            Ozet = summary,
            Icerik = content,
            Durum = input.Durum,
            YayinTarihi = input.Durum == BlogPostDurum.Yayinda ? DateTimeOffset.UtcNow : null,
        };
        ApplySeo(post, input);
        await repository.AddAsync(post, ct);
        return post.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, BlogInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        var (title, content, summary) = Normalize(input);

        var existing = await repository.FindAsync(id, ct);
        if (existing is null) return false;

        // Slug DONMUŞ mu? Bir kez yayınlandıysa (YayinTarihi dolu) değiştirilemez — mevcut linkler/sitemap kırılmasın.
        var slug = existing.YayinTarihi is not null
            ? existing.Slug
            : await ResolveSlugAsync(input.Slug, title, excludeId: id, ct);

        return await repository.UpdateAsync(id, p =>
        {
            p.Baslik = title;
            p.Slug = slug;
            p.Ozet = summary;
            p.Icerik = content;
            p.Durum = input.Durum;
            ApplySeo(p, input);
            // İLK yayında damgalanır; sonraki düzenlemelerde KORUNUR (yeniden yayınlamak tarihi ileri atmaz).
            if (input.Durum == BlogPostDurum.Yayinda && p.YayinTarihi is null) p.YayinTarihi = DateTimeOffset.UtcNow;
            p.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    /// <summary>
    /// F11.1b — tam değiştirme, iyimser eşzamanlılıkla: satır kilitlenir, <paramref name="expectedVersion"/> kilit
    /// altında karşılaştırılır (uyuşmazlık <see cref="ConcurrentModificationException"/>). Slug dondurma kuralı
    /// <see cref="UpdateAsync(Guid, BlogInput, CancellationToken)"/> ile aynı; "yayınlandı mı" KİLİTLİ satırdan okunur.
    /// </summary>
    public async Task<bool> UpdateAsync(Guid id, BlogInput input, string expectedVersion, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        var (title, content, summary) = Normalize(input);

        var existing = await repository.FindAsync(id, ct);
        if (existing is null) return false;
        var newSlug = existing.YayinTarihi is not null
            ? existing.Slug
            : await ResolveSlugAsync(input.Slug, title, excludeId: id, ct);

        return await repository.UpdateAsync(id, expectedVersion, p =>
        {
            p.Baslik = title;
            if (p.YayinTarihi is null) p.Slug = newSlug; // yayınlanmış yazının adresi DONAR
            p.Ozet = summary;
            p.Icerik = content;
            p.Durum = input.Durum;
            ApplySeo(p, input);
            if (input.Durum == BlogPostDurum.Yayinda && p.YayinTarihi is null) p.YayinTarihi = DateTimeOffset.UtcNow;
            p.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    /// <summary>F11.1b — satır sürümü (opak; PUT'ta geri gönderilir).</summary>
    public Task<string?> VersionAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        return repository.VersionAsync(id, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        return repository.DeleteAsync(id, ct);
    }

    /// <summary>Kapak yükle — `VehiclePhotoService.AddAsync` ile AYNI doğrulama/thumbnail yolu
    /// (paylaşılan ImageValidation/ImageProcessing). `null` bytes → kapağı kaldırır.</summary>
    public async Task<bool> SetCoverAsync(Guid id, byte[]? bytes, CancellationToken ct = default)
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
        if (bytes.Length > MaxCoverBytes) throw new ValidationException("Kapak görseli en fazla 2 MB olabilir.");

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
        var title = (input.Baslik ?? string.Empty).Trim();
        var content = (input.Icerik ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(title)) throw new ValidationException("Başlık zorunludur.");
        if (string.IsNullOrWhiteSpace(content)) throw new ValidationException("İçerik zorunludur.");
        var summary = string.IsNullOrWhiteSpace(input.Ozet) ? null : input.Ozet.Trim();
        return (title, content, summary);
    }

    private async Task<string> ResolveSlugAsync(string? requested, string title, Guid? excludeId, CancellationToken ct)
    {
        var slug = Slugify(string.IsNullOrWhiteSpace(requested) ? title : requested);
        if (slug.Length == 0) throw new ValidationException("Başlıktan geçerli bir adres üretilemedi — adresi elle girin.");
        if (await repository.SlugExistsAsync(slug, excludeId, ct))
            throw new ValidationException($"'{slug}' adresli bir yazı zaten var.");
        return slug;
    }

    /// <summary>PR-14: gerçek uygulama <see cref="TurkishText.Slugify"/>'a taşındı (ilan adresleri de
    /// aynı kuralı kullanıyor). Bu köprü, mevcut çağrıları ve testleri kırmamak için duruyor.</summary>
    internal static string Slugify(string s) => TurkishText.Slugify(s);
}
