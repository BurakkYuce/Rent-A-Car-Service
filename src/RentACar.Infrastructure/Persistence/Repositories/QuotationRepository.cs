using Microsoft.EntityFrameworkCore;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Teklif kalıcılığı. No tahsisi + insert AYNI transaction. Kabul (ConvertToReservation):
/// ReservationNo tahsis + Reservation insert + teklif Durum/ReservationId güncelle, atomik.
/// Tenant izolasyonu RLS + query filter ile otomatik.
/// </summary>
public sealed class QuotationRepository(IDbContextFactory<AppDbContext> factory) : IQuotationRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<Quotation>> ListAsync(string? sube = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.Quotations.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(sube)) q = q.Where(x => x.CikisOfisi == sube);
        return await q.OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
    }

    public async Task<Quotation?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Quotations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task CreateAsync(Quotation quotation, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var n = await SequenceAllocator.NextAsync(db, db.TenantId, "QuotationNo", ct);
        quotation.No = $"TK-{n:D6}";
        db.Quotations.Add(quotation);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<bool> UpdateAsync(Guid id, Action<Quotation> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = await db.Quotations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (q is null) return false;
        apply(q);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<Guid> ConvertToReservationAsync(
        Guid quotationId, Func<Quotation, Reservation> buildReservation, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var quotation = await db.Quotations.FirstOrDefaultAsync(x => x.Id == quotationId, ct)
            ?? throw new ValidationException("Teklif bulunamadı.");
        if (quotation.ReservationId is not null)
            throw new ValidationException("Teklif zaten rezervasyona çevrilmiş.");

        var reservation = buildReservation(quotation);
        var n = await SequenceAllocator.NextAsync(db, db.TenantId, "ReservationNo", ct);
        reservation.ReservationNo = $"RZ-{n:D6}";
        db.Reservations.Add(reservation);

        quotation.Durum = QuotationStatus.Kabul;
        quotation.ReservationId = reservation.Id;
        quotation.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct); // reservation insert + quotation update + audit, atomik
        await tx.CommitAsync(ct);
        return reservation.Id;
    }
}
