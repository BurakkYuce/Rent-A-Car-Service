using Microsoft.EntityFrameworkCore;
using RentACar.Application.Search;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// ISearchRepository (roadmap C4): cross-module ILIKE araması. Her DbSet tenant query filter + RLS ile
/// otomatik kapsamlı. Tür başına perTypeLimit ile sınırlı (sessiz kırpma — UI "ilk N" gösterir).
/// </summary>
public sealed class SearchRepository(IDbContextFactory<AppDbContext> factory) : ISearchRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string q, int perTypeLimit, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var like = $"%{q}%";
        var hits = new List<SearchHit>();

        var araclar = await db.Vehicles.AsNoTracking()
            .Where(v => EF.Functions.ILike(v.Plaka, like) || (v.Marka != null && EF.Functions.ILike(v.Marka, like)))
            .OrderBy(v => v.Plaka).Take(perTypeLimit)
            .Select(v => new { v.Id, v.Plaka, v.Marka }).ToListAsync(ct);
        hits.AddRange(araclar.Select(v => new SearchHit("Araç", v.Plaka, v.Marka, $"/araclar/{v.Id}")));

        var cariler = await db.Customers.AsNoTracking()
            .Where(c => (c.Ad != null && EF.Functions.ILike(c.Ad, like)) || (c.Unvan != null && EF.Functions.ILike(c.Unvan, like)))
            .OrderBy(c => c.Ad).Take(perTypeLimit)
            .Select(c => new { c.Id, c.Ad, c.Unvan }).ToListAsync(ct);
        hits.AddRange(cariler.Select(c => new SearchHit("Cari", c.Ad ?? c.Unvan ?? "(isimsiz)", c.Unvan, $"/cariler/{c.Id}")));

        var kiralar = await db.Rentals.AsNoTracking()
            .Where(r => EF.Functions.ILike(r.SozlesmeNo, like))
            .OrderByDescending(r => r.SozlesmeNo).Take(perTypeLimit)
            .Select(r => new { r.Id, r.SozlesmeNo }).ToListAsync(ct);
        hits.AddRange(kiralar.Select(r => new SearchHit("Kira", r.SozlesmeNo, null, $"/kiralar/{r.Id}")));

        var rezler = await db.Reservations.AsNoTracking()
            .Where(r => EF.Functions.ILike(r.ReservationNo, like))
            .OrderByDescending(r => r.ReservationNo).Take(perTypeLimit)
            .Select(r => new { r.ReservationNo }).ToListAsync(ct);
        hits.AddRange(rezler.Select(r => new SearchHit("Rezervasyon", r.ReservationNo, null, "/rezervasyonlar")));

        var faturalar = await db.Invoices.AsNoTracking()
            .Where(i => EF.Functions.ILike(i.No, like))
            .OrderByDescending(i => i.No).Take(perTypeLimit)
            .Select(i => new { i.No }).ToListAsync(ct);
        hits.AddRange(faturalar.Select(i => new SearchHit("Fatura", i.No, null, "/faturalar")));

        return hits;
    }
}
