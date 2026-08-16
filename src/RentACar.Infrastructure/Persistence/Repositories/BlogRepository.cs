using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Blog;
using RentACar.Application.Common;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>PR-6: IBlogRepository implementasyonu. Blob kolonları YALNIZ kapak metotlarının SELECT'ine
/// girer (bkz. arayüz doc-yorumu). Slug benzersizliği DB unique index ile; ihlal ValidationException.</summary>
public sealed class BlogRepository(IDbContextFactory<AppDbContext> factory) : IBlogRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    // Projeksiyonlar TEK yerde — blob kolonlarının kazara SELECT'e sızmasını yapısal olarak engeller.
    private static readonly System.Linq.Expressions.Expression<Func<BlogPost, BlogListItem>> ToListItem =
        p => new BlogListItem(p.Id, p.Baslik, p.Slug, p.Ozet, p.Durum, p.YayinTarihi, p.KapakBytes != null,
            p.AltBaslik, p.KapakAlt, p.AramaDisi);

    private static readonly System.Linq.Expressions.Expression<Func<BlogPost, BlogDetail>> ToDetail =
        p => new BlogDetail(p.Id, p.Baslik, p.Slug, p.Ozet, p.Icerik, p.Durum, p.YayinTarihi, p.KapakBytes != null,
            p.AltBaslik, p.SeoBaslik, p.MetaAciklama, p.AnahtarKelimeler, p.Yazar, p.KapakAlt, p.AramaDisi);

    public async Task<IReadOnlyList<BlogListItem>> ListAllAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.BlogYazilari.AsNoTracking()
            .OrderByDescending(p => p.YayinTarihi ?? p.CreatedAtUtc)
            .Select(ToListItem).ToListAsync(ct);
    }

    public async Task<BlogPost?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.BlogYazilari.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<bool> SlugExistsAsync(string slug, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.BlogYazilari.AsNoTracking()
            .AnyAsync(p => p.Slug == slug && (excludeId == null || p.Id != excludeId), ct);
    }

    public async Task AddAsync(BlogPost post, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.BlogYazilari.Add(post);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{post.Slug}' adresli bir yazı zaten var.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<BlogPost> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var post = await db.BlogYazilari.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (post is null) return false;

        apply(post);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{post.Slug}' adresli bir yazı zaten var.");
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var post = await db.BlogYazilari.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (post is null) return false;
        db.BlogYazilari.Remove(post);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<BlogCover?> GetCoverAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await CoverQuery(db.BlogYazilari.Where(p => p.Id == id)).FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<BlogListItem>> ListPublishedAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.BlogYazilari.AsNoTracking()
            .Where(p => p.Durum == BlogPostDurum.Yayinda)
            .OrderByDescending(p => p.YayinTarihi)
            .Select(ToListItem).ToListAsync(ct);
    }

    public async Task<BlogDetail?> GetPublishedBySlugAsync(string slug, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.BlogYazilari.AsNoTracking()
            .Where(p => p.Slug == slug && p.Durum == BlogPostDurum.Yayinda)
            .Select(ToDetail).FirstOrDefaultAsync(ct);
    }

    public async Task<BlogCover?> GetPublishedCoverAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await CoverQuery(db.BlogYazilari.Where(p => p.Id == id && p.Durum == BlogPostDurum.Yayinda))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>TEK blob-dokunan projeksiyon. `UpdatedAtUtc` null ise `CreatedAtUtc` — ETag her zaman
    /// deterministik bir zaman damgası taşır.</summary>
    private static IQueryable<BlogCover> CoverQuery(IQueryable<BlogPost> q) => q
        .AsNoTracking()
        .Where(p => p.KapakBytes != null && p.KapakContentType != null)
        .Select(p => new BlogCover(p.KapakBytes!, p.KapakThumbBytes, p.KapakContentType!, p.UpdatedAtUtc ?? p.CreatedAtUtc));
}
