using Microsoft.EntityFrameworkCore;
using RentACar.Application.Kur;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// TCMB kur okuması (KurKayitlari — paylaşımlı/platform, RLS yok → tenant'tan bağımsız). Yazma
/// TcmbKurService'te (app-conn + NullTenantContext). Fallback: bu kod için ≤tarih en yeni satır.
/// </summary>
public sealed class ExchangeRateRepository(IDbContextFactory<AppDbContext> factory) : IExchangeRateRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<KurKaydi?> GetAsync(string code, DateTimeOffset date, CancellationToken ct = default)
    {
        var k = code.Trim().ToUpperInvariant();
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.KurKayitlari.AsNoTracking()
            .Where(x => x.Kod == k && x.Tarih <= date)
            .OrderByDescending(x => x.Tarih)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<KurKaydi>> ListByDateAsync(DateTimeOffset date, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var newest = await db.KurKayitlari.AsNoTracking()
            .Where(x => x.Tarih <= date)
            .MaxAsync(x => (DateTimeOffset?)x.Tarih, ct);
        if (newest is null) return [];
        return await db.KurKayitlari.AsNoTracking()
            .Where(x => x.Tarih == newest)
            .OrderBy(x => x.Kod)
            .ToListAsync(ct);
    }

    public async Task<DateTimeOffset?> LatestDateAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.KurKayitlari.AsNoTracking().MaxAsync(x => (DateTimeOffset?)x.Tarih, ct);
    }
}
