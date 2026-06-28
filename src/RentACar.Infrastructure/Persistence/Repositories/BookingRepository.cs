using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Rezervasyon + kira kalıcılığı. Boşluksuz sıra tahsisi ve insert AYNI transaction'da.
/// Kira insert'inde DB exclusion constraint (23P01) çakışmayı engeller → AvailabilityConflict.
/// </summary>
public sealed class BookingRepository(IDbContextFactory<AppDbContext> factory) : IBookingRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    // ---- Rezervasyon ----

    public async Task<IReadOnlyList<Reservation>> ListReservationsAsync(string? sube = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.Reservations.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(sube)) q = q.Where(r => r.CikisOfisi == sube);
        return await q.OrderByDescending(r => r.CreatedAtUtc).ToListAsync(ct);
    }

    public async Task<Reservation?> FindReservationAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Reservations.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task CreateReservationAsync(Reservation reservation, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var n = await SequenceAllocator.NextAsync(db, db.TenantId, "ReservationNo", ct);
        reservation.ReservationNo = $"RZ-{n:D6}";
        db.Reservations.Add(reservation);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<bool> UpdateReservationAsync(Guid id, Action<Reservation> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var r = await db.Reservations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return false;
        apply(r);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ---- Kira ----

    public async Task<IReadOnlyList<RentalContract>> ListRentalsAsync(string? sube = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.Rentals.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(sube)) q = q.Where(r => r.CikisOfisi == sube);
        return await q.OrderByDescending(r => r.CreatedAtUtc).ToListAsync(ct);
    }

    public async Task<RentalContract?> FindRentalAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Rentals.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<IReadOnlyList<RentalRow>> SearchRentalRowsAsync(RentalFilter filter, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var q = db.Rentals.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filter.Sube)) q = q.Where(r => r.CikisOfisi == filter.Sube);
        if (filter.Durum is { } d) q = q.Where(r => r.Durum == d);
        if (filter.BaslangicMin is { } min) q = q.Where(r => r.BasTar >= min);
        if (filter.BaslangicMax is { } max) q = q.Where(r => r.BasTar <= max);
        if (!string.IsNullOrWhiteSpace(filter.Ofis))
            q = q.Where(r => r.CikisOfisi == filter.Ofis || r.DonusOfisi == filter.Ofis);

        var rentals = await q.OrderByDescending(r => r.CreatedAtUtc).ToListAsync(ct);

        var custIds = rentals.Select(r => r.MusteriId).Distinct().ToList();
        var vehIds = rentals.Select(r => r.VehicleId).Distinct().ToList();
        var rentalIds = rentals.Select(r => r.Id).ToList();

        var custNames = (await db.Customers.AsNoTracking().Where(c => custIds.Contains(c.Id)).ToListAsync(ct))
            .ToDictionary(c => c.Id, c => c.DisplayName);
        var plakalar = (await db.Vehicles.AsNoTracking().Where(v => vehIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Plaka }).ToListAsync(ct))
            .ToDictionary(v => v.Id, v => v.Plaka);
        var invoicedSet = (await db.Invoices.AsNoTracking()
            .Where(i => i.RentalId != null && rentalIds.Contains(i.RentalId!.Value))
            .Select(i => i.RentalId!.Value).ToListAsync(ct)).ToHashSet();

        var rows = rentals.Select(r => new RentalRow
        {
            Id = r.Id,
            SozlesmeNo = r.SozlesmeNo,
            MusteriAd = custNames.GetValueOrDefault(r.MusteriId, "—"),
            Plaka = plakalar.GetValueOrDefault(r.VehicleId, "—"),
            BasTar = r.BasTar,
            BitTar = r.BitTar,
            Gun = r.Gun,
            Tutar = r.Tutar,
            Bakiye = r.Bakiye,
            Durum = r.Durum,
            Faturali = invoicedSet.Contains(r.Id)
        }).AsEnumerable();

        if (filter.Faturali is { } fat) rows = rows.Where(r => r.Faturali == fat);
        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var t = filter.Query.Trim();
            rows = rows.Where(r =>
                r.SozlesmeNo.Contains(t, StringComparison.OrdinalIgnoreCase)
                || r.MusteriAd.Contains(t, StringComparison.OrdinalIgnoreCase)
                || r.Plaka.Contains(t, StringComparison.OrdinalIgnoreCase));
        }
        return rows.ToList();
    }

    public async Task CreateRentalAsync(RentalContract contract, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var n = await SequenceAllocator.NextAsync(db, db.TenantId, "RentalNo", ct);
        contract.SozlesmeNo = $"KS-{n:D6}";
        db.Rentals.Add(contract);
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (IsExclusionViolation(ex))
        {
            await tx.RollbackAsync(ct);
            throw new AvailabilityConflictException();
        }
    }

    public async Task<bool> UpdateRentalAsync(Guid id, Action<RentalContract> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var r = await db.Rentals.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return false;
        apply(r);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> HasOverlappingActiveRentalAsync(
        Guid vehicleId, DateTimeOffset basTar, DateTimeOffset bitTar,
        Guid? excludeRentalId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Rentals.AsNoTracking()
            .Where(r => r.VehicleId == vehicleId
                && r.Durum == RentalStatus.Kirada
                && (excludeRentalId == null || r.Id != excludeRentalId)
                && r.BasTar < bitTar && basTar < r.BitTar) // [bas,bit) ∩ [r.Bas,r.Bit)
            .AnyAsync(ct);
    }

    public async Task<Guid> ConvertToRentalAsync(
        Guid reservationId, Func<Reservation, RentalContract> buildRental, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var reservation = await db.Reservations.FirstOrDefaultAsync(r => r.Id == reservationId, ct)
            ?? throw new ValidationException("Rezervasyon bulunamadı.");

        var rental = buildRental(reservation);
        var n = await SequenceAllocator.NextAsync(db, db.TenantId, "RentalNo", ct);
        rental.SozlesmeNo = $"KS-{n:D6}";
        db.Rentals.Add(rental);

        reservation.Durum = ReservationStatus.KirayaCevrildi;
        reservation.RentalContractId = rental.Id;
        reservation.UpdatedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            await db.SaveChangesAsync(ct); // rental insert + reservation update + audit, atomik
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (IsExclusionViolation(ex))
        {
            await tx.RollbackAsync(ct);
            throw new AvailabilityConflictException();
        }

        return rental.Id;
    }

    private static bool IsExclusionViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation };
}
