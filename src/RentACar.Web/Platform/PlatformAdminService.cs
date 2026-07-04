using Microsoft.EntityFrameworkCore;
using RentACar.Application.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Platform;

/// <summary>Konsol satırı: tenant + kullanıcı sayısı.</summary>
public sealed record PlatformTenantRow(
    Guid Id, string Code, string Name, bool IsActive, DateTimeOffset CreatedAtUtc, int UserCount);

/// <summary>
/// Platform süper-admin veri katmanı — YALNIZ platform tablolarına (<c>Tenants</c>/<c>Users</c>) dokunur.
/// Owner (<c>Migrator</c>/racar_owner) bağlantısı + <see cref="NullTenantContext"/> ile ELLE <see cref="AppDbContext"/>
/// kurar (DbInitializer deseni): options'a interceptor EKLENMEZ → GUC boş → owner Tenants (RLS'siz) + Users
/// (FORCE-değil) TÜMÜNÜ görür/yazar. FORCE-RLS iş verisine DOKUNMAZ (görülemez zaten). Users okumalarında
/// <c>IgnoreQueryFilters</c> (context tenant'ı boş → query filter aksi halde 0 döner).
/// </summary>
public sealed class PlatformAdminService(
    IConfiguration config, IPasswordHasher hasher, TenantStatusCache statusCache, ILogger<PlatformAdminService> log)
{
    private AppDbContext OwnerDb()
    {
        var conn = config.GetConnectionString("Migrator")
            ?? throw new InvalidOperationException("ConnectionStrings:Migrator eksik (platform konsolu owner bağlantısı).");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(conn).Options;
        return new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance);
    }

    /// <summary>Tüm tenant'lar + tenant başına kullanıcı sayısı (owner tümünü görür).</summary>
    public async Task<IReadOnlyList<PlatformTenantRow>> ListTenantsAsync(CancellationToken ct = default)
    {
        await using var db = OwnerDb();
        var tenants = await db.Tenants.AsNoTracking().OrderBy(t => t.Code).ToListAsync(ct);
        var counts = await db.Users.AsNoTracking().IgnoreQueryFilters()
            .GroupBy(u => u.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);
        return tenants
            .Select(t => new PlatformTenantRow(t.Id, t.Code, t.Name, t.IsActive, t.CreatedAtUtc,
                counts.GetValueOrDefault(t.Id)))
            .ToList();
    }

    /// <summary>Tenant erişimini aç/kapa (+ anlık kesme için cache invalidate).</summary>
    public async Task SetActiveAsync(Guid tenantId, bool active, string operatorName, CancellationToken ct = default)
    {
        await using var db = OwnerDb();
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw new ValidationException("Tenant bulunamadı.");
        if (tenant.IsActive == active) return;
        tenant.IsActive = active;
        await db.SaveChangesAsync(ct);
        statusCache.Invalidate(tenantId); // anlık kesme (aynı-instance): açık oturum sonraki istekte düşer
        log.LogWarning("PLATFORM: tenant {Code} ({TenantId}) {Durum} — operatör {Operator}.",
            tenant.Code, tenantId, active ? "AÇILDI" : "KAPATILDI", operatorName);
    }

    /// <summary>Yeni tenant + ilk admin kullanıcı oluştur.</summary>
    public async Task CreateTenantAsync(
        string code, string name, string adminUser, string adminPassword, string operatorName, CancellationToken ct = default)
    {
        code = (code ?? "").Trim();
        name = (name ?? "").Trim();
        adminUser = (adminUser ?? "").Trim();
        // Kolon sınırları (adversarial L1): aşarsa DbUpdateException→500 yerine anlamlı ValidationException.
        if (string.IsNullOrWhiteSpace(code)) throw new ValidationException("Firma kodu zorunludur.");
        if (code.Length > 64) throw new ValidationException("Firma kodu en çok 64 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(name)) throw new ValidationException("Firma adı zorunludur.");
        if (name.Length > 256) throw new ValidationException("Firma adı en çok 256 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(adminUser)) throw new ValidationException("Admin kullanıcı adı zorunludur.");
        if (adminUser.Length > 128) throw new ValidationException("Admin kullanıcı adı en çok 128 karakter olabilir.");
        if ((adminPassword ?? "").Length < 6) throw new ValidationException("Admin parolası en az 6 karakter olmalıdır.");

        await using var db = OwnerDb();
        if (await db.Tenants.AnyAsync(t => t.Code == code, ct)) // IX_Tenants_Code — çift kod login'i bozar
            throw new ValidationException($"'{code}' kodlu firma zaten var.");

        var tenant = new Tenant { Code = code, Name = name };
        var user = new User
        {
            TenantId = tenant.Id,
            UserName = adminUser,
            DisplayName = adminUser,
            Rol = UserRole.Admin,
            PasswordHash = hasher.Hash(adminPassword!), // LoginService (PasswordHasher<User>) doğrular — uyumlu
        };
        db.Tenants.Add(tenant);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        // Tanım varsayılanları (marka/renk/segment/ceza türü…) — yeni tenant restart beklemeden dolu başlasın.
        await MasterDataSeeder.SeedTenantAsync(db, tenant.Id, ct);

        log.LogWarning("PLATFORM: yeni tenant {Code} + admin {Admin} oluşturuldu — operatör {Operator}.",
            code, adminUser, operatorName);
    }
}
