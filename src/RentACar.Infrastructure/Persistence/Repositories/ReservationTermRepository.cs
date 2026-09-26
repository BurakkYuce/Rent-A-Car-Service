using Microsoft.EntityFrameworkCore;
using RentACar.Application.RezSartlar;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IRezSartRepository: kısa-ömürlü context'ler (factory). Tenant izolasyonu RLS + merkezi query
/// filter ile otomatik. Benzersizlik kısıtı YOK — aynı müşteri aynı şartı iki kez isteyebilir
/// (serbest metin not defteri).
/// </summary>
public sealed class ReservationTermRepository(IDbContextFactory<AppDbContext> factory) : IReservationTermRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<RezSart>> ListAsync(RezSartFilter? filter = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.RezSartlar.AsNoTracking().AsQueryable();

        if (filter is not null)
        {
            if (filter.MusteriId is { } m) q = q.Where(r => r.MusteriId == m);
            if (filter.TarihBas is { } b) q = q.Where(r => r.TalepTarihi >= b);
            if (filter.TarihBit is { } t) q = q.Where(r => r.TalepTarihi <= t);
            // Durum tek kaynaktan türetilir: KarsilamaTarihi null ⇔ karşılanmadı.
            if (filter.Karsilandi is true) q = q.Where(r => r.KarsilamaTarihi != null);
            else if (filter.Karsilandi is false) q = q.Where(r => r.KarsilamaTarihi == null);
        }

        return await q.OrderByDescending(r => r.TalepTarihi).ThenBy(r => r.Sart).ToListAsync(ct);
    }

    public async Task<RezSart?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.RezSartlar.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task CreateAsync(RezSart row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.RezSartlar.Add(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> UpdateAsync(Guid id, Action<RezSart> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.RezSartlar.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;
        apply(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public Task<bool> UpdateAsync(Guid id, string? expectedVersion, Action<RezSart> apply, CancellationToken ct = default)
        => RowVersionSql.UpdateAsync(_factory, RowVersionSql.ReservationTerms, id, expectedVersion,
            (db, k, c) => db.RezSartlar.FirstOrDefaultAsync(r => r.Id == k, c), apply, ct);

    public async Task<string?> VersionAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await RowVersionSql.ReadAsync(db, RowVersionSql.ReservationTerms, id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.RezSartlar.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;
        db.RezSartlar.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
