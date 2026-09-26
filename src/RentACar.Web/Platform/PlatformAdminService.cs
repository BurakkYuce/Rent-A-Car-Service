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
    int UserCount, int AracSayisi, int AktifKira, int ToplamKira, DateTimeOffset? SonGiris, decimal Gelir30Gun,
    bool PublicSiteEnabled, IReadOnlyList<PlatformTenantDomain> Domainler,
    /// <summary>PR-12: "Web Sitesi" modülü satın alındı mı (platform kararı).</summary>
    bool WebSitesiModulu = false,
    /// <summary>F4.6: yeni arayüz (Angular <c>/app</c>) pilotu açık mı (<c>TenantSettings.YeniArayuzPilot</c>).</summary>
    bool YeniArayuzPilot = false)
{
    public string Durum => KapanisTarihiUtc is not null ? "Kapalı" : IsActive ? "Aktif" : "Pasif";
}

/// <summary>PR-9: platform konsolunda salt-okunur host satırı (halka açık site görünürlüğü).</summary>
public sealed record PlatformTenantDomain(string Host, string Tur, string Durum);

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
    private static async Task<T> InTenantScope<T>(AppDbContext db, Guid tenantId, Func<Task<T>> query, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT set_config('app.tenant_id', {tenantId.ToString()}, true)", ct);
        var result = await query();
        await tx.CommitAsync(ct);
        return result;
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
        var lastLogin = await db.Users.AsNoTracking().IgnoreQueryFilters()
            .Where(u => u.LastLoginAtUtc != null)
            .GroupBy(u => u.TenantId)
            .Select(g => new { TenantId = g.Key, Son = g.Max(u => u.LastLoginAtUtc) })
            .ToDictionaryAsync(x => x.TenantId, x => x.Son, ct);
        // İş-tablosu sayımları FORCE-RLS ardında → tenant başına GUC'lu tx (konsolda az tenant; kabul).
        var rows = new List<PlatformTenantRow>(tenants.Count);
        foreach (var t in tenants)
        {
            var (vehicle, activeRental) = await InTenantScope(db, t.Id, async () => (
                await db.Vehicles.AsNoTracking().IgnoreQueryFilters().CountAsync(v => v.TenantId == t.Id, ct),
                await db.Rentals.AsNoTracking().IgnoreQueryFilters()
                    .CountAsync(r => r.TenantId == t.Id && r.Durum == RentalStatus.Kirada, ct)), ct);
            rows.Add(new PlatformTenantRow(t.Id, t.Code, t.Name, t.IsActive, t.KapanisTarihiUtc,
                t.CreatedAtUtc, counts.GetValueOrDefault(t.Id), vehicle, activeRental, lastLogin.GetValueOrDefault(t.Id)));
        }
        return rows;
    }

    /// <summary>F12.1: lightweight tenant options (platform table only — no per-tenant RLS round-trips).</summary>
    public sealed record TenantOption(Guid Id, string Code, string Name, bool IsActive, DateTimeOffset? KapanisTarihiUtc);

    /// <summary>F12.1: all tenants as selection options (UI API picker, document targets), ordered by code.</summary>
    public async Task<IReadOnlyList<TenantOption>> ListTenantOptionsAsync(CancellationToken ct = default)
    {
        await using var db = OwnerDb();
        return await db.Tenants.AsNoTracking().OrderBy(t => t.Code)
            .Select(t => new TenantOption(t.Id, t.Code, t.Name, t.IsActive, t.KapanisTarihiUtc))
            .ToListAsync(ct);
    }

    /// <summary>F12.1: does a tenant with this id exist (UI API 404 before any action).</summary>
    public async Task<bool> TenantExistsAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = OwnerDb();
        return await db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId, ct);
    }

    /// <summary>F12.1: is the code taken, case-INSENSITIVELY ("Demo" vs "demo" would be two login keys that differ
    /// only by case — confusing for users). The unique index still guards exact duplicates under a race.</summary>
    public async Task<bool> TenantCodeTakenAsync(string code, CancellationToken ct = default)
    {
        var lowered = (code ?? "").Trim().ToLowerInvariant();
        await using var db = OwnerDb();
        return await db.Tenants.AsNoTracking().AnyAsync(t => t.Code.ToLower() == lowered, ct);
    }

    /// <summary>Tenant detayı: bilgi alanları + metrikler. Gelir30Gun = son 30 gün defter Gelir
    /// (ΣCredit−ΣDebit base — GelirGider netleme aynası, iade düşer).</summary>
    public async Task<PlatformTenantDetay?> GetTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = OwnerDb();
        var t = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId, ct);
        if (t is null) return null;

        var userCount = await db.Users.AsNoTracking().IgnoreQueryFilters().CountAsync(u => u.TenantId == tenantId, ct);
        var lastLogin = await db.Users.AsNoTracking().IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId).MaxAsync(u => (DateTimeOffset?)u.LastLoginAtUtc, ct);

        // İş tabloları FORCE-RLS ardında → tenant-GUC'lu tx içinde okunur (owner'da BYPASSRLS yok).
        var threshold = DateTimeOffset.UtcNow.AddDays(-30);
        var (vehicleCount, totalRentals, activeRental, revenue30) = await InTenantScope(db, tenantId, async () =>
        {
            var vehicle = await db.Vehicles.AsNoTracking().IgnoreQueryFilters().CountAsync(v => v.TenantId == tenantId, ct);
            var total = await db.Rentals.AsNoTracking().IgnoreQueryFilters().CountAsync(r => r.TenantId == tenantId, ct);
            var active = await db.Rentals.AsNoTracking().IgnoreQueryFilters()
                .CountAsync(r => r.TenantId == tenantId && r.Durum == RentalStatus.Kirada, ct);
            var revenueRows = await db.AccountLedgerEntries.AsNoTracking().IgnoreQueryFilters()
                .Where(e => e.TenantId == tenantId && e.AccountType == LedgerAccountType.Gelir && e.EntryDateUtc >= threshold)
                .Select(e => new { e.Direction, e.Amount })
                .ToListAsync(ct);
            var revenue = revenueRows.Sum(e => e.Direction == LedgerDirection.Credit
                ? e.Amount.AmountInBase : -e.Amount.AmountInBase); // iade (Debit) netlenir
            return (arac: vehicle, toplam: total, aktif: active, gelir: revenue);
        }, ct);

        // PR-9 halka açık site görünürlüğü: TenantDomains PLATFORM tablosu (RLS yok) → owner doğrudan okur.
        // TenantSettings ise FORCE-RLS ardında → tenant-GUC'lu tx gerekir (yukarıdaki desen).
        var domains = await db.TenantDomains.AsNoTracking()
            .Where(d => d.TenantId == tenantId)
            .OrderBy(d => d.Kind).ThenBy(d => d.CreatedAtUtc)
            .Select(d => new { d.Host, d.Kind, d.Status })
            .ToListAsync(ct);
        var settings = await InTenantScope(db, tenantId, async () =>
            await db.TenantSettings.AsNoTracking().IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantId)
                .Select(x => new { x.PublicSiteEnabled, x.YeniArayuzPilot }).FirstOrDefaultAsync(ct), ct);
        var isSiteOpen = settings?.PublicSiteEnabled == true;

        return new PlatformTenantDetay(t.Id, t.Code, t.Name, t.IsActive, t.KapanisTarihiUtc,
            t.CreatedAtUtc, t.UpdatedAtUtc, t.YetkiliAd, t.Eposta, t.Telefon, t.Notlar, t.Plan,
            userCount, vehicleCount, activeRental, totalRentals, lastLogin, revenue30,
            isSiteOpen,
            domains.Select(d => new PlatformTenantDomain(d.Host,
                d.Kind == TenantDomainKind.Custom ? "Özel Domain" : "Alt Domain",
                d.Status switch
                {
                    TenantDomainStatus.Active => "Aktif",
                    TenantDomainStatus.PendingVerification => "Doğrulama Bekliyor",
                    TenantDomainStatus.Failed => "Başarısız",
                    _ => d.Status.ToString()
                })).ToList(),
            t.WebSitesiModulu, // PR-12
            settings?.YeniArayuzPilot == true); // F4.6
    }

    /// <summary>
    /// F12.1: optimistic-concurrency version of a tenant row for full-replace updates (<c>surum</c> in the UI API).
    /// Derived from the last write stamp (every platform write sets <c>UpdatedAtUtc</c>); both sides of the comparison
    /// are read from the DB (microsecond precision), so the round-trip is stable. String: ticks exceed JS safe integers.
    /// </summary>
    public static string TenantVersion(DateTimeOffset createdAtUtc, DateTimeOffset? updatedAtUtc)
        => (updatedAtUtc ?? createdAtUtc).UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// F12.1: audit row for a platform action in the tenant's OWN <c>AuditLogs</c> (the owner context has no audit
    /// interceptor). The caller must be inside <see cref="InTenantScope{T}"/> for the same tenant (FORCE-RLS
    /// WITH CHECK) and save in the same transaction as the change, so the action and its record commit together.
    /// </summary>
    private static void AddAudit(AppDbContext db, Guid tenantId, string entityName, string entityId, AuditAction action,
        string operatorName, object? oldValues, object? newValues)
        => db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            EntityName = entityName,
            EntityId = entityId,
            Action = action,
            UserName = "platform:" + operatorName,
            TimestampUtc = DateTimeOffset.UtcNow,
            OldValues = oldValues is null ? null : System.Text.Json.JsonSerializer.Serialize(oldValues),
            NewValues = newValues is null ? null : System.Text.Json.JsonSerializer.Serialize(newValues),
        });

    private static object StateSnapshot(Tenant t)
        => new { t.IsActive, Kapali = t.KapanisTarihiUtc is not null, t.WebSitesiModulu };

    /// <summary>Bilgi alanlarını günceller. Code DEĞİŞMEZ (login anahtarı). Kolon sınırları burada
    /// doğrulanır (L1 deseni: DbUpdateException→500 yerine anlamlı red). <paramref name="expectedVersion"/> verilirse
    /// (UI API tam değiştirme) satırın güncel <see cref="TenantVersion"/>'ı ile eşleşmeli; aksi halde
    /// <see cref="ConcurrentModificationException"/> (409 <c>cakisma</c>).</summary>
    public async Task UpdateTenantAsync(Guid tenantId, string name, string? authorizedName, string? email,
        string? phone, string? notes, string? plan, string operatorName, CancellationToken ct = default,
        string? expectedVersion = null)
    {
        name = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new ValidationException("Firma adı zorunludur.");
        if (name.Length > 256) throw new ValidationException("Firma adı en çok 256 karakter olabilir.");
        if ((authorizedName ?? "").Length > 128) throw new ValidationException("Yetkili adı en çok 128 karakter olabilir.");
        if ((email ?? "").Length > 256) throw new ValidationException("E-posta en çok 256 karakter olabilir.");
        if ((phone ?? "").Length > 32) throw new ValidationException("Telefon en çok 32 karakter olabilir.");
        if ((notes ?? "").Length > 2000) throw new ValidationException("Notlar en çok 2000 karakter olabilir.");
        if ((plan ?? "").Length > 64) throw new ValidationException("Plan en çok 64 karakter olabilir.");

        await using var db = OwnerDb();
        var tenant = await InTenantScope(db, tenantId, async () =>
        {
            var t = await LockTenantAsync(db, tenantId, ct);
            // Checked under the row lock: two operators saving the same version cannot both win.
            if (expectedVersion is not null && expectedVersion != TenantVersion(t.CreatedAtUtc, t.UpdatedAtUtc))
                throw new ConcurrentModificationException(ConcurrentModificationException.RecordMessage);
            var before = new { t.Name, t.YetkiliAd, t.Eposta, t.Telefon, t.Notlar, t.Plan };
            t.Name = name;
            t.YetkiliAd = Drain(authorizedName);
            t.Eposta = Drain(email);
            t.Telefon = Drain(phone);
            t.Notlar = Drain(notes);
            t.Plan = Drain(plan);
            t.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AddAudit(db, tenantId, nameof(Tenant), tenantId.ToString(), AuditAction.Update, operatorName, before,
                new { t.Name, t.YetkiliAd, t.Eposta, t.Telefon, t.Notlar, t.Plan });
            await db.SaveChangesAsync(ct);
            return t;
        }, ct);
        log.LogWarning("PLATFORM: tenant {Code} bilgileri güncellendi — operatör {Operator}.", tenant.Code, operatorName);

        static string? Drain(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    /// <summary>F12.1: tenant row under <c>FOR UPDATE</c> (call inside a transaction), tracked for the write.</summary>
    private static async Task<Tenant> LockTenantAsync(AppDbContext db, Guid tenantId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Tenants\" WHERE \"Id\" = {tenantId} FOR UPDATE", ct);
        return await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw new ValidationException("Tenant bulunamadı.");
    }

    // ---- PR-B: Belge Merkezi (platform → tenant PDF dağıtımı) ----

    /// <summary>Platform ekranı satırı — <b>PDF içeriği YOK</b> (blob liste sorgusuna girmez).</summary>
    public sealed record PlatformBelgeSatiri(
        Guid Id, string Baslik, string? Aciklama, string DosyaAdi, long Boyut, int Surum,
        PlatformBelgeDurum Durum, bool YalnizYoneticiler, DateTimeOffset GuncellemeUtc,
        string? YukleyenOperator, IReadOnlyList<string> HedefKodlar);

    /// <summary>Tüm belgeler (taslak/arşiv dahil) + hedef tenant kodları. Yalnız platform konsolu.</summary>
    public async Task<IReadOnlyList<PlatformBelgeSatiri>> ListDocumentsAsync(CancellationToken ct = default)
    {
        await using var db = OwnerDb();
        var documents = await db.PlatformBelgeler.AsNoTracking()
            .OrderByDescending(b => b.GuncellemeUtc)
            .Select(b => new
            {
                b.Id, b.Baslik, b.Aciklama, b.DosyaAdi, b.Boyut, b.Surum, b.Durum,
                b.YalnizYoneticiler, b.GuncellemeUtc, b.YukleyenOperator,
            })
            .ToListAsync(ct);
        if (documents.Count == 0) return [];

        // Hedefleri TEK sorguda çek (belge başına sorgu N+1 üretirdi).
        var ids = documents.Select(b => b.Id).ToList();
        var targets = await db.PlatformBelgeHedefler.AsNoTracking()
            .Where(h => ids.Contains(h.BelgeId))
            .Join(db.Tenants.AsNoTracking(), h => h.TenantId, t => t.Id, (h, t) => new { h.BelgeId, t.Code })
            .ToListAsync(ct);

        return [.. documents.Select(b => new PlatformBelgeSatiri(
            b.Id, b.Baslik, b.Aciklama, b.DosyaAdi, b.Boyut, b.Surum, b.Durum,
            b.YalnizYoneticiler, b.GuncellemeUtc, b.YukleyenOperator,
            [.. targets.Where(h => h.BelgeId == b.Id).Select(h => h.Code).OrderBy(c => c)]))];
    }

    /// <summary>
    /// Yeni belge yükler — <b>TASLAK</b> olarak (tenant görmez). "Yükle → kontrol et → yayınla"
    /// akışı bilinçli: aksi halde her yükleme anında tüm müşterilerin ekranına çıkardı.
    /// </summary>
    public async Task<Guid> UploadDocumentAsync(string title, string? description, string fileName, byte[] bytes,
        IReadOnlyList<Guid> targetTenantIds, bool managersOnly, string operatorName, CancellationToken ct = default)
    {
        title = (title ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title)) throw new ValidationException("Belge başlığı zorunludur.");
        if (title.Length > 200) throw new ValidationException("Başlık en çok 200 karakter olabilir.");
        if (PdfValidation.Reject(bytes) is { } error) throw new ValidationException(error);

        await using var db = OwnerDb();
        var document = new PlatformBelge
        {
            Baslik = title,
            Aciklama = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            DosyaAdi = PdfValidation.SafeFileName(fileName),
            Bytes = bytes,
            Boyut = bytes.Length,
            Durum = PlatformBelgeDurum.Taslak,
            YalnizYoneticiler = managersOnly,
            YukleyenOperator = operatorName,
        };
        db.PlatformBelgeler.Add(document);
        foreach (var tid in targetTenantIds.Distinct())
            db.PlatformBelgeHedefler.Add(new PlatformBelgeHedef { BelgeId = document.Id, TenantId = tid });
        await db.SaveChangesAsync(ct);

        log.LogWarning("PLATFORM: belge '{Baslik}' yüklendi ({Bayt} bayt, {Hedef}) — operatör {Operator}.",
            title, bytes.Length, targetTenantIds.Count == 0 ? "GLOBAL" : $"{targetTenantIds.Count} tenant", operatorName);
        return document.Id;
    }

    /// <summary>
    /// Dosyayı DEĞİŞTİRİR: yeni kayıt açılmaz, <c>Surum</c> artar, tarih yenilenir. Böylece
    /// tenant'ın elindeki link kırılmaz ve "v2 · 3 gün önce güncellendi" gösterilebilir.
    /// </summary>
    public async Task UpdateDocumentVersionAsync(Guid documentId, string fileName, byte[] bytes,
        string operatorName, CancellationToken ct = default)
    {
        if (PdfValidation.Reject(bytes) is { } error) throw new ValidationException(error);

        await using var db = OwnerDb();
        var document = await db.PlatformBelgeler.FirstOrDefaultAsync(b => b.Id == documentId, ct)
            ?? throw new ValidationException("Belge bulunamadı.");
        document.Bytes = bytes;
        document.Boyut = bytes.Length;
        document.DosyaAdi = PdfValidation.SafeFileName(fileName);
        document.Surum++;
        document.GuncellemeUtc = DateTimeOffset.UtcNow;
        document.YukleyenOperator = operatorName;
        await db.SaveChangesAsync(ct);

        log.LogWarning("PLATFORM: belge '{Baslik}' v{Surum}'e güncellendi — operatör {Operator}.",
            document.Baslik, document.Surum, operatorName);
    }

    public async Task DocumentStatusAsync(Guid documentId, PlatformBelgeDurum status, string operatorName, CancellationToken ct = default)
    {
        await using var db = OwnerDb();
        var document = await db.PlatformBelgeler.FirstOrDefaultAsync(b => b.Id == documentId, ct)
            ?? throw new ValidationException("Belge bulunamadı.");
        document.Durum = status;
        document.GuncellemeUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        log.LogWarning("PLATFORM: belge '{Baslik}' durumu {Durum} — operatör {Operator}.", document.Baslik, status, operatorName);
    }

    /// <summary>Belgeyi tamamen siler (hedefleri cascade düşer). Arşiv yeterli olmadığında —
    /// ör. yanlış dosya yüklendiyse.</summary>
    public async Task DeleteDocumentAsync(Guid documentId, string operatorName, CancellationToken ct = default)
    {
        await using var db = OwnerDb();
        var document = await db.PlatformBelgeler.FirstOrDefaultAsync(b => b.Id == documentId, ct)
            ?? throw new ValidationException("Belge bulunamadı.");
        db.PlatformBelgeler.Remove(document);
        await db.SaveChangesAsync(ct);
        log.LogWarning("PLATFORM: belge '{Baslik}' SİLİNDİ — operatör {Operator}.", document.Baslik, operatorName);
    }

    /// <summary>Platform ekranında belgeyi önizlemek/indirmek için (platform operatörü — hedef
    /// filtresi UYGULANMAZ, burada zaten her belgeyi görme yetkisi var).</summary>
    public async Task<(byte[] Bytes, string DosyaAdi)?> DocumentContentAsync(Guid documentId, CancellationToken ct = default)
    {
        await using var db = OwnerDb();
        var row = await db.PlatformBelgeler.AsNoTracking()
            .Where(b => b.Id == documentId)
            .Select(b => new { b.Bytes, b.DosyaAdi })
            .FirstOrDefaultAsync(ct);
        return row is null ? null : (row.Bytes, row.DosyaAdi);
    }

    /// <summary>
    /// PR-A: tenant'ın PDF logosunu platform konsolundan yükle/kaldır. Tenant kendi
    /// <c>/ayarlar</c> yolundan da yükleyebilir — bu EK kanal, ikame değil (tek alan, son yazan kazanır).
    ///
    /// <para><c>Ayarlar</c> tenant-owned ve FORCE-RLS → owner bağlantısı tek başına yetmez, GUC şart:
    /// <see cref="InTenantScope"/> helper'ı (tx-yerel <c>set_config</c>) kullanılır. Bu helper bugüne
    /// kadar yalnız OKUMA için kullanılıyordu; yazma da aynı tx içinde güvenli.</para>
    ///
    /// <para>Doğrulama <see cref="LogoValidationRules.Reject"/> ile — tenant yoluyla AYNI kural (iki panel
    /// farklı davranmasın).</para>
    /// </summary>
    public async Task SetTenantLogoAsync(Guid tenantId, byte[]? png, string operatorName, CancellationToken ct = default)
    {
        if (png is { Length: > 0 } filled && LogoValidationRules.Reject(filled) is { } error)
            throw new ValidationException(error);

        await using var db = OwnerDb();
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw new ValidationException("Tenant bulunamadı.");

        await InTenantScope(db, tenantId, async () =>
        {
            var setting = await db.TenantSettings.IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
            if (setting is null)
            {
                // Tenant hiç Ayarlar satırı açmamış olabilir (yeni firma) → upsert.
                setting = new RentACar.Domain.Entities.TenantSettings { TenantId = tenantId };
                db.TenantSettings.Add(setting);
            }
            var hadLogo = setting.LogoBytes is { Length: > 0 };
            setting.LogoBytes = png is { Length: > 0 } ? png : null;
            setting.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AddAudit(db, tenantId, nameof(RentACar.Domain.Entities.TenantSettings), setting.Id.ToString(), AuditAction.Update,
                operatorName, new { Logo = hadLogo }, new { Logo = png is { Length: > 0 }, LogoBayt = png?.Length ?? 0 });
            await db.SaveChangesAsync(ct);
            return 0;
        }, ct);

        log.LogWarning("PLATFORM: tenant {Code} ({TenantId}) PDF logosu {Durum} — operatör {Operator}.",
            tenant.Code, tenantId, png is { Length: > 0 } ? $"GÜNCELLENDİ ({png.Length} bayt)" : "KALDIRILDI", operatorName);
    }

    /// <summary>Platform ekranında logoyu göstermek + değerlendirmesini basmak için.</summary>
    public async Task<(byte[]? Bytes, LogoDegerlendirme? Degerlendirme)> GetTenantLogoAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        await using var db = OwnerDb();
        var bytes = await InTenantScope(db, tenantId, async () =>
            await db.TenantSettings.AsNoTracking().IgnoreQueryFilters()
                .Where(s => s.TenantId == tenantId).Select(s => s.LogoBytes).FirstOrDefaultAsync(ct), ct);
        return bytes is { Length: > 0 }
            ? (bytes, LogoValidationRules.Evaluate(bytes))
            : (null, null);
    }

    /// <summary>
    /// PR-12: "Web Sitesi" modülünü aç/kapa. SATIN ALMA kararıdır → yalnız platform konsolundan;
    /// tenant'ın ERP'sinde bu alanı yazan hiçbir yol YOKTUR (bilinçli — <c>TenantSettings</c>
    /// `ManageUsers` ile müşteriye açıktır, oraya konsaydı müşteri kendi kendine açardı).
    /// Kapatmak veriyi SİLMEZ: mevcut ilanlar durur, yalnız erişim ve halka açık site kapanır.
    /// </summary>
    public async Task SetWebsiteModuleAsync(Guid tenantId, bool active, string operatorName, CancellationToken ct = default)
    {
        await using var db = OwnerDb();
        var tenant = await InTenantScope(db, tenantId, async () =>
        {
            var t = await LockTenantAsync(db, tenantId, ct);
            if (t.WebSitesiModulu == active) return t; // no-op (no audit row for a non-change)
            var before = new { t.WebSitesiModulu };
            t.WebSitesiModulu = active;
            t.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AddAudit(db, tenantId, nameof(Tenant), tenantId.ToString(), AuditAction.Update, operatorName,
                before, new { t.WebSitesiModulu });
            await db.SaveChangesAsync(ct);
            return t;
        }, ct);

        // Invalidate ŞART: yoksa "modülü açtım, menü gelmedi" (TTL kadar sessizlik).
        statusCache.Invalidate(tenantId);
        log.LogWarning("PLATFORM: tenant {Code} ({TenantId}) Web Sitesi modülü {Durum} — operatör {Operator}.",
            tenant.Code, tenantId, active ? "AÇILDI" : "KAPATILDI", operatorName);
    }

    // F13.1b: yeni arayüz PİLOT anahtarı (SetNewUiPilotAsync) kaldırıldı — pilot kapısı yok, yeni arayüz herkes için.
    // TenantSettings.YeniArayuzPilot kolonu kullanılmıyor (ayrı migration'la düşer); detayda salt okunur gösterilir.

    /// <summary>Tenant erişimini aç/kapa — PASİF geçici askıya alma (+ anlık kesme).
    /// KAPALI tenant buradan AÇILAMAZ (Yeniden Aç ayrı ve bilinçli işlemdir).</summary>
    public async Task SetActiveAsync(Guid tenantId, bool active, string operatorName, CancellationToken ct = default)
    {
        await using var db = OwnerDb();
        var (tenant, changed) = await InTenantScope(db, tenantId, async () =>
        {
            var t = await LockTenantAsync(db, tenantId, ct);
            if (active && t.KapanisTarihiUtc is not null)
                throw new ValidationException("Kapalı firma 'Aktifleştir' ile açılamaz — 'Yeniden Aç' kullanın.");
            if (t.IsActive == active) return (t, false);
            var before = StateSnapshot(t);
            t.IsActive = active;
            t.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AddAudit(db, tenantId, nameof(Tenant), tenantId.ToString(), AuditAction.Update, operatorName, before, StateSnapshot(t));
            await db.SaveChangesAsync(ct);
            return (t, true);
        }, ct);
        if (!changed) return;
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
        var (tenant, changed) = await InTenantScope(db, tenantId, async () =>
        {
            var t = await LockTenantAsync(db, tenantId, ct);
            if (t.KapanisTarihiUtc is not null) return (t, false); // idempotent
            var before = StateSnapshot(t);
            t.KapanisTarihiUtc = DateTimeOffset.UtcNow;
            t.IsActive = false;
            t.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AddAudit(db, tenantId, nameof(Tenant), tenantId.ToString(), AuditAction.Update, operatorName, before, StateSnapshot(t));
            await db.SaveChangesAsync(ct);
            return (t, true);
        }, ct);
        if (!changed) return;
        statusCache.Invalidate(tenantId);
        log.LogWarning("PLATFORM: tenant {Code} ({TenantId}) KAPATILDI (veri korunuyor) — operatör {Operator}.",
            tenant.Code, tenantId, operatorName);
    }

    /// <summary>Kapalı firmayı yeniden açar (Kapanis damgası + IsActive birlikte temizlenir/açılır).</summary>
    public async Task ReopenAsync(Guid tenantId, string operatorName, CancellationToken ct = default)
    {
        await using var db = OwnerDb();
        var (tenant, changed) = await InTenantScope(db, tenantId, async () =>
        {
            var t = await LockTenantAsync(db, tenantId, ct);
            if (t.KapanisTarihiUtc is null) return (t, false); // idempotent
            var before = StateSnapshot(t);
            t.KapanisTarihiUtc = null;
            t.IsActive = true;
            t.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AddAudit(db, tenantId, nameof(Tenant), tenantId.ToString(), AuditAction.Update, operatorName, before, StateSnapshot(t));
            await db.SaveChangesAsync(ct);
            return (t, true);
        }, ct);
        if (!changed) return;
        statusCache.Invalidate(tenantId);
        log.LogWarning("PLATFORM: tenant {Code} ({TenantId}) YENİDEN AÇILDI — operatör {Operator}.",
            tenant.Code, tenantId, operatorName);
    }

    /// <summary>Yeni tenant + ilk admin kullanıcı oluştur. Yeni firmanın kimliğini döner (F12.1: UI API 201).</summary>
    public async Task<Guid> CreateTenantAsync(
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
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // Race between the AnyAsync pre-check and the insert: the unique index is the real guard.
            throw new ValidationException($"'{code}' kodlu firma zaten var.");
        }

        // Tanım varsayılanları (marka/renk/segment/ceza türü…) — yeni tenant restart beklemeden dolu başlasın.
        await MasterDataSeeder.SeedTenantAsync(db, tenant.Id, ct);

        // F12.1: creation lands in the new tenant's own audit trail (password/hash NEVER recorded).
        await InTenantScope(db, tenant.Id, async () =>
        {
            AddAudit(db, tenant.Id, nameof(Tenant), tenant.Id.ToString(), AuditAction.Create, operatorName, null,
                new { tenant.Code, tenant.Name, AdminKullanici = adminUser });
            await db.SaveChangesAsync(ct);
            return 0;
        }, ct);

        log.LogWarning("PLATFORM: yeni tenant {Code} + admin {Admin} oluşturuldu — operatör {Operator}.",
            code, adminUser, operatorName);
        return tenant.Id;
    }
}
