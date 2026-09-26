using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Personnel;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Vardiya kalıcılığı (FAZ-45). Şube kapsamı C4 şablonuyla (FK öncelikli, metin yedek).</summary>
public sealed class StaffShiftRepository(IDbContextFactory<AppDbContext> factory) : IPersonnelShiftRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<VardiyaSatir>> SearchAsync(
        DateOnly start, DateOnly bit, VardiyaFilter filter,
        BranchScope.BranchFilter scope, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.PersonelVardiyalari.AsNoTracking().Where(v => v.Tarih >= start && v.Tarih <= bit);

        if (filter.PersonelId is Guid pid) q = q.Where(v => v.PersonelId == pid);

        if (!string.IsNullOrWhiteSpace(filter.Sube))
        {
            var s = filter.Sube.Trim();
            q = q.Where(v => v.Sube != null && v.Sube.Trim() == s);
        }

        // C4 ŞABLON (BranchScope.InScope ile birebir): FK-eşit VEYA (FK'lardan biri boşsa) metin-eşit.
        if (!scope.Unrestricted)
        {
            var kid = scope.SubeId; var kad = scope.SubeAd;
            q = q.Where(v => (kid != null && v.SubeId == kid)
                          || ((kid == null || v.SubeId == null) && kad != null && v.Sube != null && v.Sube.Trim() == kad));
        }

        // Personel adı JOIN'le çözülür (kopyalanmaz) — ad değişirse rapor da düzelir.
        var rows = await q
            .Join(db.Personeller.AsNoTracking(), v => v.PersonelId, p => p.Id,
                (v, p) => new { V = v, p.Ad, p.Soyad, KadroSube = p.Sube })
            .OrderBy(x => x.Ad).ThenBy(x => x.Soyad)
            .ThenBy(x => x.V.Tarih).ThenBy(x => x.V.BaslangicSaat)
            .ToListAsync(ct);

        return rows.Select(x => new VardiyaSatir(x.V, $"{x.Ad} {x.Soyad}".Trim(), x.KadroSube)).ToList();
    }

    public async Task<IReadOnlyList<PersonelVardiya>> ListForOverlapAsync(
        Guid staffId, DateOnly day, Guid? excludeId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // ±1 gün: gece vardiyası (22:00–06:00) komşu güne taştığı için dar pencere çakışmayı kaçırır.
        var sub = day.AddDays(-1); var parent = day.AddDays(1);
        return await db.PersonelVardiyalari.AsNoTracking()
            .Where(v => v.PersonelId == staffId && v.Tarih >= sub && v.Tarih <= parent
                        && (excludeId == null || v.Id != excludeId))
            .ToListAsync(ct);
    }

    public async Task<PersonelVardiya?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.PersonelVardiyalari.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, ct);
    }

    public async Task CreateAsync(PersonelVardiya row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.PersonelVardiyalari.Add(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> UpdateAsync(Guid id, Action<PersonelVardiya> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.PersonelVardiyalari.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (row is null) return false;
        apply(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.PersonelVardiyalari.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (row is null) return false;
        db.PersonelVardiyalari.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
