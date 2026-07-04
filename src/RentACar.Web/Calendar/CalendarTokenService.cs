using Microsoft.EntityFrameworkCore;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Calendar;

/// <summary>
/// Kullanıcının iCal token'ını yönetir (Users platform tablosu → owner/Migrator conn ile yazılır; app yalnız okur).
/// EnsureAsync: yoksa üret+kaydet; RegenerateAsync: yeni token (eskisi anında geçersiz olur).
/// </summary>
public sealed class CalendarTokenService(IConfiguration config)
{
    private AppDbContext OwnerDb()
    {
        var conn = config.GetConnectionString("Migrator")
            ?? throw new InvalidOperationException("ConnectionStrings:Migrator eksik (takvim token yazımı).");
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(conn).Options,
            NullTenantContext.Instance, NullCurrentUser.Instance);
    }

    public async Task<string> EnsureAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = OwnerDb();
        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new InvalidOperationException("Kullanıcı bulunamadı.");
        if (string.IsNullOrEmpty(user.CalendarToken))
        {
            user.CalendarToken = CalendarTokenUtil.New();
            await db.SaveChangesAsync(ct);
        }
        return user.CalendarToken!;
    }

    public async Task<string> RegenerateAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = OwnerDb();
        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new InvalidOperationException("Kullanıcı bulunamadı.");
        user.CalendarToken = CalendarTokenUtil.New();
        await db.SaveChangesAsync(ct);
        return user.CalendarToken!;
    }
}
