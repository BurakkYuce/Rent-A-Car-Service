using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.GelenEFaturalar;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IGelenEFaturaRepository: kısa-ömürlü context'ler (factory). Tenant izolasyonu RLS + query filter ile
/// otomatik. ETTN benzersizliği DB unique index (TenantId,Ettn) ile; ihlal (23505) ValidationException.
/// </summary>
public sealed class GelenEFaturaRepository(IDbContextFactory<AppDbContext> factory) : IIncomingEInvoiceRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<GelenEFatura>> ListAsync(
        GelenEFaturaFilter? filter, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.GelenEFaturalar.AsNoTracking();

        if (filter is not null)
        {
            if (!string.IsNullOrWhiteSpace(filter.Firma))
            {
                var t = filter.Firma.Trim();
                q = q.Where(r => EF.Functions.ILike(r.GonderenUnvan, $"%{t}%")
                              || EF.Functions.ILike(r.GonderenVkn, $"%{t}%"));
            }
            // ETTN ARALIĞI: metin karşılaştırma (fatura no serileri sıralıdır). Uçlar DAHİL.
            if (!string.IsNullOrWhiteSpace(filter.EttnBas))
            {
                var b = filter.EttnBas.Trim();
                q = q.Where(r => string.Compare(r.Ettn, b) >= 0);
            }
            if (!string.IsNullOrWhiteSpace(filter.EttnBit))
            {
                var s = filter.EttnBit.Trim();
                q = q.Where(r => string.Compare(r.Ettn, s) <= 0);
            }
            if (filter.Durum is { } d) q = q.Where(r => r.Durum == d);
            if (filter.Bas is { } bas) q = q.Where(r => r.Tarih >= bas);
            if (filter.Bit is { } bit) q = q.Where(r => r.Tarih <= bit);
            if (filter.Giderlestirildi is { } g)
                q = g ? q.Where(r => r.GiderlestirilmeUtc != null) : q.Where(r => r.GiderlestirilmeUtc == null);
            if (!string.IsNullOrWhiteSpace(filter.Plaka))
            {
                // Plaka GelenEFatura'da YOK → bağlı araçtan alt-sorgu (ExpenseRepository ile aynı desen).
                // Plakalar DB'de normalize saklanır ("34AA01"); kullanıcı "34 AA 01" yazar → terim de normalize.
                var p = filter.Plaka.Trim().ToUpperInvariant().Replace(" ", string.Empty);
                q = q.Where(r => r.VehicleId != null && db.Vehicles
                    .Where(v => EF.Functions.ILike(v.Plaka, $"%{p}%"))
                    .Select(v => (Guid?)v.Id).Contains(r.VehicleId));
            }
        }

        return await q.OrderByDescending(r => r.Tarih).ToListAsync(ct);
    }

    public async Task<GelenEFatura?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.GelenEFaturalar.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<bool> EttnExistsAsync(string ettn, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var e = ettn.Trim();
        return await db.GelenEFaturalar.AsNoTracking().Where(r => r.Ettn == e).AnyAsync(ct);
    }

    public async Task CreateAsync(GelenEFatura row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.GelenEFaturalar.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{row.Ettn}' ETTN'li gelen fatura zaten kayıtlı.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<GelenEFatura> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.GelenEFaturalar.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;

        apply(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<string?> VersionAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await SatirSurumu.OkuAsync(db, SatirSurumu.GelenEFaturalar, id, ct);
    }

    public Task<bool> UpdateLockedAsync(Guid id, string? expectedVersion, Action<GelenEFatura> apply, CancellationToken ct = default)
        => SatirSurumu.GuncelleAsync(_factory, SatirSurumu.GelenEFaturalar, id, expectedVersion,
            (db, key, c) => db.GelenEFaturalar.FirstOrDefaultAsync(r => r.Id == key, c), apply, ct);
}
