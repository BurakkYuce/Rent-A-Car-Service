using Microsoft.EntityFrameworkCore;
using RentACar.Application.Pricing;
using RentACar.Domain.Entities;

using RentACar.Domain.Common;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Kaydedilmiş maliyet teklifi kalıcılığı (FAZ-74). Deftere DOKUNMAZ.</summary>
public sealed class CostQuotationRepository(IDbContextFactory<AppDbContext> factory) : ICostQuotationRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<MaliyetTeklifi>> SearchAsync(
        MaliyetTeklifiFilter filter, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.MaliyetTeklifleri.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Metin))
        {
            var m = filter.Metin.Trim();
            q = q.Where(x => EF.Functions.ILike(x.Baslik, $"%{m}%")
                             || EF.Functions.ILike(x.KayitNo, $"%{m}%")
                             || (x.Aciklama != null && EF.Functions.ILike(x.Aciklama, $"%{m}%")));
        }
        if (!string.IsNullOrWhiteSpace(filter.Plaka))
        {
            var p = filter.Plaka.Trim();
            q = q.Where(x => x.Plaka != null && EF.Functions.ILike(x.Plaka, $"%{p}%"));
        }
        if (filter.CariId is Guid c) q = q.Where(x => x.CariId == c);
        if (filter.TarihMin is { } tmin) q = q.Where(x => x.Tarih >= tmin);
        if (filter.TarihMax is { } tmax) q = q.Where(x => x.Tarih <= tmax);
        // Fiyat aralığı ARAÇ BAŞINA aylık net üzerinden — filo toplamı türetilmiş olduğu için
        // SQL'e çevrilemez (ve adet karıştırılırsa aynı araç fiyatı farklı kovaya düşerdi).
        if (filter.FiyatMin is { } fmin) q = q.Where(x => x.TeklifAylikNet >= fmin);
        if (filter.FiyatMax is { } fmax) q = q.Where(x => x.TeklifAylikNet <= fmax);

        return await q.OrderByDescending(x => x.Tarih).ThenByDescending(x => x.KayitNo).ToListAsync(ct);
    }

    public async Task<MaliyetTeklifi?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.MaliyetTeklifleri.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task CreateAsync(MaliyetTeklifi row, CancellationToken ct = default)
    {
        await PgRetry.RunAsync(async () =>   // deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            // No tahsisi INSERT ile AYNI transaction: rollback numarayı geri alır → boşluk olmaz.
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            row.KayitNo = await DocumentNoGenerator.GenerateAsync(db, db.TenantId, DocumentNoType.MaliyetTeklifi, ct);
            db.MaliyetTeklifleri.Add(row);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }, ct);
    }

    public async Task<bool> UpdateAsync(Guid id, Action<MaliyetTeklifi> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.MaliyetTeklifleri.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return false;
        apply(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.MaliyetTeklifleri.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return false;
        db.MaliyetTeklifleri.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
