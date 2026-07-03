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

        // KVKK/F2: eski düz-metin PII'yı şifrele + blind-index doldur + tarihsel audit izlerini
        // maskele (idempotent; tenant-loop — BYPASSRLS varsayımı yok).
        var log = sp.GetRequiredService<ILogger<AppDbContext>>();
        var backfilled = await PiiBackfill.RunAsync(db,
            sp.GetRequiredService<RentACar.Application.Common.ISecretProtector>(),
            sp.GetRequiredService<RentACar.Application.Common.IPiiHasher>(), log);
        if (backfilled > 0)
            log.LogInformation("PII backfill: {Count} cari şifrelendi (KVKK/F2).", backfilled);

        // Şube-FK (roadmap F1 tamamlama): mevcut satırların serbest-metin şubesini Branch FK'sine
        // doldur (idempotent; interceptor yeni yazımları zaten çözer).
        await BranchBackfill.RunAsync(db, log);

        if (await db.Tenants.AnyAsync()) return; // zaten seed edilmiş

        var hasher = new PasswordHasher<User>();

        var t1 = new Tenant { Code = "yucerent", Name = "Yüce Rent A Car" };
        var t2 = new Tenant { Code = "demo", Name = "Demo Filo" };
        db.Tenants.AddRange(t1, t2);

        db.Users.AddRange(
            NewUser(t1.Id, "umit", "Ümit (Yüce Rent)", UserRole.Admin, hasher),
            NewUser(t1.Id, "operator", "Operatör (Merkez şube)", UserRole.Operator, hasher, sube: "Merkez"),
            NewUser(t2.Id, "umit", "Ümit (Demo Filo)", UserRole.Admin, hasher));

        await db.SaveChangesAsync();
    }

    private static User NewUser(
        Guid tenantId, string userName, string displayName, UserRole rol, IPasswordHasher<User> hasher, string? sube = null)
    {
        var user = new User
        {
            TenantId = tenantId, UserName = userName, DisplayName = displayName, Rol = rol, AtanmisSube = sube
        };
        user.PasswordHash = hasher.HashPassword(user, "umit1376");
        return user;
    }
}
