using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Calendar;

/// <summary>
/// Kullanıcının iCal token'ını yönetir. Denetim O3 fix: runtime'da OWNER (Migrator) bağlantısı KULLANILMAZ
/// (kilitli kural: uygulama daima racar_app). Users yazımı racar_app'e açık ve users_update policy'si
/// tenant-kısıtlı → kullanıcı satırı önce okunur (users_select GUC boşken açık — login-bootstrap deseni),
/// GUC kullanıcının KENDİ tenant'ına set edilir, sonra token yazılır (policy ancak o tenant'ın satırına izin
/// verir → çapraz-tenant yazım DB seviyesinde imkânsız).
/// </summary>
public sealed class CalendarTokenService(IConfiguration config)
{
    private AppDbContext AppDb()
    {
        var conn = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default eksik (takvim token yazımı).");
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(conn).Options,
            NullTenantContext.Instance, NullCurrentUser.Instance);
    }

    /// <summary>Kullanıcıyı okur ve GUC'u kullanıcının tenant'ına set eder (bağlantı açık tutulur — update
    /// policy'si bu GUC'a bakar).</summary>
    private static async Task<User> LoadWithGucAsync(AppDbContext db, Guid userId, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct); // GUC bağlantı ömrünce açık kalmalı
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new InvalidOperationException("Kullanıcı bulunamadı.");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.tenant_id', {user.TenantId.ToString()}, false)", ct);
        return user;
    }

    public async Task<string> EnsureAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = AppDb();
        var user = await LoadWithGucAsync(db, userId, ct);
        if (string.IsNullOrEmpty(user.CalendarToken))
        {
            user.CalendarToken = CalendarTokenUtil.New();
            await db.SaveChangesAsync(ct);
        }
        return user.CalendarToken!;
    }

    public async Task<string> RegenerateAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = AppDb();
        var user = await LoadWithGucAsync(db, userId, ct);
        user.CalendarToken = CalendarTokenUtil.New();
        await db.SaveChangesAsync(ct);
        return user.CalendarToken!;
    }
}
