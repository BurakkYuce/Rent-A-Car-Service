using Microsoft.EntityFrameworkCore;
using RentACar.Application.FiloKiralamalar;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

using RentACar.Domain.Common;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Filo kiralama kalıcılığı (roadmap L1). CreateAsync boşluksuz No (FK-000001) tahsis eder.</summary>
public sealed class FiloKiralamaRepository(IDbContextFactory<AppDbContext> factory) : IFleetRentalRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<FiloKiralama>> ListAsync(
        RentACar.Application.FiloKiralamalar.FiloKiralamaFilter? filter = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.FiloKiralamalar.AsNoTracking();

        if (filter is not null)
        {
            // F5.1 adversarial M3 — C4 ŞABLON (BranchScope.InScope ile birebir), ARACIN şubesi üzerinden.
            if (!filter.Kapsam.Unrestricted)
            {
                var kid = filter.Kapsam.SubeId; var kad = filter.Kapsam.SubeAd;
                q = q.Where(x => db.Vehicles.Any(v => v.Id == x.VehicleId
                    && ((kid != null && v.SubeId == kid)
                        || ((kid == null || v.SubeId == null) && kad != null && v.Sube != null && v.Sube.Trim() == kad))));
            }
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

    public Task<bool> UpdateAsync(Guid id, string? beklenenSurum, Action<FiloKiralama> apply, CancellationToken ct = default)
        => SatirSurumu.GuncelleAsync(_factory, SatirSurumu.FiloKiralamalar, id, beklenenSurum,
            (db, k, c) => db.FiloKiralamalar.FirstOrDefaultAsync(x => x.Id == k, c), apply, ct);

    public async Task<string?> VersionAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await SatirSurumu.OkuAsync(db, SatirSurumu.FiloKiralamalar, id, ct);
    }

    public async Task<(Guid? SubeId, string? Sube)?> VehicleBranchAsync(Guid vehicleId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var v = await db.Vehicles.AsNoTracking().Where(x => x.Id == vehicleId)
            .Select(x => new { x.SubeId, x.Sube }).FirstOrDefaultAsync(ct);
        return v is null ? null : (v.SubeId, v.Sube);
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
            row.No = await BelgeNoUretici.UretAsync(db, db.TenantId, DocumentNoType.FiloKiralama, ct);
            db.FiloKiralamalar.Add(row);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }, ct);
    }

    public async Task<bool> SetStatusAsync(Guid id, FleetRentalStatus durum, CancellationToken ct = default)
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
