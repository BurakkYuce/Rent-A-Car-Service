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

        // PR-9 halka açık site görünürlüğü: TenantDomains PLATFORM tablosu (RLS yok) → owner doğrudan okur.
        // TenantSettings ise FORCE-RLS ardında → tenant-GUC'lu tx gerekir (yukarıdaki desen).
        var domainler = await db.TenantDomains.AsNoTracking()
            .Where(d => d.TenantId == tenantId)
            .OrderBy(d => d.Kind).ThenBy(d => d.CreatedAtUtc)
            .Select(d => new { d.Host, d.Kind, d.Status })
            .ToListAsync(ct);
        var ayarlar = await TenantKapsaminda(db, tenantId, async () =>
            await db.TenantSettings.AsNoTracking().IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantId)
                .Select(x => new { x.PublicSiteEnabled, x.YeniArayuzPilot }).FirstOrDefaultAsync(ct), ct);
        var siteAcik = ayarlar?.PublicSiteEnabled == true;

        return new PlatformTenantDetay(t.Id, t.Code, t.Name, t.IsActive, t.KapanisTarihiUtc,
            t.CreatedAtUtc, t.UpdatedAtUtc, t.YetkiliAd, t.Eposta, t.Telefon, t.Notlar, t.Plan,
            userCount, aracSayisi, aktifKira, toplamKira, sonGiris, gelir30,
            siteAcik,
            domainler.Select(d => new PlatformTenantDomain(d.Host,
                d.Kind == TenantDomainKind.Custom ? "Özel Domain" : "Alt Domain",
                d.Status switch
                {
                    TenantDomainStatus.Active => "Aktif",
                    TenantDomainStatus.PendingVerification => "Doğrulama Bekliyor",
                    TenantDomainStatus.Failed => "Başarısız",
                    _ => d.Status.ToString()
                })).ToList(),
            t.WebSitesiModulu, // PR-12
            ayarlar?.YeniArayuzPilot == true); // F4.6
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

    // ---- PR-B: Belge Merkezi (platform → tenant PDF dağıtımı) ----

    /// <summary>Platform ekranı satırı — <b>PDF içeriği YOK</b> (blob liste sorgusuna girmez).</summary>
    public sealed record PlatformBelgeSatiri(
        Guid Id, string Baslik, string? Aciklama, string DosyaAdi, long Boyut, int Surum,
        PlatformBelgeDurum Durum, bool YalnizYoneticiler, DateTimeOffset GuncellemeUtc,
        string? YukleyenOperator, IReadOnlyList<string> HedefKodlar);

    /// <summary>Tüm belgeler (taslak/arşiv dahil) + hedef tenant kodları. Yalnız platform konsolu.</summary>
    public async Task<IReadOnlyList<PlatformBelgeSatiri>> ListBelgelerAsync(CancellationToken ct = default)
    {
        await using var db = OwnerDb();
        var belgeler = await db.PlatformBelgeler.AsNoTracking()
            .OrderByDescending(b => b.GuncellemeUtc)
            .Select(b => new
            {
                b.Id, b.Baslik, b.Aciklama, b.DosyaAdi, b.Boyut, b.Surum, b.Durum,
                b.YalnizYoneticiler, b.GuncellemeUtc, b.YukleyenOperator,
            })
            .ToListAsync(ct);
        if (belgeler.Count == 0) return [];

        // Hedefleri TEK sorguda çek (belge başına sorgu N+1 üretirdi).
        var idler = belgeler.Select(b => b.Id).ToList();
        var hedefler = await db.PlatformBelgeHedefler.AsNoTracking()
            .Where(h => idler.Contains(h.BelgeId))
            .Join(db.Tenants.AsNoTracking(), h => h.TenantId, t => t.Id, (h, t) => new { h.BelgeId, t.Code })
            .ToListAsync(ct);

        return [.. belgeler.Select(b => new PlatformBelgeSatiri(
            b.Id, b.Baslik, b.Aciklama, b.DosyaAdi, b.Boyut, b.Surum, b.Durum,
            b.YalnizYoneticiler, b.GuncellemeUtc, b.YukleyenOperator,
            [.. hedefler.Where(h => h.BelgeId == b.Id).Select(h => h.Code).OrderBy(c => c)]))];
    }

    /// <summary>
    /// Yeni belge yükler — <b>TASLAK</b> olarak (tenant görmez). "Yükle → kontrol et → yayınla"
    /// akışı bilinçli: aksi halde her yükleme anında tüm müşterilerin ekranına çıkardı.
    /// </summary>
    public async Task<Guid> BelgeYukleAsync(string baslik, string? aciklama, string dosyaAdi, byte[] bytes,
        IReadOnlyList<Guid> hedefTenantIdler, bool yalnizYoneticiler, string operatorName, CancellationToken ct = default)
    {
        baslik = (baslik ?? "").Trim();
        if (string.IsNullOrWhiteSpace(baslik)) throw new ValidationException("Belge başlığı zorunludur.");
        if (baslik.Length > 200) throw new ValidationException("Başlık en çok 200 karakter olabilir.");
        if (PdfValidation.Reddet(bytes) is { } hata) throw new ValidationException(hata);

        await using var db = OwnerDb();
        var belge = new PlatformBelge
        {
            Baslik = baslik,
            Aciklama = string.IsNullOrWhiteSpace(aciklama) ? null : aciklama.Trim(),
            DosyaAdi = PdfValidation.GuvenliDosyaAdi(dosyaAdi),
            Bytes = bytes,
            Boyut = bytes.Length,
            Durum = PlatformBelgeDurum.Taslak,
            YalnizYoneticiler = yalnizYoneticiler,
            YukleyenOperator = operatorName,
        };
        db.PlatformBelgeler.Add(belge);
        foreach (var tid in hedefTenantIdler.Distinct())
            db.PlatformBelgeHedefler.Add(new PlatformBelgeHedef { BelgeId = belge.Id, TenantId = tid });
        await db.SaveChangesAsync(ct);

        log.LogWarning("PLATFORM: belge '{Baslik}' yüklendi ({Bayt} bayt, {Hedef}) — operatör {Operator}.",
            baslik, bytes.Length, hedefTenantIdler.Count == 0 ? "GLOBAL" : $"{hedefTenantIdler.Count} tenant", operatorName);
        return belge.Id;
    }

    /// <summary>
    /// Dosyayı DEĞİŞTİRİR: yeni kayıt açılmaz, <c>Surum</c> artar, tarih yenilenir. Böylece
    /// tenant'ın elindeki link kırılmaz ve "v2 · 3 gün önce güncellendi" gösterilebilir.
    /// </summary>
    public async Task BelgeSurumGuncelleAsync(Guid belgeId, string dosyaAdi, byte[] bytes,
        string operatorName, CancellationToken ct = default)
    {
        if (PdfValidation.Reddet(bytes) is { } hata) throw new ValidationException(hata);

        await using var db = OwnerDb();
        var belge = await db.PlatformBelgeler.FirstOrDefaultAsync(b => b.Id == belgeId, ct)
            ?? throw new ValidationException("Belge bulunamadı.");
        belge.Bytes = bytes;
        belge.Boyut = bytes.Length;
        belge.DosyaAdi = PdfValidation.GuvenliDosyaAdi(dosyaAdi);
        belge.Surum++;
        belge.GuncellemeUtc = DateTimeOffset.UtcNow;
        belge.YukleyenOperator = operatorName;
        await db.SaveChangesAsync(ct);

        log.LogWarning("PLATFORM: belge '{Baslik}' v{Surum}'e güncellendi — operatör {Operator}.",
            belge.Baslik, belge.Surum, operatorName);
    }

    public async Task BelgeDurumAsync(Guid belgeId, PlatformBelgeDurum durum, string operatorName, CancellationToken ct = default)
    {
        await using var db = OwnerDb();
        var belge = await db.PlatformBelgeler.FirstOrDefaultAsync(b => b.Id == belgeId, ct)
            ?? throw new ValidationException("Belge bulunamadı.");
        belge.Durum = durum;
        belge.GuncellemeUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        log.LogWarning("PLATFORM: belge '{Baslik}' durumu {Durum} — operatör {Operator}.", belge.Baslik, durum, operatorName);
    }

    /// <summary>Belgeyi tamamen siler (hedefleri cascade düşer). Arşiv yeterli olmadığında —
    /// ör. yanlış dosya yüklendiyse.</summary>
    public async Task BelgeSilAsync(Guid belgeId, string operatorName, CancellationToken ct = default)
    {
        await using var db = OwnerDb();
        var belge = await db.PlatformBelgeler.FirstOrDefaultAsync(b => b.Id == belgeId, ct)
            ?? throw new ValidationException("Belge bulunamadı.");
        db.PlatformBelgeler.Remove(belge);
        await db.SaveChangesAsync(ct);
        log.LogWarning("PLATFORM: belge '{Baslik}' SİLİNDİ — operatör {Operator}.", belge.Baslik, operatorName);
    }

    /// <summary>Platform ekranında belgeyi önizlemek/indirmek için (platform operatörü — hedef
    /// filtresi UYGULANMAZ, burada zaten her belgeyi görme yetkisi var).</summary>
    public async Task<(byte[] Bytes, string DosyaAdi)?> BelgeIcerikAsync(Guid belgeId, CancellationToken ct = default)
    {
        await using var db = OwnerDb();
        var satir = await db.PlatformBelgeler.AsNoTracking()
            .Where(b => b.Id == belgeId)
            .Select(b => new { b.Bytes, b.DosyaAdi })
            .FirstOrDefaultAsync(ct);
        return satir is null ? null : (satir.Bytes, satir.DosyaAdi);
    }

    /// <summary>
    /// PR-A: tenant'ın PDF logosunu platform konsolundan yükle/kaldır. Tenant kendi
    /// <c>/ayarlar</c> yolundan da yükleyebilir — bu EK kanal, ikame değil (tek alan, son yazan kazanır).
    ///
    /// <para><c>Ayarlar</c> tenant-owned ve FORCE-RLS → owner bağlantısı tek başına yetmez, GUC şart:
    /// <see cref="TenantKapsaminda"/> helper'ı (tx-yerel <c>set_config</c>) kullanılır. Bu helper bugüne
    /// kadar yalnız OKUMA için kullanılıyordu; yazma da aynı tx içinde güvenli.</para>
    ///
    /// <para>Doğrulama <see cref="LogoKurallari.Reddet"/> ile — tenant yoluyla AYNI kural (iki panel
    /// farklı davranmasın).</para>
    /// </summary>
    public async Task SetTenantLogoAsync(Guid tenantId, byte[]? png, string operatorName, CancellationToken ct = default)
    {
        if (png is { Length: > 0 } dolu && LogoKurallari.Reddet(dolu) is { } hata)
            throw new ValidationException(hata);

        await using var db = OwnerDb();
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw new ValidationException("Tenant bulunamadı.");

        await TenantKapsaminda(db, tenantId, async () =>
        {
            var ayar = await db.TenantSettings.IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
            if (ayar is null)
            {
                // Tenant hiç Ayarlar satırı açmamış olabilir (yeni firma) → upsert.
                ayar = new RentACar.Domain.Entities.TenantSettings { TenantId = tenantId };
                db.TenantSettings.Add(ayar);
            }
            ayar.LogoBytes = png is { Length: > 0 } ? png : null;
            ayar.UpdatedAtUtc = DateTimeOffset.UtcNow;
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
        var bytes = await TenantKapsaminda(db, tenantId, async () =>
            await db.TenantSettings.AsNoTracking().IgnoreQueryFilters()
                .Where(s => s.TenantId == tenantId).Select(s => s.LogoBytes).FirstOrDefaultAsync(ct), ct);
        return bytes is { Length: > 0 }
            ? (bytes, LogoKurallari.Degerlendir(bytes))
            : (null, null);
    }

    /// <summary>
    /// PR-12: "Web Sitesi" modülünü aç/kapa. SATIN ALMA kararıdır → yalnız platform konsolundan;
    /// tenant'ın ERP'sinde bu alanı yazan hiçbir yol YOKTUR (bilinçli — <c>TenantSettings</c>
    /// `ManageUsers` ile müşteriye açıktır, oraya konsaydı müşteri kendi kendine açardı).
    /// Kapatmak veriyi SİLMEZ: mevcut ilanlar durur, yalnız erişim ve halka açık site kapanır.
    /// </summary>
    public async Task SetWebSitesiModuluAsync(Guid tenantId, bool aktif, string operatorName, CancellationToken ct = default)
    {
        await using var db = OwnerDb();
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw new ValidationException("Tenant bulunamadı.");
        tenant.WebSitesiModulu = aktif;
        tenant.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        // Invalidate ŞART: yoksa "modülü açtım, menü gelmedi" (TTL kadar sessizlik).
        statusCache.Invalidate(tenantId);
        log.LogWarning("PLATFORM: tenant {Code} ({TenantId}) Web Sitesi modülü {Durum} — operatör {Operator}.",
            tenant.Code, tenantId, aktif ? "AÇILDI" : "KAPATILDI", operatorName);
    }

    /// <summary>
    /// F4.6: yeni arayüz (Angular <c>/app</c>) PİLOT anahtarı. Açıkken firmanın <c>/api/ui/v1</c> uçları çalışır ve
    /// Blazor Panel/Kira sayfaları (GET) <c>/app</c>'e yönlenir; kapatınca ANINDA eski arayüz (bayrak önbelleksiz okunur).
    /// Yalnız platform konsolundan: <c>TenantSettings</c> ekranı bu alanı yazmaz (firma kendi kendine pilota giremez).
    /// Ayar satırı yoksa (yeni firma) oluşturulur. Denetim: firmanın <c>AuditLogs</c>'una açık satır (owner bağlamında
    /// denetim interceptor'ı yok) + platform uyarı logu. Aynı değere yazmak no-op (denetim satırı da yazılmaz).
    /// </summary>
    public async Task SetYeniArayuzPilotAsync(Guid tenantId, bool aktif, string operatorName, CancellationToken ct = default)
    {
        await using var db = OwnerDb();
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw new ValidationException("Tenant bulunamadı.");

        var degisti = await TenantKapsaminda(db, tenantId, async () =>
        {
            var ayar = await db.TenantSettings.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
            if (ayar is null)
            {
                if (!aktif) return false; // satır yok = pilot değil; kapatmak için satır açmaya gerek yok
                ayar = new RentACar.Domain.Entities.TenantSettings { TenantId = tenantId };
                db.TenantSettings.Add(ayar);
            }
            else if (ayar.YeniArayuzPilot == aktif) return false;

            var eski = ayar.YeniArayuzPilot;
            ayar.YeniArayuzPilot = aktif;
            ayar.UpdatedAtUtc = DateTimeOffset.UtcNow;
            db.AuditLogs.Add(new AuditLog
            {
                TenantId = tenantId,
                EntityName = nameof(RentACar.Domain.Entities.TenantSettings),
                EntityId = ayar.Id.ToString(),
                Action = AuditAction.Update,
                UserName = "platform:" + operatorName,
                TimestampUtc = DateTimeOffset.UtcNow,
                OldValues = $"{{\"YeniArayuzPilot\":{(eski ? "true" : "false")}}}",
                NewValues = $"{{\"YeniArayuzPilot\":{(aktif ? "true" : "false")}}}",
            });
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);

        if (degisti)
            log.LogWarning("PLATFORM: tenant {Code} ({TenantId}) yeni arayüz pilotu {Durum} — operatör {Operator}.",
                tenant.Code, tenantId, aktif ? "AÇILDI" : "KAPATILDI", operatorName);
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
