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
    private static readonly string[] Markalar =
        ["Toyota", "Renault", "Fiat", "Volkswagen", "Ford", "Hyundai", "Opel", "Peugeot", "Citroën", "Dacia",
         "Mercedes-Benz", "BMW", "Audi", "Honda", "Nissan", "Kia", "Škoda", "Seat", "Volvo", "Tesla"];
    private static readonly string[] Renkler =
        ["Beyaz", "Siyah", "Gri", "Gümüş", "Kırmızı", "Mavi", "Lacivert", "Yeşil", "Kahverengi", "Bordo", "Turuncu", "Sarı"];
    private static readonly string[] Segmentler =
        ["Ekonomi", "Kompakt", "Orta", "Üst", "Lüks", "SUV", "Ticari", "Minivan"];
    private static readonly string[] AracTipleri =
        ["Sedan", "Hatchback", "Station Wagon", "SUV", "Van", "Pick-up"];
    private static readonly string[] AracGruplari =
        ["Ekonomi", "Orta", "Üst", "SUV", "Lüks", "Ticari"];
    private static readonly string[] Subeler = ["Merkez"];
    private static readonly string[] Lokasyonlar = ["Merkez Ofis"];
    private static readonly string[] CezaTurleri =
        ["Hız Cezası", "Park Cezası", "Kırmızı Işık", "Emniyet Kemeri", "HGS İhlali", "Diğer"];
    private static readonly string[] OdemeTipleri =
        ["Nakit", "Kredi Kartı", "Havale/EFT", "Çek", "Senet"];
    private static readonly string[] GiderTurleri =
        ["Yakıt", "Bakım-Onarım", "Sigorta", "Lastik", "Temizlik", "Otopark", "Ceza", "Diğer"];
    private static readonly string[] Kaynaklar =
        ["Telefon", "Web", "Ofis", "Acente", "Kurumsal", "Yürüyen Müşteri"];
    private static readonly string[] SigortaSirketleri =
        ["Allianz", "AXA", "Anadolu Sigorta", "Aksigorta", "HDI Sigorta", "Mapfre", "Sompo", "Türkiye Sigorta"];
    private static readonly string[] MusteriGruplari = ["Bireysel", "Kurumsal", "Acente", "VIP"];
    private static readonly string[] Ulkeler =
        ["Türkiye", "Almanya", "İngiltere", "Fransa", "Hollanda", "ABD", "Rusya", "Diğer"];
    private static readonly string[] Departmanlar = ["Operasyon", "Muhasebe", "Satış", "Yönetim"];

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
        n += await SeedCat<Brand>(db, tid, Markalar, (k, a) => new Brand { TenantId = tid, Kod = k, Ad = a }, ct);
        n += await SeedCat<VehicleColor>(db, tid, Renkler, (k, a) => new VehicleColor { TenantId = tid, Kod = k, Ad = a }, ct);
        n += await SeedCat<VehicleSegment>(db, tid, Segmentler, (k, a) => new VehicleSegment { TenantId = tid, Kod = k, Ad = a }, ct);
        n += await SeedCat<VehicleType>(db, tid, AracTipleri, (k, a) => new VehicleType { TenantId = tid, Kod = k, Ad = a }, ct);
        n += await SeedCat<VehicleGroup>(db, tid, AracGruplari, (k, a) => new VehicleGroup { TenantId = tid, Kod = k, Ad = a }, ct);
        n += await SeedCat<Branch>(db, tid, Subeler, (k, a) => new Branch { TenantId = tid, Kod = k, Ad = a }, ct);
        n += await SeedCat<Location>(db, tid, Lokasyonlar, (k, a) => new Location { TenantId = tid, Kod = k, Ad = a }, ct);
        n += await SeedCat<PenaltyType>(db, tid, CezaTurleri, (k, a) => new PenaltyType { TenantId = tid, Kod = k, Ad = a }, ct);
        n += await SeedCat<PaymentType>(db, tid, OdemeTipleri, (k, a) => new PaymentType { TenantId = tid, Kod = k, Ad = a }, ct);
        n += await SeedCat<ExpenseCategory>(db, tid, GiderTurleri, (k, a) => new ExpenseCategory { TenantId = tid, Kod = k, Ad = a }, ct);
        n += await SeedCat<ReservationSource>(db, tid, Kaynaklar, (k, a) => new ReservationSource { TenantId = tid, Kod = k, Ad = a }, ct);
        n += await SeedCat<InsuranceCompany>(db, tid, SigortaSirketleri, (k, a) => new InsuranceCompany { TenantId = tid, Kod = k, Ad = a }, ct);
        n += await SeedCat<CustomerGroup>(db, tid, MusteriGruplari, (k, a) => new CustomerGroup { TenantId = tid, Kod = k, Ad = a }, ct);
        n += await SeedCat<Country>(db, tid, Ulkeler, (k, a) => new Country { TenantId = tid, Kod = k, Ad = a }, ct);
        n += await SeedCat<Department>(db, tid, Departmanlar, (k, a) => new Department { TenantId = tid, Kod = k, Ad = a }, ct);

        if (n > 0)
        {
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear(); // tenant-loop'ta tracker şişmesin
        }
        return n;
    }

    /// <summary>O tenant'ta T kategorisi BOŞSA varsayılanları ekler; doluysa (kullanıcı tanımı var) dokunmaz.</summary>
    private static async Task<int> SeedCat<T>(
        AppDbContext db, Guid tid, string[] adlar, Func<string, string, T> make, CancellationToken ct)
        where T : class, ITenantOwned
    {
        var doluMu = await db.Set<T>().IgnoreQueryFilters().Where(x => x.TenantId == tid).AnyAsync(ct);
        if (doluMu) return 0;

        var kullanilan = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ad in adlar)
            db.Set<T>().Add(make(BenzersizKod(Fold(ad), kullanilan), ad));
        return adlar.Length;
    }

    /// <summary>Türkçe/Latin özel karakterleri ASCII'ye indirir, harf/rakam dışını atar, büyük harf yapar.</summary>
    private static string Fold(string ad)
    {
        var pre = ad.Trim()
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
    private static string BenzersizKod(string bazKod, HashSet<string> kullanilan)
    {
        var kod = bazKod.Length > 32 ? bazKod[..32] : bazKod;
        if (kullanilan.Add(kod)) return kod;
        for (var i = 2; ; i++)
        {
            var g = bazKod.Length > 30 ? bazKod[..30] : bazKod;
            var k = g + i;
            if (kullanilan.Add(k)) return k;
        }
    }
}
