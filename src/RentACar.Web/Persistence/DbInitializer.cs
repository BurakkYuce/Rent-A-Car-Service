using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Persistence;

/// <summary>
/// Başlangıçta şemayı uygular (owner/Migrator bağlantısı) ve iki tenant + birer
/// kullanıcı seed eder. Tenants/Users platform tablolarıdır → owner ile yazılır
/// (racar_app yalnız okur). Seed idempotenttir.
/// </summary>
public static class DbInitializer
{
    public static async Task MigrateAndSeedAsync(IServiceProvider sp, string migratorConnectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(migratorConnectionString)
            .Options;

        await using var db = new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance);
        await db.Database.MigrateAsync();

        if (await db.Tenants.AnyAsync()) return; // zaten seed edilmiş

        var hasher = new PasswordHasher<User>();

        var t1 = new Tenant { Code = "yucerent", Name = "Yüce Rent A Car" };
        var t2 = new Tenant { Code = "demo", Name = "Demo Filo" };
        db.Tenants.AddRange(t1, t2);

        db.Users.AddRange(
            NewUser(t1.Id, "umit", "Ümit (Yüce Rent)", UserRole.Admin, hasher),
            NewUser(t1.Id, "operator", "Operatör (Yüce Rent)", UserRole.Operator, hasher),
            NewUser(t2.Id, "umit", "Ümit (Demo Filo)", UserRole.Admin, hasher));

        await db.SaveChangesAsync();
    }

    private static User NewUser(
        Guid tenantId, string userName, string displayName, UserRole rol, IPasswordHasher<User> hasher)
    {
        var user = new User { TenantId = tenantId, UserName = userName, DisplayName = displayName, Rol = rol };
        user.PasswordHash = hasher.HashPassword(user, "umit1376");
        return user;
    }
}
