using Microsoft.EntityFrameworkCore;
using RentACar.Application.Kur;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// TCMB kur okuması (KurKayitlari — paylaşımlı/platform, RLS yok → tenant'tan bağımsız). Yazma
/// TcmbKurService'te (app-conn + NullTenantContext). Fallback: bu kod için ≤tarih en yeni satır.
/// </summary>
public sealed class KurRepository(IDbContextFactory<AppDbContext> factory) : IExchangeRateRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<KurKaydi?> GetAsync(string kod, DateTimeOffset tarih, CancellationToken ct = default)
    {
        var k = kod.Trim().ToUpperInvariant();
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.KurKayitlari.AsNoTracking()
            .Where(x => x.Kod == k && x.Tarih <= tarih)
            .OrderByDescending(x => x.Tarih)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<KurKaydi>> ListByDateAsync(DateTimeOffset tarih, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var enYeni = await db.KurKayitlari.AsNoTracking()
            .Where(x => x.Tarih <= tarih)
            .MaxAsync(x => (DateTimeOffset?)x.Tarih, ct);
        if (enYeni is null) return [];
        return await db.KurKayitlari.AsNoTracking()
            .Where(x => x.Tarih == enYeni)
            .OrderBy(x => x.Kod)
            .ToListAsync(ct);
    }

    public async Task<DateTimeOffset?> LatestDateAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.KurKayitlari.AsNoTracking().MaxAsync(x => (DateTimeOffset?)x.Tarih, ct);
    }
}
