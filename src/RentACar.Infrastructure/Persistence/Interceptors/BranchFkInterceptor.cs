using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Şube-FK denormalizasyonu (roadmap F1 tamamlama): kaydedilen <see cref="IBranchScoped"/> entity'lerin
/// SubeAdi serbest-metnini tenant Branch master'ından çözüp SubeFk'yi doldurur (BranchRepository.
/// FindByAdAsync ile BİREBİR: lower(btrim)=lower(btrim) eşleşme + aynı adda Kod sırası deterministik;
/// eşleşmezse null). Tek doğruluk kaynağı SubeAdi metni; SubeFk daima türev — MERKEZÎ, servisler tek
/// tek çözmez. Yalnız SubeAdi dolu Added/Modified entity varsa Branch okunur (ekstra sorgu nadir).
/// Audit interceptor'dan ÖNCE koşar (çözülen SubeFk denetim izine yansısın). Stateless → singleton.
/// Hem sync hem async SaveChanges yolu (audit interceptor ile simetri).
/// </summary>
public sealed class BranchFkInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is AppDbContext db)
        {
            var scoped = Collect(db);
            if (scoped.Count > 0)
                Apply(scoped, db.Set<Branch>().AsNoTracking().Select(b => new BranchRef(b.Id, b.Ad, b.Kod)).ToList());
        }
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (eventData.Context is AppDbContext db)
        {
            var scoped = Collect(db);
            if (scoped.Count > 0)
                Apply(scoped, await db.Set<Branch>().AsNoTracking()
                    .Select(b => new BranchRef(b.Id, b.Ad, b.Kod)).ToListAsync(ct));
        }
        return await base.SavingChangesAsync(eventData, result, ct);
    }

    private readonly record struct BranchRef(Guid Id, string Ad, string Kod);

    private static List<EntityEntry<IBranchScoped>> Collect(AppDbContext db)
    {
        db.ChangeTracker.DetectChanges();
        return db.ChangeTracker.Entries<IBranchScoped>()
            .Where(e => (e.State == EntityState.Added || e.State == EntityState.Modified)
                        && !string.IsNullOrWhiteSpace(e.Entity.SubeAdi))
            .ToList();
    }

    private static void Apply(List<EntityEntry<IBranchScoped>> scoped, List<BranchRef> branches)
    {
        // Ad case-insensitive → Id; aynı adda Kod sırası (L2 deseni; FindByAdAsync ile birebir).
        var map = branches
            .GroupBy(b => b.Ad.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.OrderBy(b => b.Kod).First().Id);

        foreach (var e in scoped)
        {
            var key = e.Entity.SubeAdi!.Trim().ToLowerInvariant();
            e.Entity.SubeFk = map.TryGetValue(key, out var id) ? id : null; // eşleşmezse null (metin korunur)
        }
    }
}
