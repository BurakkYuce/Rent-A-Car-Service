using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.VehicleSales;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Araç satış kalıcılığı. PostAsync: No tahsisi + satış belgesi + DENGELİ defter kümesi +
/// aracı Satildi'ye çevirme → TEK transaction. Çift satış DB-garantili engellenir: tamamlanmış
/// satış için araç başına KISMİ UNIQUE index (yarış güvenli) → unique-violation = idempotent hata.
/// </summary>
public sealed class VehicleSaleRepository(IDbContextFactory<AppDbContext> factory) : IVehicleSaleRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<VehicleSale>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.VehicleSales.AsNoTracking().OrderByDescending(s => s.Tarih).ToListAsync(ct);
    }

    public async Task<VehicleSale?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.VehicleSales.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task PostAsync(VehicleSale sale, IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default)
    {
        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        if (debit != credit)
            throw new ValidationException($"Satış defteri dengesiz: borç {debit} ≠ alacak {credit}.");

        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var n = await SequenceAllocator.NextAsync(db, db.TenantId, "VehicleSaleNo", ct);
        sale.No = $"ST-{n:D6}";
        foreach (var entry in entries)
            entry.Description = $"Araç satış {sale.No}";

        db.VehicleSales.Add(sale);
        db.AccountLedgerEntries.AddRange(entries);

        // Aracı filodan çıkar (Satildi). Araç başka tenant'taysa RLS zaten bulduramaz.
        var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == sale.VehicleId, ct)
            ?? throw new ValidationException("Araç bulunamadı.");
        if (vehicle.Durum == VehicleStatus.Satildi)
            throw new ValidationException("Araç zaten satılmış.");
        vehicle.Durum = VehicleStatus.Satildi;
        vehicle.UpdatedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Yarış: aynı araç için ikinci tamamlanmış satış (kısmi unique index) → idempotent hata.
            await tx.RollbackAsync(ct);
            throw new ValidationException("Araç zaten satılmış.");
        }
    }
}
