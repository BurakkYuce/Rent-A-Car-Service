using Microsoft.EntityFrameworkCore;
using RentACar.Application.MusteriTaksitleri;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Müşteri taksiti kalıcılığı (FAZ-66).</summary>
public sealed class CustomerInstallmentRepository(IDbContextFactory<AppDbContext> factory) : ICustomerInstallmentRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<MusteriTaksit>> SearchAsync(
        MusteriTaksitFilter filter, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.MusteriTaksitleri.AsNoTracking();

        if (filter.CariId is Guid c) q = q.Where(x => x.CariId == c);
        if (filter.VehicleId is Guid v) q = q.Where(x => x.VehicleId == v);
        if (filter.Durum is { } d) q = q.Where(x => x.Durum == d);
        if (filter.VadeMin is { } min) q = q.Where(x => x.Vade >= min);
        if (filter.VadeMax is { } max) q = q.Where(x => x.Vade <= max);

        var rows = await q.OrderBy(x => x.Vade).ThenBy(x => x.Sira).ToListAsync(ct);

        // "Gecikti" TÜRETİLMİŞ (kolon değil) → bellekte süzülür. SQL'e taşımak için kolona
        // yazsaydık gece yarısı bayatlardı (bkz. TaksitDurum).
        if (filter.SadeceGecikmis is true) rows = rows.Where(x => x.Gecikti).ToList();
        return rows;
    }

    public async Task<MusteriTaksit?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.MusteriTaksitleri.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task CreateAsync(MusteriTaksit row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.MusteriTaksitleri.Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (PkViolation.Is(ex)) // F6.1b: Id = işlem anahtarı → çift gönderim
        { throw new RentACar.Application.Common.DuplicateOperationException(PkViolation.Message); }
    }

    public async Task CreateManyAsync(IReadOnlyList<MusteriTaksit> rows, CancellationToken ct = default)
    {
        if (rows.Count == 0) return;
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.MusteriTaksitleri.AddRange(rows);
        // TEK transaction: yarım plan (ör. 12 taksitten 7'si) kalırsa toplam borç yanlış görünür.
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (PkViolation.Is(ex)) // F6.1b: aynı anahtarla ikinci plan → tümü geri
        {
            await tx.RollbackAsync(ct);
            throw new RentACar.Application.Common.DuplicateOperationException(PkViolation.Message);
        }
        await tx.CommitAsync(ct);
    }

    public async Task<bool> UpdateAsync(Guid id, Action<MusteriTaksit> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.MusteriTaksitleri.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return false;
        apply(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.MusteriTaksitleri.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return false;
        db.MusteriTaksitleri.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public Task<bool> UpdateLockedAsync(Guid id, string? expectedVersion, Action<MusteriTaksit> apply,
        CancellationToken ct = default)
        => RowVersionSql.UpdateAsync(_factory, RowVersionSql.CustomerInstallments, id, expectedVersion,
            (db, k, c) => db.MusteriTaksitleri.FirstOrDefaultAsync(x => x.Id == k, c), apply, ct);

    public async Task<string?> VersionAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await RowVersionSql.ReadAsync(db, RowVersionSql.CustomerInstallments, id, ct);
    }

    public async Task<int> LastOrderAsync(Guid customerId, Guid? vehicleId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.MusteriTaksitleri.AsNoTracking().Where(x => x.CariId == customerId);
        q = vehicleId is Guid v ? q.Where(x => x.VehicleId == v) : q.Where(x => x.VehicleId == null);
        return await q.Select(x => (int?)x.Sira).MaxAsync(ct) ?? 0;
    }
}
