using Microsoft.EntityFrameworkCore;
using RentACar.Application.Users;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Kullanıcı izin istisnaları kalıcılığı. Tablo platform desenindedir (Users gibi — global query
/// filter YOK, login bootstrap GUC'suz okur) → tenant filtresi BURADA db.TenantId ile açıkça
/// uygulanır; DB tarafında komut-bazlı RLS ikinci savunma katmanı.
/// </summary>
public sealed class UserPermissionRepository(IDbContextFactory<AppDbContext> factory) : IUserPermissionRepository
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

    public async Task UpsertAsync(Guid userId, string permission, bool give, string? definedBy, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var existing = await db.KullaniciIzinIstisnalari
            .FirstOrDefaultAsync(i => i.TenantId == db.TenantId && i.UserId == userId && i.Izin == permission, ct);
        if (existing is null)
        {
            db.KullaniciIzinIstisnalari.Add(new KullaniciIzinIstisna
            {
                TenantId = db.TenantId, // tenant damgası (UserRepository ile aynı; RLS WITH CHECK ikinci kez doğrular)
                UserId = userId,
                Izin = permission,
                Ver = give,
                TanimlayanKullanici = definedBy,
            });
        }
        else
        {
            // Aynı izinde ver→yasak (veya tersi) çevirme: satır GÜNCELLENİR — unique index gereği
            // ikinci satır zaten yazılamaz, çatışma yapısal olarak imkânsız.
            existing.Ver = give;
            existing.TanimlayanKullanici = definedBy;
            existing.CreatedAtUtc = DateTimeOffset.UtcNow;
        }
        // F11.1b güvenlik M2: istisna değişikliği denetim izine (tablo IAuditable değil).
        UserAdminAudit.Add(db, UserAdminAudit.UsersEntity, userId, Domain.Enums.AuditAction.Update,
            new UserAuditEntry(give ? "IzinIstisnasiVer" : "IzinIstisnasiYasakla", permission));
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> RemoveAsync(Guid userId, string permission, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var deleted = await db.KullaniciIzinIstisnalari
            .Where(i => i.TenantId == db.TenantId && i.UserId == userId && i.Izin == permission)
            .ExecuteDeleteAsync(ct);
        if (deleted > 0)
        {
            UserAdminAudit.Add(db, UserAdminAudit.UsersEntity, userId, Domain.Enums.AuditAction.Update,
                new UserAuditEntry("IzinIstisnasiKaldir", permission));
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return deleted > 0;
    }
}
