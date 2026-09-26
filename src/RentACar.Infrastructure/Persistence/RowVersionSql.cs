using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// F5.1 — kira DIŞINDAKİ tam değiştirme (PUT) uçlarının iyimser eşzamanlılığı: satır kilidi (<c>FOR UPDATE</c>) +
/// satır sürümü (Postgres <c>xmin</c>, opak metin). <see cref="RentalLocks.VersionAsync"/> ile AYNI desen; burada
/// tablo adı parametre — YALNIZ <see cref="Tables"/> beyaz listesinden (SQL metnine giren ad kullanıcı girdisi
/// olamaz). xmin satıra dokunan HER güncellemede değişir (EF, ham SQL); <c>UpdatedAtUtc</c>'yi her yol yazmıyor.
/// </summary>
internal static class RowVersionSql
{
    public const string Reservations = "Reservations";
    public const string ReservationTerms = "RezSartlari";
    public const string FleetRentals = "FiloKiralamalar";
    /// <summary>F5.1 adversarial H1/L6 — teklif durum geçişleri ve kabul (rezervasyona çevirme) satır kilidi.</summary>
    public const string Quotations = "Quotations";
    /// <summary>F6.1a — araç kartı ve araç tanımları (sahip, segment, tip) tam değiştirme PUT'ları.</summary>
    public const string Vehicles = "Vehicles";
    public const string VehicleOwners = "AracSahipleri";
    public const string Segments = "Segmentler";
    public const string VehicleTypes = "AracTipleri";
    /// <summary>F11.1b — firma ayarları (tenant başına tek satır) ve müşteri mesaj şablonları tam değiştirme PUT'ları.</summary>
    public const string CompanySettings = "Ayarlar";
    public const string MessageTemplates = "MesajSablonlari";

    /// <summary>F11.1b — tanım ekranlarının (ikinci yarı) tam değiştirme PUT'ları.</summary>
    public const string InsuranceCompanies = "SigortaSirketleri";
    public const string KdvRates = "KdvOranlari";
    public const string PenaltyTypes = "CezaTurleri";
    public const string Locations = "Locations";
    public const string Personnel = "Personeller";
    public const string DocumentTemplates = "BelgeSablonlari";
    public const string VehicleGroups = "AracGruplari";

    /// <summary>F11.1b — web sitesi yönetimi tam değiştirme PUT'ları (blog, site sayfası, SSS, ilan fiyat/özellik).</summary>
    public const string BlogPosts = "BlogYazilari";
    public const string PageContents = "SayfaIcerikler";
    public const string FaqEntries = "SssKayitlari";
    public const string WebListings = "WebIlanlar";

    /// <summary>F7.1 — cari kartı ve CRM kayıtları (anket, şikayet, assistans, hukuk) tam değiştirme PUT'ları.</summary>
    public const string Customers = "Customers", Surveys = "Anketler", Complaints = "Sikayetler", AssistanceRequests = "AssistansTalepleri", LegalFiles = "HukukDosyalari";

    // F6.1b — araç finans/operasyon kayıtları: durum geçişleri kilit altında, tam değiştirme PUT'u sürümlü.
    public const string VehicleOrders = "AracSiparisleri";
    public const string Bafs = "Baflar";
    public const string DamageFiles = "DamageFiles";
    public const string CustomerInstallments = "MusteriTaksitleri";
    public const string FleetPlanTargets = "FiloPlanHedefleri";

    /// <summary>F8.1b (#286 M3) — gelen e-fatura bağlama PUT'u sürümlü; durum geçişleri ve giderleştirme kilit altında.</summary>
    public const string IncomingEInvoices = "GelenEFaturalar";

    private static readonly HashSet<string> Tables =
    [
        Reservations, ReservationTerms, FleetRentals, Quotations,
        Vehicles, VehicleOwners, Segments, VehicleTypes,
        VehicleOrders, Bafs, DamageFiles, CustomerInstallments, FleetPlanTargets,
        // F11.1b
        CompanySettings, MessageTemplates,
        InsuranceCompanies, KdvRates, PenaltyTypes, Locations, Personnel, DocumentTemplates, VehicleGroups,
        BlogPosts, PageContents, FaqEntries, WebListings,
        IncomingEInvoices,
        Customers, Surveys, Complaints, AssistanceRequests, LegalFiles,
    ];

    private static string Validate(string table)
        => Tables.Contains(table) ? table : throw new ArgumentException($"Sürüm tablosu beyaz listede değil: {table}", nameof(table));

    /// <summary>Satırı OKUMADAN önce kilitler (aynı işlemde; çağıran işlemi açmış olmalı).</summary>
    public static Task LockAsync(AppDbContext db, string table, Guid id, CancellationToken ct)
    {
        var sql = $"SELECT 1 FROM \"{Validate(table)}\" WHERE \"Id\" = {{0}} FOR UPDATE";
        return db.Database.ExecuteSqlRawAsync(sql, [id], ct);
    }

    /// <summary>Satır sürümü; satır yoksa / RLS kapsamı dışındaysa <c>null</c>. Açık işlemde çağrılırsa aynı işlemde okunur.</summary>
    public static async Task<string?> ReadAsync(AppDbContext db, string table, Guid id, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await db.Database.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText = $"SELECT xmin::text FROM \"{Validate(table)}\" WHERE \"Id\" = @id";
        var p = cmd.CreateParameter();
        p.ParameterName = "id";
        p.Value = id;
        cmd.Parameters.Add(p);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    /// <summary>
    /// Kilit + sürüm karşılaştırması + <paramref name="apply"/> TEK işlemde. <paramref name="expectedVersion"/> null →
    /// yalnız kilit (karşılaştırma yok). Sürüm farklı → <see cref="Application.Common.ConcurrentModificationException"/>,
    /// hiçbir şey yazılmaz (kontrol ile yazma arasında başka yazım giremez).
    /// </summary>
    public static async Task<bool> UpdateAsync<T>(
        IDbContextFactory<AppDbContext> factory, string table, Guid id, string? expectedVersion,
        Func<AppDbContext, Guid, CancellationToken, Task<T?>> find, Action<T> apply, CancellationToken ct)
        where T : class
        => await PgRetry.RunAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await LockAsync(db, table, id, ct);
            if (expectedVersion is not null && await ReadAsync(db, table, id, ct) is { } current
                && !string.Equals(current, expectedVersion.Trim(), StringComparison.Ordinal))
                throw new Application.Common.ConcurrentModificationException(Application.Common.ConcurrentModificationException.RecordMessage);
            var row = await find(db, id, ct);
            if (row is null) return false;
            apply(row);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }, ct);
}
