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
