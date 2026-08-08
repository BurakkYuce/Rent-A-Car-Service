using Microsoft.EntityFrameworkCore;
using RentACar.Application.FiloKiralamalar;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Filo kiralama kalıcılığı (roadmap L1). CreateAsync boşluksuz No (FK-000001) tahsis eder.</summary>
public sealed class FiloKiralamaRepository(IDbContextFactory<AppDbContext> factory) : IFiloKiralamaRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<FiloKiralama>> ListAsync(
        RentACar.Application.FiloKiralamalar.FiloKiralamaFilter? filter = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.FiloKiralamalar.AsNoTracking();

        if (filter is not null)
        {
            if (filter.MusteriId is { } m) q = q.Where(x => x.MusteriId == m);
            if (filter.Durum is { } d) q = q.Where(x => x.Durum == d);
            if (filter.Bas is { } b) q = q.Where(x => x.BasTar >= b);
            if (filter.Bit is { } t) q = q.Where(x => x.BasTar <= t);
            if (!string.IsNullOrWhiteSpace(filter.Ara))
            {
                var a = filter.Ara.Trim();
                q = q.Where(x => EF.Functions.ILike(x.No, $"%{a}%")
                              || (x.SozlesmeNo != null && EF.Functions.ILike(x.SozlesmeNo, $"%{a}%"))
                              || (x.MakbuzNo != null && EF.Functions.ILike(x.MakbuzNo, $"%{a}%"))
                              || (x.DosyaNo != null && EF.Functions.ILike(x.DosyaNo, $"%{a}%"))
                              || (x.Aciklama != null && EF.Functions.ILike(x.Aciklama, $"%{a}%")));
            }
            if (!string.IsNullOrWhiteSpace(filter.Plaka))
            {
                // Plakalar DB'de normalize saklanır ("34AA01"); kullanıcı "34 AA 01" yazar →
                // arama terimi AYNI normalizasyondan geçmezse hiçbir şey bulunmaz (FAZ-63 dersi).
                var p = filter.Plaka.Trim().ToUpperInvariant().Replace(" ", string.Empty);
                q = q.Where(x => db.Vehicles.Where(v => EF.Functions.ILike(v.Plaka, $"%{p}%"))
                    .Select(v => v.Id).Contains(x.VehicleId));
            }
        }

        return await q.OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
    }

    public async Task<bool> UpdateAsync(Guid id, Action<FiloKiralama> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.FiloKiralamalar.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return false;
        apply(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<FiloKiralama?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.FiloKiralamalar.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task CreateAsync(FiloKiralama row, CancellationToken ct = default)
    {
        await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct); // No tahsisi atomik (boşluksuz)
            var n = await SequenceAllocator.NextAsync(db, db.TenantId, "FiloKiralamaNo", ct);
            row.No = $"FK-{n:D6}";
            db.FiloKiralamalar.Add(row);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }, ct);
    }

    public async Task<bool> SetDurumAsync(Guid id, FiloKiraDurum durum, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.FiloKiralamalar.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return false;
        row.Durum = durum;
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
