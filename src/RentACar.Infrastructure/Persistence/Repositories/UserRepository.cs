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
}
