using Microsoft.EntityFrameworkCore;
using RentACar.Application.FaturaDonemleri;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>IFaturaDonemRepository implementasyonu (FAZ 4.2-B1).</summary>
public sealed class FaturaDonemRepository(IDbContextFactory<AppDbContext> factory) : IFaturaDonemRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<FaturaDonemi>> ListForRentalAsync(Guid rentalId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.FaturaDonemleri.AsNoTracking()
            .Where(d => d.RentalId == rentalId)
            .OrderBy(d => d.DonemSira).ToListAsync(ct);
    }

    public async Task<bool> AtlandiIsaretleAsync(Guid donemId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var d = await db.FaturaDonemleri.FirstOrDefaultAsync(x => x.Id == donemId, ct);
        if (d is null || d.Durum != FaturaDonemDurum.Planlandi) return false;
        d.Durum = FaturaDonemDurum.Atlandi;
        d.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task ReplacePlannedAsync(
        Guid rentalId, IReadOnlyList<FaturaDonemi> yeniPlanlar, CancellationToken ct = default)
    {
        await PgRetry.RunAsync(async () => // deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var eskiler = await db.FaturaDonemleri
                .Where(d => d.RentalId == rentalId && d.Durum == FaturaDonemDurum.Planlandi)
                .ToListAsync(ct);
            db.FaturaDonemleri.RemoveRange(eskiler);
            db.FaturaDonemleri.AddRange(yeniPlanlar);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }, ct);
    }
}
