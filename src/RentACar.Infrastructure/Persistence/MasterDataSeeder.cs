using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// Tanım (master) tablolarına makul Türkçe VARSAYILANLARI seed eder — combobox'ların "seç" kısmı ilk andan
/// dolu gelsin (yeni tenant boş başlamasın). BranchBackfill deseni: owner/migrator conn + tenant-loop +
/// <c>set_config('app.tenant_id', …)</c> GUC + AÇIK TenantId (owner FORCE-RLS'i bypass etse de etmese de çalışır).
/// İDEMPOTENT per-tenant-per-kategori: yalnız o tenant'ta kategori BOŞSA doldurur (kullanıcının kendi tanımına/
/// sildiğine dokunmaz). Kod = Ad'ın Türkçe→ASCII-büyük-harf folding'i (`(TenantId,Kod)` UNIQUE için dedupe-guard'lı).
/// DbInitializer (her açılış, tüm tenant'lar) + PlatformAdminService.CreateTenantAsync (yeni tenant anında) çağırır.
/// </summary>
public static class MasterDataSeeder
{
    private static readonly string[] Brands =
        ["Toyota", "Renault", "Fiat", "Volkswagen", "Ford", "Hyundai", "Opel", "Peugeot", "Citroën", "Dacia",
         "Mercedes-Benz", "BMW", "Audi", "Honda", "Nissan", "Kia", "Škoda", "Seat", "Volvo", "Tesla"];
    private static readonly string[] Colors =
        ["Beyaz", "Siyah", "Gri", "Gümüş", "Kırmızı", "Mavi", "Lacivert", "Yeşil", "Kahverengi", "Bordo", "Turuncu", "Sarı"];
    private static readonly string[] Segments =
        ["Ekonomi", "Kompakt", "Orta", "Üst", "Lüks", "SUV", "Ticari", "Minivan"];
    private static readonly string[] VehicleTypes =
        ["Sedan", "Hatchback", "Station Wagon", "SUV", "Van", "Pick-up"];
    private static readonly string[] VehicleGroups =
        ["Ekonomi", "Orta", "Üst", "SUV", "Lüks", "Ticari"];
    private static readonly string[] Branches = ["Merkez"];
    private static readonly string[] Locations = ["Merkez Ofis"];
    private static readonly string[] PenaltyTypes =
        ["Hız Cezası", "Park Cezası", "Kırmızı Işık", "Emniyet Kemeri", "HGS İhlali", "Diğer"];
    private static readonly string[] PaymentTypes =
        ["Nakit", "Kredi Kartı", "Havale/EFT", "Çek", "Senet"];
    private static readonly string[] ExpenseTypes =
        ["Yakıt", "Bakım-Onarım", "Sigorta", "Lastik", "Temizlik", "Otopark", "Ceza", "Diğer"];
    private static readonly string[] Sources =
        ["Telefon", "Web", "Ofis", "Acente", "Kurumsal", "Yürüyen Müşteri"];
    private static readonly string[] InsuranceCompanies =
        ["Allianz", "AXA", "Anadolu Sigorta", "Aksigorta", "HDI Sigorta", "Mapfre", "Sompo", "Türkiye Sigorta"];
    private static readonly string[] CustomerGroups = ["Bireysel", "Kurumsal", "Acente", "VIP"];
    private static readonly string[] Countries =
        ["Türkiye", "Almanya", "İngiltere", "Fransa", "Hollanda", "ABD", "Rusya", "Diğer"];
    private static readonly string[] Departments = ["Operasyon", "Muhasebe", "Satış", "Yönetim"];

    /// <summary>Tüm tenant'ları dolaşıp idempotent seed eder (başlangıçta, owner conn).</summary>
    public static async Task RunAsync(AppDbContext db, ILogger? log = null, CancellationToken ct = default)
    {
        var tenantIds = await db.Tenants.AsNoTracking().Select(t => t.Id).ToListAsync(ct);
        if (tenantIds.Count == 0) return;

        await db.Database.OpenConnectionAsync(ct); // GUC bağlantı ömrünce açık kalmalı
        var total = 0;
        foreach (var tid in tenantIds)
            total += await SeedTenantCoreAsync(db, tid, ct);
        if (total > 0) log?.LogInformation("Master seed: {Count} varsayılan tanım eklendi ({N} tenant).", total, tenantIds.Count);
    }

    /// <summary>Tek tenant için seed (platform konsolu yeni tenant oluştururken).</summary>
    public static async Task<int> SeedTenantAsync(AppDbContext db, Guid tenantId, CancellationToken ct = default)
    {
        await db.Database.OpenConnectionAsync(ct);
        return await SeedTenantCoreAsync(db, tenantId, ct);
    }

    private static async Task<int> SeedTenantCoreAsync(AppDbContext db, Guid tid, CancellationToken ct)
    {
        // Tenant Tenants tablosundan gelen güvenilir Guid → literal (injection yok); GUC bu tenant'a sabitlenir.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.tenant_id', {tid.ToString()}, false)", ct);

        var n = 0;
        n += await SeedCat<Brand>(db, tid, Brands, (k, a) => new Brand { TenantId = tid, Kod = k, Ad = a }, ct);
        n += await SeedCat<VehicleColor>(db, tid, Colors, (k, a) => new VehicleColor { TenantId = tid, Kod = k, Ad = a }, ct);
        n += await SeedCat<VehicleSegment>(db, tid, Segments, (k, a) => new VehicleSegment { TenantId = tid, Kod = k, Ad = a }, ct);
        n += await SeedCat<VehicleType>(db, tid, VehicleTypes, (k, a) => new VehicleType { TenantId = tid, Kod = k, Ad = a }, ct);
        n += await SeedCat<VehicleGroup>(db, tid, VehicleGroups, (k, a) => new VehicleGroup { TenantId = tid, Kod = k, Ad = a }, ct);
        n += await SeedCat<Branch>(db, tid, Branches, (k, a) => new Branch { TenantId = tid, Kod = k, Ad = a }, ct);
        n += await SeedCat<Location>(db, tid, Locations, (k, a) => new Location { TenantId = tid, Kod = k, Ad = a }, ct);
        n += await SeedCat<PenaltyType>(db, tid, PenaltyTypes, (k, a) => new PenaltyType { TenantId = tid, Kod = k, Ad = a }, ct);
        n += await SeedCat<PaymentType>(db, tid, PaymentTypes, (k, a) => new PaymentType { TenantId = tid, Kod = k, Ad = a }, ct);
        n += await SeedCat<ExpenseCategory>(db, tid, ExpenseTypes, (k, a) => new ExpenseCategory { TenantId = tid, Kod = k, Ad = a }, ct);
        n += await SeedCat<ReservationSource>(db, tid, Sources, (k, a) => new ReservationSource { TenantId = tid, Kod = k, Ad = a }, ct);
        n += await SeedCat<InsuranceCompany>(db, tid, InsuranceCompanies, (k, a) => new InsuranceCompany { TenantId = tid, Kod = k, Ad = a }, ct);
        n += await SeedCat<CustomerGroup>(db, tid, CustomerGroups, (k, a) => new CustomerGroup { TenantId = tid, Kod = k, Ad = a }, ct);
        n += await SeedCat<Country>(db, tid, Countries, (k, a) => new Country { TenantId = tid, Kod = k, Ad = a }, ct);
        n += await SeedCat<Department>(db, tid, Departments, (k, a) => new Department { TenantId = tid, Kod = k, Ad = a }, ct);

        if (n > 0)
        {
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear(); // tenant-loop'ta tracker şişmesin
        }
        return n;
    }

    /// <summary>O tenant'ta T kategorisi BOŞSA varsayılanları ekler; doluysa (kullanıcı tanımı var) dokunmaz.</summary>
    private static async Task<int> SeedCat<T>(
        AppDbContext db, Guid tid, string[] names, Func<string, string, T> make, CancellationToken ct)
        where T : class, ITenantOwned
    {
        var isFilled = await db.Set<T>().IgnoreQueryFilters().Where(x => x.TenantId == tid).AnyAsync(ct);
        if (isFilled) return 0;

        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
            db.Set<T>().Add(make(UniqueCode(Fold(name), used), name));
        return names.Length;
    }

    /// <summary>Türkçe/Latin özel karakterleri ASCII'ye indirir, harf/rakam dışını atar, büyük harf yapar.</summary>
    private static string Fold(string name)
    {
        var pre = name.Trim()
            .Replace('ı', 'i').Replace('İ', 'i').Replace('ş', 's').Replace('Ş', 'S')
            .Replace('ğ', 'g').Replace('Ğ', 'G').Replace('ç', 'c').Replace('Ç', 'C')
            .Replace('ö', 'o').Replace('Ö', 'O').Replace('ü', 'u').Replace('Ü', 'U');
        var nfd = pre.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(nfd.Length);
        foreach (var ch in nfd)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue; // diakritik at
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToUpperInvariant(ch));
        }
        return sb.Length == 0 ? "KOD" : sb.ToString();
    }

    /// <summary>(TenantId,Kod) UNIQUE → aynı kategoride çakışan Kod'a sayısal suffix ekler; 32 kolon sınırı.</summary>
    private static string UniqueCode(string baseCode, HashSet<string> used)
    {
        var code = baseCode.Length > 32 ? baseCode[..32] : baseCode;
        if (used.Add(code)) return code;
        for (var i = 2; ; i++)
        {
            var g = baseCode.Length > 30 ? baseCode[..30] : baseCode;
            var k = g + i;
            if (used.Add(k)) return k;
        }
    }
}
