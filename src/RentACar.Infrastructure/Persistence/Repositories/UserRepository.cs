using Microsoft.EntityFrameworkCore;
using RentACar.Application.Users;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Kullanıcı kalıcılığı. Users platform tablosudur (global query filter YOK) → tenant
/// filtresi BURADA db.TenantId ile açıkça uygulanır. Oluşturmada TenantId damgalanır;
/// DB tarafında RLS yazma politikası (WITH CHECK tenant) ikinci savunma katmanıdır.
/// </summary>
public sealed class UserRepository(IDbContextFactory<AppDbContext> factory) : IUserRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<User>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking()
            .Where(u => u.TenantId == db.TenantId)
            .OrderBy(u => u.UserName)
            .ToListAsync(ct);
    }

    public async Task<User?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == db.TenantId, ct);
    }

    public async Task<bool> UserNameExistsAsync(string userName, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking()
            .AnyAsync(u => u.TenantId == db.TenantId && u.UserName == userName, ct);
    }

    public async Task CreateAsync(User user, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        user.TenantId = db.TenantId; // tenant damgası (RLS WITH CHECK ile de doğrulanır)
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> UpdateAsync(Guid id, Action<User> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == db.TenantId, ct);
        if (user is null) return false;
        apply(user);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>F11.1b güvenlik M2 — ekleme + denetim kaydı tek işlemde.</summary>
    public async Task CreateAsync(User user, UserAuditEntry audit, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        user.TenantId = db.TenantId;
        db.Users.Add(user);
        UserAdminAudit.Add(db, UserAdminAudit.UsersEntity, user.Id, Domain.Enums.AuditAction.Create, audit,
            new Dictionary<string, object?> { ["KullaniciAdi"] = user.UserName, ["Rol"] = user.Rol.ToString() });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// F11.1b güvenlik M1/M2 — kiracı kilidi ALTINDA güncelleme: uygulamadan sonra aktif Admin kalmayacaksa red
    /// (sayım kilit altında; eşzamanlı iki pasifleştirme ayrı ayrı geçemez); denetim kaydı aynı işlemde.
    /// </summary>
    public async Task<bool> UpdateAuditedAsync(Guid id, Action<User> apply, UserAuditEntry audit, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await UserAdminAudit.LockTenantAsync(db, ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == db.TenantId, ct);
        if (user is null) return false;
        var wasActiveAdmin = user is { IsActive: true, Rol: Domain.Enums.UserRole.Admin };
        apply(user);
        if (wasActiveAdmin && user is not { IsActive: true, Rol: Domain.Enums.UserRole.Admin }
            && !await db.Users.AsNoTracking().AnyAsync(u => u.TenantId == db.TenantId && u.Id != id && u.IsActive
                                                          && u.Rol == Domain.Enums.UserRole.Admin, ct))
            throw new Application.Common.ValidationException("Son aktif Admin kullanıcısı pasifleştirilemez.");
        UserAdminAudit.Add(db, UserAdminAudit.UsersEntity, id, Domain.Enums.AuditAction.Update, audit,
            new Dictionary<string, object?> { ["KullaniciAdi"] = user.UserName });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
