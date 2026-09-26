using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.VehicleSales;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

using RentACar.Domain.Common;

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

    /// <summary>
    /// FAZ-18 — filtreli liste. Plaka/Ofis kolonları satış belgesinde YOK (VehicleId var) → Vehicles
    /// alt-sorgusu ile süzülür; tüm satışları belleğe çekip filtrelemek listeyi ölçeklenemez yapardı.
    /// Plaka DB'de boşluksuz-büyük harf saklanır → arama terimi de AYNI kuraldan geçer (tek kural, kopya yok).
    /// </summary>
    public async Task<IReadOnlyList<VehicleSale>> SearchAsync(VehicleSaleFilter filter, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.VehicleSales.AsNoTracking();

        if (filter.AliciCariId is Guid c) q = q.Where(x => x.AliciCariId == c);
        if (filter.Durum is { } d) q = q.Where(x => x.Durum == d);
        if (filter.SatisiVerildi is bool sv) q = q.Where(x => x.SatisiVerildi == sv);
        if (filter.Bas is { } start) q = q.Where(x => x.Tarih >= start);
        if (filter.Bit is { } bit) q = q.Where(x => x.Tarih <= bit);

        if (!string.IsNullOrWhiteSpace(filter.Plaka))
        {
            var p = RentACar.Application.Vehicles.VehicleService.PlateKey(filter.Plaka);
            q = q.Where(x => db.Vehicles.Any(v => v.Id == x.VehicleId && EF.Functions.ILike(v.Plaka, $"%{p}%")));
        }

        if (!string.IsNullOrWhiteSpace(filter.Ofis))
        {
            // Ofis = SATILAN ARACIN şubesi (VehicleSale mali belgeye şube kolonu eklenmedi — bkz. filtre notu).
            var o = filter.Ofis.Trim();
            q = q.Where(x => db.Vehicles.Any(v => v.Id == x.VehicleId && v.Sube != null && v.Sube == o));
        }

        return await q.OrderByDescending(s => s.Tarih).ToListAsync(ct);
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

        await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            sale.No = await DocumentNoGenerator.GenerateAsync(db, db.TenantId, DocumentNoType.AracSatis, ct);
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
        }, ct);
    }
}
