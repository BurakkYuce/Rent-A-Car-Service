using Microsoft.EntityFrameworkCore;
using RentACar.Application.Users;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Kullanıcı izin istisnaları kalıcılığı. Tablo platform desenindedir (Users gibi — global query
/// filter YOK, login bootstrap GUC'suz okur) → tenant filtresi BURADA db.TenantId ile açıkça
/// uygulanır; DB tarafında komut-bazlı RLS ikinci savunma katmanı.
/// </summary>
public sealed class KullaniciIzinRepository(IDbContextFactory<AppDbContext> factory) : IUserPermissionRepository
{
    public async Task<IReadOnlyList<KullaniciIzinSatiri>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.KullaniciIzinIstisnalari.AsNoTracking()
            .Where(i => i.TenantId == db.TenantId)
            .OrderBy(i => i.UserId).ThenBy(i => i.Izin)
            .Select(i => new KullaniciIzinSatiri(i.UserId, i.Izin, i.Ver, i.TanimlayanKullanici, i.CreatedAtUtc))
            .ToListAsync(ct);
    }

    public async Task UpsertAsync(Guid userId, string izin, bool ver, string? tanimlayan, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var mevcut = await db.KullaniciIzinIstisnalari
            .FirstOrDefaultAsync(i => i.TenantId == db.TenantId && i.UserId == userId && i.Izin == izin, ct);
        if (mevcut is null)
        {
            db.KullaniciIzinIstisnalari.Add(new KullaniciIzinIstisna
            {
                TenantId = db.TenantId, // tenant damgası (UserRepository ile aynı; RLS WITH CHECK ikinci kez doğrular)
                UserId = userId,
                Izin = izin,
                Ver = ver,
                TanimlayanKullanici = tanimlayan,
            });
        }
        else
        {
            // Aynı izinde ver→yasak (veya tersi) çevirme: satır GÜNCELLENİR — unique index gereği
            // ikinci satır zaten yazılamaz, çatışma yapısal olarak imkânsız.
            mevcut.Ver = ver;
            mevcut.TanimlayanKullanici = tanimlayan;
            mevcut.CreatedAtUtc = DateTimeOffset.UtcNow;
        }
        // F11.1b güvenlik M2: istisna değişikliği denetim izine (tablo IAuditable değil).
        UserAdminAudit.Add(db, UserAdminAudit.UsersEntity, userId, Domain.Enums.AuditAction.Update,
            new UserAuditEntry(ver ? "IzinIstisnasiVer" : "IzinIstisnasiYasakla", izin));
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> RemoveAsync(Guid userId, string izin, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var silinen = await db.KullaniciIzinIstisnalari
            .Where(i => i.TenantId == db.TenantId && i.UserId == userId && i.Izin == izin)
            .ExecuteDeleteAsync(ct);
        if (silinen > 0)
        {
            UserAdminAudit.Add(db, UserAdminAudit.UsersEntity, userId, Domain.Enums.AuditAction.Update,
                new UserAuditEntry("IzinIstisnasiKaldir", izin));
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return silinen > 0;
    }
}
