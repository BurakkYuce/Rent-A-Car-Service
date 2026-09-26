using Microsoft.EntityFrameworkCore;
using RentACar.Application.WebSite;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IWebIlanRepository (PR-13). Kısa-ömürlü context'ler (factory); tenant izolasyonu query filter +
/// RLS ile otomatik. Yazma yolları TEK SaveChanges kullanır — sihirbaz yarıda kalırsa filo
/// yarı-ilanlı kalmamalı.
/// </summary>
public sealed class WebListingRepository(IDbContextFactory<AppDbContext> factory) : IWebListingRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    /// <summary>İlan + üye araç + özellik: ÜÇ sorgu (ilan sayısı kadar DEĞİL). Bellekte birleştirilir.</summary>
    public async Task<IReadOnlyList<WebIlanDetay>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var listings = await db.WebIlanlar.AsNoTracking()
            .OrderBy(i => i.Sira).ThenBy(i => i.Baslik) // tie-break ŞART: eşit Sira'da Postgres sıra garanti etmez
            .ToListAsync(ct);
        if (listings.Count == 0) return [];

        var ids = listings.Select(i => i.Id).ToList();
        var vehicles = await db.Vehicles.AsNoTracking()
            .Where(v => v.WebIlanId != null && ids.Contains(v.WebIlanId.Value))
            .ToListAsync(ct);
        var features = await db.WebIlanOzellikler.AsNoTracking()
            .Where(o => ids.Contains(o.IlanId))
            .OrderBy(o => o.Sira).ToListAsync(ct);

        return [.. listings.Select(i => new WebIlanDetay(i,
            vehicles.Where(v => v.WebIlanId == i.Id).OrderBy(v => v.Plaka).ToList(),
            features.Where(o => o.IlanId == i.Id).ToList()))];
    }

    public async Task<WebIlanDetay?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var listing = await db.WebIlanlar.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
        if (listing is null) return null;
        var vehicles = await db.Vehicles.AsNoTracking().Where(v => v.WebIlanId == id)
            .OrderBy(v => v.Plaka).ToListAsync(ct);
        var features = await db.WebIlanOzellikler.AsNoTracking().Where(o => o.IlanId == id)
            .OrderBy(o => o.Sira).ToListAsync(ct);
        return new WebIlanDetay(listing, vehicles, features);
    }

    public async Task<WebIlanDetay?> FindBySlugAsync(string slug, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var id = await db.WebIlanlar.AsNoTracking().Where(i => i.Slug == slug)
            .Select(i => (Guid?)i.Id).FirstOrDefaultAsync(ct);
        return id is { } g ? await FindAsync(g, ct) : null;
    }

    public async Task<IReadOnlyList<string>> ListSlugsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.WebIlanlar.AsNoTracking().Select(i => i.Slug).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<WebIlan>> FindByKeyAsync(string key, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.WebIlanlar.AsNoTracking()
            .Where(i => i.EslesmeAnahtari == key)
            .OrderBy(i => i.CreatedAtUtc).ToListAsync(ct);
    }

    public async Task CreateWithMembershipAsync(
        IReadOnlyList<(WebIlan Ilan, bool Yeni, IReadOnlyList<Guid> AracIdler)> groups, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var allIds = groups.SelectMany(g => g.AracIdler).Distinct().ToList();
        // Araçlar TRACKING ile yüklenir: bulk update audit interceptor'ını atlardı.
        var vehicles = await db.Vehicles.Where(v => allIds.Contains(v.Id)).ToListAsync(ct);

        foreach (var (listing, newItem, vehicleIds) in groups)
        {
            if (newItem) db.WebIlanlar.Add(listing); // katılımda INSERT YOK (aksi halde PK ihlali)
            foreach (var v in vehicles.Where(v => vehicleIds.Contains(v.Id)))
            {
                v.WebIlanId = listing.Id;
                v.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
        }
        await db.SaveChangesAsync(ct); // TEK transaction — yarım kalan üyelik yok
    }

    public async Task<bool> UpdateAsync(Guid id, Action<WebIlan> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var listing = await db.WebIlanlar.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (listing is null) return false;
        apply(listing);
        listing.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>F11.1b — satır kilidi + iyimser sürüm karşılaştırması (<see cref="RowVersionSql"/>).</summary>
    public Task<bool> UpdateAsync(Guid id, string? expectedVersion, Action<WebIlan> apply, CancellationToken ct = default)
        => RowVersionSql.UpdateAsync(_factory, RowVersionSql.WebListings, id, expectedVersion,
            (db, k, c) => db.WebIlanlar.FirstOrDefaultAsync(i => i.Id == k, c),
            i => { apply(i); i.UpdatedAtUtc = DateTimeOffset.UtcNow; }, ct);

    public async Task<string?> VersionAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await RowVersionSql.ReadAsync(db, RowVersionSql.WebListings, id, ct);
    }

    /// <summary>Kardeş = AYNI eşleşme anahtarına sahip, HÂLÂ TASLAK olan diğer ilanlar. Yayındaki bir
    /// ilanın fiyatı buradan DEĞİŞTİRİLMEZ — "ayrı" modda 12 taslak yaratılır, fiyat hepsine iner;
    /// sonradan tek tek düzenlenenler yayında olduğu için korunur.</summary>
    public async Task<int> CopyPriceToSiblingsAsync(Guid sourceListingId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var source = await db.WebIlanlar.AsNoTracking().FirstOrDefaultAsync(i => i.Id == sourceListingId, ct);
        if (source?.EslesmeAnahtari is not { } key) return 0;

        var siblings = await db.WebIlanlar
            .Where(i => i.Id != sourceListingId && i.EslesmeAnahtari == key && i.Durum == WebIlanDurum.Taslak)
            .ToListAsync(ct);
        foreach (var k in siblings)
        {
            k.GunlukFiyat = source.GunlukFiyat;
            k.HaftalikToplam = source.HaftalikToplam;
            k.AylikToplam = source.AylikToplam;
            k.KdvDahil = source.KdvDahil;
            k.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
        if (siblings.Count > 0) await db.SaveChangesAsync(ct);
        return siblings.Count;
    }

    public async Task ReplaceFeaturesAsync(
        Guid listingId, IReadOnlyList<WebIlanOzellik> rows, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = await db.WebIlanOzellikler.Where(o => o.IlanId == listingId).ToListAsync(ct);
        db.WebIlanOzellikler.RemoveRange(existing);
        foreach (var s in rows) { s.IlanId = listingId; db.WebIlanOzellikler.Add(s); }
        await db.SaveChangesAsync(ct); // sil+yaz TEK transaction (arada özelliksiz ilan görünmez)
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var listing = await db.WebIlanlar.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (listing is null) return false;
        // Üyelikleri ÖNCE çöz: FK SetNull zaten yapar ama audit/UpdatedAt yazılsın diye açıkça.
        var vehicles = await db.Vehicles.Where(v => v.WebIlanId == id).ToListAsync(ct);
        foreach (var v in vehicles) { v.WebIlanId = null; v.UpdatedAtUtc = DateTimeOffset.UtcNow; }
        db.WebIlanlar.Remove(listing);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<Vehicle>> ListVehiclesWithoutListingAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Vehicles.AsNoTracking().Where(v => v.WebIlanId == null)
            .OrderBy(v => v.Marka).ThenBy(v => v.Tip).ThenBy(v => v.Plaka).ToListAsync(ct);
    }
}
