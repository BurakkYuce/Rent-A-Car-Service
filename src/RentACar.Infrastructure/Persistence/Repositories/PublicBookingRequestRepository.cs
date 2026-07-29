using Microsoft.EntityFrameworkCore;
using RentACar.Application.PublicSite;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>PR-8: IPublicBookingRequestRepository implementasyonu.</summary>
public sealed class PublicBookingRequestRepository(IDbContextFactory<AppDbContext> factory) : IPublicBookingRequestRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task AddAsync(PublicBookingRequest talep, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.SiteTalepleri.Add(talep);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PublicBookingRequest>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.SiteTalepleri.AsNoTracking()
            .OrderBy(t => t.Durum)                 // Yeni(0) önce — çalışma kuyruğu
            .ThenByDescending(t => t.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<PublicBookingRequest?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.SiteTalepleri.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task<bool> TryClaimAsync(Guid id, PublicBookingRequestDurum hedef, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // TEK atomik UPDATE — `WHERE Durum=Yeni` koşulu yarışta tek kazanan bırakır (oku-kontrol-yaz DEĞİL).
        var etkilenen = await db.SiteTalepleri
            .Where(t => t.Id == id && t.Durum == PublicBookingRequestDurum.Yeni)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Durum, hedef)
                .SetProperty(t => t.UpdatedAtUtc, DateTimeOffset.UtcNow), ct);
        return etkilenen == 1;
    }

    public async Task SetDonusenReservationAsync(Guid id, Guid reservationId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await db.SiteTalepleri.Where(t => t.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.DonusenReservationId, reservationId)
                .SetProperty(t => t.UpdatedAtUtc, DateTimeOffset.UtcNow), ct);
    }

    public async Task ReleaseClaimAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await db.SiteTalepleri.Where(t => t.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Durum, PublicBookingRequestDurum.Yeni)
                .SetProperty(t => t.DonusenReservationId, (Guid?)null)
                .SetProperty(t => t.UpdatedAtUtc, DateTimeOffset.UtcNow), ct);
    }

    public async Task<Guid?> FindCustomerIdByPhoneAsync(string telefon, CancellationToken ct = default)
    {
        var hedef = OnlyDigits(telefon);
        if (hedef.Length < 7) return null; // anlamlı eşleşme için çok kısa — yeni cari açılsın

        await using var db = await _factory.CreateDbContextAsync(ct);
        // Normalize karşılaştırma DB'de ifade edilemez (regexp_replace EF'e çevrilmiyor) → aday kümesi
        // bellekte süzülür. Cari sayısı tenant başına makul; CepTel/Gsm2 dolu olanlarla sınırlanır.
        var adaylar = await db.Customers.AsNoTracking()
            .Where(c => c.CepTel != null || c.Gsm2 != null)
            .Select(c => new { c.Id, c.CepTel, c.Gsm2 })
            .ToListAsync(ct);

        return adaylar.FirstOrDefault(c =>
            OnlyDigits(c.CepTel) == hedef || OnlyDigits(c.Gsm2) == hedef)?.Id;
    }

    /// <summary>"0555 111 22 33" → "05551112233" (boşluk/tire/parantez/+ atılır).</summary>
    private static string OnlyDigits(string? s)
        => string.IsNullOrWhiteSpace(s) ? string.Empty : new string(s.Where(char.IsAsciiDigit).ToArray());
}
