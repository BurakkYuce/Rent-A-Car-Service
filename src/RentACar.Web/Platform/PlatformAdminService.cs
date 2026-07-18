using Microsoft.EntityFrameworkCore;
using RentACar.Application.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Platform;

/// <summary>Konsol satırı: tenant + temel metrikler. Rozet önceliği: KapanisTarihiUtc set ise
/// IsActive ne olursa olsun KAPALI (elle DB anomalisinde kapalı tenant "Aktif" gezemez).</summary>
public sealed record PlatformTenantRow(
    Guid Id, string Code, string Name, bool IsActive, DateTimeOffset? KapanisTarihiUtc,
    DateTimeOffset CreatedAtUtc, int UserCount, int AracSayisi, int AktifKira, DateTimeOffset? SonGiris)
{
    public string Durum => KapanisTarihiUtc is not null ? "Kapalı" : IsActive ? "Aktif" : "Pasif";
}

/// <summary>Tenant detayı: tüm bilgi alanları + metrikler (owner cross-tenant sayımları).</summary>
public sealed record PlatformTenantDetay(
    Guid Id, string Code, string Name, bool IsActive, DateTimeOffset? KapanisTarihiUtc,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc,
    string? YetkiliAd, string? Eposta, string? Telefon, string? Notlar, string? Plan,
    int UserCount, int AracSayisi, int AktifKira, int ToplamKira, DateTimeOffset? SonGiris, decimal Gelir30Gun)
{
    public string Durum => KapanisTarihiUtc is not null ? "Kapalı" : IsActive ? "Aktif" : "Pasif";
}

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

    /// <summary>FORCE-RLS iş tablolarını (Vehicles/Rentals/AccountLedgerEntries) TEK tenant kapsamında okur.
    /// racar_owner'da BYPASSRLS YOK (canlıda doğrulandı) → GUC'suz sorgu 0 satır döner; tx-YEREL set_config
    /// (is_local=true) ile politika o tenant için açılır, commit'te GUC buharlaşır (havuz sızıntısı yok).
    /// EF query-filter'ı ayrıca IgnoreQueryFilters ister (context tenant'ı boş).</summary>
    private static async Task<T> TenantKapsaminda<T>(AppDbContext db, Guid tenantId, Func<Task<T>> sorgu, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT set_config('app.tenant_id', {tenantId.ToString()}, true)", ct);
        var sonuc = await sorgu();
        await tx.CommitAsync(ct);
        return sonuc;
    }

    /// <summary>Tüm tenant'lar + temel metrikler (owner cross-tenant GroupBy sayımları — FORCE-RLS
    /// iş tabloları owner'a görünür; tek tek sorgu değil toplu sözlükler).</summary>
    public async Task<IReadOnlyList<PlatformTenantRow>> ListTenantsAsync(CancellationToken ct = default)
    {
        await using var db = OwnerDb();
        var tenants = await db.Tenants.AsNoTracking().OrderBy(t => t.Code).ToListAsync(ct);
        var counts = await db.Users.AsNoTracking().IgnoreQueryFilters()
            .GroupBy(u => u.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);
        var sonGiris = await db.Users.AsNoTracking().IgnoreQueryFilters()
            .Where(u => u.LastLoginAtUtc != null)
            .GroupBy(u => u.TenantId)
            .Select(g => new { TenantId = g.Key, Son = g.Max(u => u.LastLoginAtUtc) })
            .ToDictionaryAsync(x => x.TenantId, x => x.Son, ct);
        // İş-tablosu sayımları FORCE-RLS ardında → tenant başına GUC'lu tx (konsolda az tenant; kabul).
        var rows = new List<PlatformTenantRow>(tenants.Count);
        foreach (var t in tenants)
        {
            var (arac, aktifKira) = await TenantKapsaminda(db, t.Id, async () => (
                await db.Vehicles.AsNoTracking().IgnoreQueryFilters().CountAsync(v => v.TenantId == t.Id, ct),
                await db.Rentals.AsNoTracking().IgnoreQueryFilters()
                    .CountAsync(r => r.TenantId == t.Id && r.Durum == RentalStatus.Kirada, ct)), ct);
            rows.Add(new PlatformTenantRow(t.Id, t.Code, t.Name, t.IsActive, t.KapanisTarihiUtc,
                t.CreatedAtUtc, counts.GetValueOrDefault(t.Id), arac, aktifKira, sonGiris.GetValueOrDefault(t.Id)));
        }
        return rows;
    }

    /// <summary>Tenant detayı: bilgi alanları + metrikler. Gelir30Gun = son 30 gün defter Gelir
    /// (ΣCredit−ΣDebit base — GelirGider netleme aynası, iade düşer).</summary>
    public async Task<PlatformTenantDetay?> GetTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = OwnerDb();
        var t = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId, ct);
        if (t is null) return null;

        var userCount = await db.Users.AsNoTracking().IgnoreQueryFilters().CountAsync(u => u.TenantId == tenantId, ct);
        var sonGiris = await db.Users.AsNoTracking().IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId).MaxAsync(u => (DateTimeOffset?)u.LastLoginAtUtc, ct);

        // İş tabloları FORCE-RLS ardında → tenant-GUC'lu tx içinde okunur (owner'da BYPASSRLS yok).
        var esik = DateTimeOffset.UtcNow.AddDays(-30);
        var (aracSayisi, toplamKira, aktifKira, gelir30) = await TenantKapsaminda(db, tenantId, async () =>
        {
            var arac = await db.Vehicles.AsNoTracking().IgnoreQueryFilters().CountAsync(v => v.TenantId == tenantId, ct);
            var toplam = await db.Rentals.AsNoTracking().IgnoreQueryFilters().CountAsync(r => r.TenantId == tenantId, ct);
            var aktif = await db.Rentals.AsNoTracking().IgnoreQueryFilters()
                .CountAsync(r => r.TenantId == tenantId && r.Durum == RentalStatus.Kirada, ct);
            var gelirRows = await db.AccountLedgerEntries.AsNoTracking().IgnoreQueryFilters()
                .Where(e => e.TenantId == tenantId && e.AccountType == LedgerAccountType.Gelir && e.EntryDateUtc >= esik)
                .Select(e => new { e.Direction, e.Amount })
                .ToListAsync(ct);
            var gelir = gelirRows.Sum(e => e.Direction == LedgerDirection.Credit
                ? e.Amount.AmountInBase : -e.Amount.AmountInBase); // iade (Debit) netlenir
            return (arac, toplam, aktif, gelir);
        }, ct);

        return new PlatformTenantDetay(t.Id, t.Code, t.Name, t.IsActive, t.KapanisTarihiUtc,
            t.CreatedAtUtc, t.UpdatedAtUtc, t.YetkiliAd, t.Eposta, t.Telefon, t.Notlar, t.Plan,
            userCount, aracSayisi, aktifKira, toplamKira, sonGiris, gelir30);
    }

    /// <summary>Bilgi alanlarını günceller. Code DEĞİŞMEZ (login anahtarı). Kolon sınırları burada
    /// doğrulanır (L1 deseni: DbUpdateException→500 yerine anlamlı red).</summary>
    public async Task UpdateTenantAsync(Guid tenantId, string name, string? yetkiliAd, string? eposta,
        string? telefon, string? notlar, string? plan, string operatorName, CancellationToken ct = default)
    {
        name = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new ValidationException("Firma adı zorunludur.");
        if (name.Length > 256) throw new ValidationException("Firma adı en çok 256 karakter olabilir.");
        if ((yetkiliAd ?? "").Length > 128) throw new ValidationException("Yetkili adı en çok 128 karakter olabilir.");
        if ((eposta ?? "").Length > 256) throw new ValidationException("E-posta en çok 256 karakter olabilir.");
        if ((telefon ?? "").Length > 32) throw new ValidationException("Telefon en çok 32 karakter olabilir.");
        if ((notlar ?? "").Length > 2000) throw new ValidationException("Notlar en çok 2000 karakter olabilir.");
        if ((plan ?? "").Length > 64) throw new ValidationException("Plan en çok 64 karakter olabilir.");

        await using var db = OwnerDb();
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw new ValidationException("Tenant bulunamadı.");
        tenant.Name = name;
        tenant.YetkiliAd = Bosalt(yetkiliAd);
        tenant.Eposta = Bosalt(eposta);
        tenant.Telefon = Bosalt(telefon);
        tenant.Notlar = Bosalt(notlar);
        tenant.Plan = Bosalt(plan);
        tenant.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        log.LogWarning("PLATFORM: tenant {Code} bilgileri güncellendi — operatör {Operator}.", tenant.Code, operatorName);

        static string? Bosalt(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    /// <summary>Tenant erişimini aç/kapa — PASİF geçici askıya alma (+ anlık kesme).
    /// KAPALI tenant buradan AÇILAMAZ (Yeniden Aç ayrı ve bilinçli işlemdir).</summary>
    public async Task SetActiveAsync(Guid tenantId, bool active, string operatorName, CancellationToken ct = default)
    {
        await using var db = OwnerDb();
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw new ValidationException("Tenant bulunamadı.");
        if (active && tenant.KapanisTarihiUtc is not null)
            throw new ValidationException("Kapalı firma 'Aktifleştir' ile açılamaz — 'Yeniden Aç' kullanın.");
        if (tenant.IsActive == active) return;
        tenant.IsActive = active;
        tenant.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        statusCache.Invalidate(tenantId); // anlık kesme (aynı-instance): açık oturum sonraki istekte düşer
        log.LogWarning("PLATFORM: tenant {Code} ({TenantId}) {Durum} — operatör {Operator}.",
            tenant.Code, tenantId, active ? "AÇILDI" : "PASİFLEŞTİRİLDİ", operatorName);
    }

    /// <summary>Firmayı KAPATIR: KapanisTarihiUtc damgası + IsActive=false BİRLİKTE (tek yaptırım yolu —
    /// LoginService/StatusCache/Middleware sıfır değişiklikle çalışır; kemer ayrıca KapanisTarihi'ni denetler).
    /// Veri SİLİNMEZ; yalnız ReopenAsync geri açar.</summary>
    public async Task CloseAsync(Guid tenantId, string operatorName, CancellationToken ct = default)
    {
        await using var db = OwnerDb();
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw new ValidationException("Tenant bulunamadı.");
        if (tenant.KapanisTarihiUtc is not null) return; // idempotent
        tenant.KapanisTarihiUtc = DateTimeOffset.UtcNow;
        tenant.IsActive = false;
        tenant.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        statusCache.Invalidate(tenantId);
        log.LogWarning("PLATFORM: tenant {Code} ({TenantId}) KAPATILDI (veri korunuyor) — operatör {Operator}.",
            tenant.Code, tenantId, operatorName);
    }

    /// <summary>Kapalı firmayı yeniden açar (Kapanis damgası + IsActive birlikte temizlenir/açılır).</summary>
    public async Task ReopenAsync(Guid tenantId, string operatorName, CancellationToken ct = default)
    {
        await using var db = OwnerDb();
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw new ValidationException("Tenant bulunamadı.");
        if (tenant.KapanisTarihiUtc is null) return; // idempotent
        tenant.KapanisTarihiUtc = null;
        tenant.IsActive = true;
        tenant.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        statusCache.Invalidate(tenantId);
        log.LogWarning("PLATFORM: tenant {Code} ({TenantId}) YENİDEN AÇILDI — operatör {Operator}.",
            tenant.Code, tenantId, operatorName);
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
