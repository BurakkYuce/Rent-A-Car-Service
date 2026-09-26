using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Ofis→şube FK denormalizasyonu (FAZ 5-C4; BranchFkInterceptor deseninin ofis karşılığı):
/// kaydedilen <see cref="IOfficeScoped"/> belgelerin CikisOfisi metnini tenant Location master'ından
/// çözüp (lower/trim eşleşme; aynı adda Kod sırası deterministik) Location.SubeId'yi OfisSubeFk'ya
/// yazar. Location eşleşmez ya da Location.SubeId null → null (salt-metin davranış — kilitlenme yok).
/// Metin doğruluk-kaynağı; FK daima türev. Stateless → singleton; audit'ten ÖNCE koşar.
/// </summary>
public sealed class OfficeBranchInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is AppDbContext db)
        {
            var scoped = Collect(db);
            if (scoped.Count > 0)
                Apply(scoped, db.Set<Location>().AsNoTracking()
                    .Select(l => new LocRef(l.Ad, l.Kod, l.SubeId)).ToList());
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
                Apply(scoped, await db.Set<Location>().AsNoTracking()
                    .Select(l => new LocRef(l.Ad, l.Kod, l.SubeId)).ToListAsync(ct));
        }
        return await base.SavingChangesAsync(eventData, result, ct);
    }

    private readonly record struct LocRef(string Ad, string Kod, Guid? SubeId);

    private static List<EntityEntry<IOfficeScoped>> Collect(AppDbContext db)
    {
        db.ChangeTracker.DetectChanges();
        return db.ChangeTracker.Entries<IOfficeScoped>()
            .Where(e => (e.State == EntityState.Added || e.State == EntityState.Modified)
                        && !string.IsNullOrWhiteSpace(e.Entity.OfisAdi))
            .ToList();
    }

    private static void Apply(List<EntityEntry<IOfficeScoped>> scoped, List<LocRef> locations)
    {
        // F11.1b güvenlik H1: aynı ad anahtarını taşıyan ofisler FARKLI şubelere bağlıysa anahtar BELİRSİZDİR → null
        // (salt-metin davranış). Eskiden en düşük Kod kazanıyordu: bir operatör başka şubenin ofis adını ("otogar b ")
        // kendi şubesine düşük kodla açıp o şubenin kayıtlarının türetilmiş şubesini ele geçiriyordu.
        var map = locations
            .GroupBy(l => OfficeNameKey.Generate(l.Ad))
            .ToDictionary(g => g.Key, g => OfficeNameKey.UnambiguousBranch(g.Select(l => l.SubeId)));

        foreach (var e in scoped)
        {
            var key = OfficeNameKey.Generate(e.Entity.OfisAdi!);
            e.Entity.OfisSubeFk = map.TryGetValue(key, out var branchId) ? branchId : null;
        }
    }
}
