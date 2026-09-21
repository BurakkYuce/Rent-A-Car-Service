using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Identity;

namespace RentACar.Web.Persistence;

/// <summary>
/// Başlangıçta şemayı uygular (owner/Migrator bağlantısı) ve iki tenant + birer
/// kullanıcı seed eder. Tenants/Users platform tablolarıdır → owner ile yazılır
/// (racar_app yalnız okur). Seed idempotenttir.
/// <para><b>Seed parolası repoda YOK</b>: <c>Seed:Parola</c> yapılandırmasından gelir. Development'ta
/// verilmemişse ilk kurulumda BİR KEZ rastgele üretilir ve açılış logunda WARNING olarak basılır;
/// Development dışında verilmemişse demo firma/kullanıcı HİÇ oluşturulmaz (tahmin edilebilir parolalı
/// kullanıcı üretimde arka kapıdır). Seed yalnız boş veritabanında koşar → mevcut kullanıcıların
/// parolasına hiçbir açılışta dokunulmaz.</para>
/// </summary>
public static class DbInitializer
{
    /// <summary>Seed kullanıcılarının parolası (user-secret / ortam değişkeni <c>Seed__Parola</c>).</summary>
    public const string SeedParolaAnahtari = "Seed:Parola";

    /// <summary>Seed parolası kararı. <see cref="Parola"/> null → seed kullanıcıları oluşturulmaz.</summary>
    public readonly record struct SeedParolaKarari(string? Parola, bool Uretildi);

    /// <summary>
    /// Saf karar: yapılandırılmış parola her ortamda kazanır; yoksa Development'ta rastgele üretilir,
    /// diğer ortamlarda null (seed atlanır).
    /// </summary>
    public static SeedParolaKarari SeedParolasiCoz(string? yapilandirilan, bool gelistirme)
        => !string.IsNullOrWhiteSpace(yapilandirilan) ? new(yapilandirilan, Uretildi: false)
         : gelistirme ? new(GelistirmeParolasi.Uret(), Uretildi: true)
         : new(null, Uretildi: false);

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

        if (!await db.Tenants.AnyAsync()) // ilk kurulum: iki tenant + kullanıcıları
        {
            var karar = SeedParolasiCoz(
                sp.GetRequiredService<IConfiguration>()[SeedParolaAnahtari],
                sp.GetRequiredService<IHostEnvironment>().IsDevelopment());

            if (karar.Parola is null)
            {
                log.LogWarning(
                    "Seed atlandı: {Anahtar:l} yapılandırılmamış ve ortam Development değil — tahmin edilebilir "
                    + "parolalı demo firma/kullanıcı oluşturulmadı. Firmaları platform konsolundan açın.",
                    SeedParolaAnahtari);
            }
            else
            {
                var hasher = new PasswordHasher<User>();

                var t1 = new Tenant { Code = "yucerent", Name = "Yüce Rent A Car" };
                var t2 = new Tenant { Code = "demo", Name = "Demo Filo" };
                db.Tenants.AddRange(t1, t2);

                db.Users.AddRange(
                    NewUser(t1.Id, "umit", "Ümit (Yüce Rent)", UserRole.Admin, hasher, karar.Parola),
                    NewUser(t1.Id, "operator", "Operatör (Merkez şube)", UserRole.Operator, hasher, karar.Parola, sube: "Merkez"),
                    NewUser(t2.Id, "umit", "Ümit (Demo Filo)", UserRole.Admin, hasher, karar.Parola));

                await db.SaveChangesAsync();

                // Üretilen parola hiçbir yerde saklanmaz → tek görünür yer bu satır (yalnız Development,
                // yalnız bu ilk kurulum açılışında). Yapılandırılmış parola ASLA loglanmaz.
                if (karar.Uretildi)
                    log.LogWarning(
                        "Seed kullanıcı parolası: {Parola:l} (firma yucerent/demo, kullanıcı umit/operator; "
                        + "yalnız bu ilk kurulumda üretildi ve bir daha basılmaz — sabitlemek için {Anahtar:l})",
                        karar.Parola, SeedParolaAnahtari);
            }
        }

        // Tanım (master) varsayılanları — HER açılış, TÜM tenant'lar, idempotent (yalnız boş kategori dolar).
        // Guard'dan SONRA + koşulsuz: ilk-init'te YENİ oluşturulan tenant'lar da, mevcut/platform tenant'ları da kapsanır.
        await MasterDataSeeder.RunAsync(db, log);
    }

    private static User NewUser(
        Guid tenantId, string userName, string displayName, UserRole rol, IPasswordHasher<User> hasher,
        string parola, string? sube = null)
    {
        var user = new User
        {
            TenantId = tenantId, UserName = userName, DisplayName = displayName, Rol = rol, AtanmisSube = sube
        };
        user.PasswordHash = hasher.HashPassword(user, parola);
        return user;
    }
}
