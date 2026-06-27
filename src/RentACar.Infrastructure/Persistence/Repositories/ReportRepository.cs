using Microsoft.EntityFrameworkCore;
using RentACar.Application.Reporting;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Salt-okunur defter sorgusu. Verilen hesap türleri + tarih aralığındaki AccountLedgerEntry
/// satırlarını base tutarıyla (Amount×Rate) döndürür. Tenant izolasyonu query filter + RLS ile
/// otomatik. Karmaşık-tip (Money) alanları ham kolon olarak çekilir; base bellek-içi hesaplanır.
/// </summary>
public sealed class ReportRepository(IDbContextFactory<AppDbContext> factory) : IReportRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<LedgerRowDto>> GetLedgerRowsAsync(
        IReadOnlyCollection<LedgerAccountType> accountTypes,
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var q = db.AccountLedgerEntries.AsNoTracking().Where(e => accountTypes.Contains(e.AccountType));
        if (from is { } f) q = q.Where(e => e.EntryDateUtc >= f);
        if (to is { } t) q = q.Where(e => e.EntryDateUtc <= t);

        var raw = await q
            .Select(e => new
            {
                e.EntryDateUtc, e.AccountType, e.Direction, e.SourceType, e.Description,
                Amount = e.Amount.Amount, Rate = e.Amount.Rate
            })
            .ToListAsync(ct);

        return raw
            .Select(r => new LedgerRowDto(
                r.EntryDateUtc, r.AccountType, r.Direction, r.SourceType, r.Description, r.Amount * r.Rate))
            .ToList();
    }

    public async Task<IReadOnlyList<CariLedgerRowDto>> GetCariLedgerRowsAsync(
        DateTimeOffset? asOf, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var q = db.AccountLedgerEntries.AsNoTracking().Where(e => e.AccountType == LedgerAccountType.Cari);
        if (asOf is { } a) q = q.Where(e => e.EntryDateUtc <= a);

        var raw = await q
            .Select(e => new { e.AccountRef, e.Direction, Amount = e.Amount.Amount, Rate = e.Amount.Rate, e.EntryDateUtc })
            .ToListAsync(ct);

        // Cari adları: DisplayName mapped değil (computed) → Customers bellek-içi çekilip eşlenir.
        var names = (await db.Customers.AsNoTracking().ToListAsync(ct))
            .ToDictionary(c => c.Id, c => c.DisplayName);

        return raw
            .Select(r =>
            {
                var id = r.AccountRef ?? Guid.Empty;
                return new CariLedgerRowDto(
                    id, names.TryGetValue(id, out var n) && !string.IsNullOrWhiteSpace(n) ? n : "(bilinmeyen cari)",
                    r.Direction, r.Amount * r.Rate, r.EntryDateUtc);
            })
            .ToList();
    }
}
