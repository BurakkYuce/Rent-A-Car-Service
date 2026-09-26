using Microsoft.EntityFrameworkCore;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

using RentACar.Domain.Common;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Teklif kalıcılığı. No tahsisi + insert AYNI transaction. Kabul (ConvertToReservation):
/// ReservationNo tahsis + Reservation insert + teklif Durum/ReservationId güncelle, atomik.
/// Tenant izolasyonu RLS + query filter ile otomatik.
/// </summary>
public sealed class QuotationRepository(IDbContextFactory<AppDbContext> factory) : IQuotationRepository
{
    private const string AlreadyAcceptedMessage = ConcurrentModificationException.QuotationAcceptMessage;

    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<Quotation>> ListAsync(RentACar.Application.Authorization.BranchScope.BranchFilter scope = default, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.Quotations.AsNoTracking();
        // C4 ŞABLON (InScope ile birebir): türetilmiş-FK-eşit VEYA ofis-metni-eşit (Ordinal).
        if (!scope.Unrestricted)
        {
            var kid = scope.SubeId; var kad = scope.SubeAd;
            q = q.Where(x => (kid != null && x.CikisSubeId == kid)
                          || ((kid == null || x.CikisSubeId == null) && kad != null && x.CikisOfisi != null && x.CikisOfisi.Trim() == kad)); // C5
        }
        return await q.OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
    }

    public async Task<Quotation?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Quotations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task CreateAsync(Quotation quotation, CancellationToken ct = default)
    {
        await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            quotation.No = await DocumentNoGenerator.GenerateAsync(db, db.TenantId, DocumentNoType.Teklif, ct);
            db.Quotations.Add(quotation);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }, ct);
    }

    /// <summary>F5.1 adversarial L6 — satır kilidi (<c>FOR UPDATE</c>) ALTINDA oku-uygula-yaz: <paramref name="apply"/>
    /// içindeki durum denetimi eşzamanlı kabul/red ile yarışamaz (kabul edilmiş teklif "Reddedildi"ye ezilmez).</summary>
    public Task<bool> UpdateAsync(Guid id, Action<Quotation> apply, CancellationToken ct = default)
        => RowVersionSql.UpdateAsync(_factory, RowVersionSql.Quotations, id, expectedVersion: null,
            (db, k, c) => db.Quotations.FirstOrDefaultAsync(x => x.Id == k, c), apply, ct);

    public async Task<Guid> ConvertToReservationAsync(
        Guid quotationId, Func<Quotation, Reservation> buildReservation, CancellationToken ct = default)
    {
        return await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // F5.1 adversarial H1: teklif satırı OKUMADAN ÖNCE kilitlenir; durum + ReservationId kilit ALTINDA yeniden
            // denetlenir. Kilitsiz okumada 8 eşzamanlı kabulün hepsi "ReservationId null" görüp 8 rezervasyon açıyordu.
            await RowVersionSql.LockAsync(db, RowVersionSql.Quotations, quotationId, ct);
            var quotation = await db.Quotations.FirstOrDefaultAsync(x => x.Id == quotationId, ct)
                ?? throw new ValidationException("Teklif bulunamadı.");
            if (quotation.ReservationId is not null)
                throw new ConcurrentModificationException(AlreadyAcceptedMessage);
            if (quotation.Durum is not (QuotationStatus.Taslak or QuotationStatus.Gonderildi))
                throw new ConcurrentModificationException(
                    $"Teklif bu sırada başka bir oturumda '{quotation.Durum}' durumuna geçti; kabul edilmedi.");

            var reservation = buildReservation(quotation);
            reservation.KaynakTeklifId = quotation.Id; // yapısal çit: (TenantId, KaynakTeklifId) kısmi UNIQUE
            reservation.ReservationNo = await DocumentNoGenerator.GenerateAsync(db, db.TenantId, DocumentNoType.Rezervasyon, ct);
            db.Reservations.Add(reservation);

            quotation.Durum = QuotationStatus.Kabul;
            quotation.ReservationId = reservation.Id;
            quotation.UpdatedAtUtc = DateTimeOffset.UtcNow;

            try
            {
                await db.SaveChangesAsync(ct); // reservation insert + quotation update + audit, atomik
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException
            {
                SqlState: Npgsql.PostgresErrorCodes.UniqueViolation,
                ConstraintName: Configurations.ReservationConfig.QuotationSingleReservationIndex,
            })
            {
                throw new ConcurrentModificationException(AlreadyAcceptedMessage);
            }
            return reservation.Id;
        }, ct);
    }
}
