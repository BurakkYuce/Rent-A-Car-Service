using Microsoft.EntityFrameworkCore;
using RentACar.Application.SiteIcerik;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// PR-16 — içerik sayfası + SSS kalıcılığı. İkisi de tenant-owned: merkezi EF query filter + Postgres
/// RLS zaten kapsıyor, bu yüzden burada ELLE tenant süzgeci YOKTUR (platform tablolarının tersine).
///
/// <para>Liste sorguları <c>Govde</c>/<c>Cevap</c> kolonlarını SEÇMEZ — yönetim listesi yalnız
/// üstveri gösteriyor ve 20 KB'lık metinleri boşuna taşımak istemiyoruz (blob/uzun-metin
/// projeksiyon dersi).</para>
/// </summary>
public sealed class SiteIcerikRepository(IDbContextFactory<AppDbContext> factory) : ISiteContentRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    // ---- Sayfalar ----

    public async Task<IReadOnlyList<SayfaOzet>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.SayfaIcerikler.AsNoTracking()
            .OrderBy(s => s.Sira).ThenBy(s => s.Baslik)
            .Select(s => new SayfaOzet(s.Id, s.Slug, s.Baslik, s.Sira, s.Yayinda))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SayfaOzet>> PublishedAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.SayfaIcerikler.AsNoTracking()
            .Where(s => s.Yayinda)
            .OrderBy(s => s.Sira).ThenBy(s => s.Baslik)
            .Select(s => new SayfaOzet(s.Id, s.Slug, s.Baslik, s.Sira, s.Yayinda))
            .ToListAsync(ct);
    }

    public async Task<SayfaGoster?> FindAsync(string slug, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.SayfaIcerikler.AsNoTracking()
            .Where(s => s.Slug == slug && s.Yayinda)
            .Select(s => new SayfaGoster(s.Slug, s.Baslik, s.Govde, s.MetaAciklama))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<SayfaIcerik?> FetchAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.SayfaIcerikler.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<bool> SlugExistsAsync(string slug, Guid? haricId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.SayfaIcerikler.AsNoTracking()
            .AnyAsync(s => s.Slug == slug && (haricId == null || s.Id != haricId), ct);
    }

    public async Task AddAsync(SayfaIcerik sayfa, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.SayfaIcerikler.Add(sayfa);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> UpdateAsync(Guid id, Action<SayfaIcerik> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var s = await db.SayfaIcerikler.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return false;
        apply(s);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var s = await db.SayfaIcerikler.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return false;
        db.SayfaIcerikler.Remove(s);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ---- SSS ----

    public async Task<IReadOnlyList<SssSatiri>> ListFaqAsync(bool yalnizYayinda, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Cevap BURADA seçiliyor (site `<details>` içinde cevabı da basıyor); metin 4 KB ile sınırlı.
        return await db.SssKayitlari.AsNoTracking()
            .Where(k => !yalnizYayinda || k.Yayinda)
            .OrderBy(k => k.Sira).ThenBy(k => k.Soru)
            .Select(k => new SssSatiri(k.Id, k.Soru, k.Cevap, k.Sira, k.Yayinda))
            .ToListAsync(ct);
    }

    public async Task AddFaqAsync(SssKaydi kayit, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.SssKayitlari.Add(kayit);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> UpdateFaqAsync(Guid id, Action<SssKaydi> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = await db.SssKayitlari.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (k is null) return false;
        apply(k);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteFaqAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = await db.SssKayitlari.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (k is null) return false;
        db.SssKayitlari.Remove(k);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ---- F11.1b: iyimser eşzamanlılık (SatirSurumu) ----

    public Task<bool> UpdateAsync(Guid id, string? expectedVersion, Action<SayfaIcerik> apply, CancellationToken ct = default)
        => SatirSurumu.GuncelleAsync(_factory, SatirSurumu.SayfaIcerikler, id, expectedVersion,
            (db, k, c) => db.SayfaIcerikler.FirstOrDefaultAsync(x => x.Id == k, c), apply, ct);

    public Task<bool> UpdateFaqAsync(Guid id, string? expectedVersion, Action<SssKaydi> apply, CancellationToken ct = default)
        => SatirSurumu.GuncelleAsync(_factory, SatirSurumu.SssKayitlari, id, expectedVersion,
            (db, k, c) => db.SssKayitlari.FirstOrDefaultAsync(x => x.Id == k, c), apply, ct);

    public async Task<string?> VersionAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await SatirSurumu.OkuAsync(db, SatirSurumu.SayfaIcerikler, id, ct);
    }

    public async Task<string?> FaqVersionAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await SatirSurumu.OkuAsync(db, SatirSurumu.SssKayitlari, id, ct);
    }
}
