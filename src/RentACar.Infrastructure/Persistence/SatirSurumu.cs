using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// F5.1 — kira DIŞINDAKİ tam değiştirme (PUT) uçlarının iyimser eşzamanlılığı: satır kilidi (<c>FOR UPDATE</c>) +
/// satır sürümü (Postgres <c>xmin</c>, opak metin). <see cref="KiraKilitleri.SurumAsync"/> ile AYNI desen; burada
/// tablo adı parametre — YALNIZ <see cref="Tablolar"/> beyaz listesinden (SQL metnine giren ad kullanıcı girdisi
/// olamaz). xmin satıra dokunan HER güncellemede değişir (EF, ham SQL); <c>UpdatedAtUtc</c>'yi her yol yazmıyor.
/// </summary>
internal static class SatirSurumu
{
    public const string Rezervasyonlar = "Reservations";
    public const string RezSartlari = "RezSartlari";
    public const string FiloKiralamalar = "FiloKiralamalar";
    /// <summary>F5.1 adversarial H1/L6 — teklif durum geçişleri ve kabul (rezervasyona çevirme) satır kilidi.</summary>
    public const string Teklifler = "Quotations";
    /// <summary>F6.1a — araç kartı ve araç tanımları (sahip, segment, tip) tam değiştirme PUT'ları.</summary>
    public const string Araclar = "Vehicles";
    public const string AracSahipleri = "AracSahipleri";
    public const string Segmentler = "Segmentler";
    public const string AracTipleri = "AracTipleri";
    /// <summary>F11.1b — firma ayarları (tenant başına tek satır) ve müşteri mesaj şablonları tam değiştirme PUT'ları.</summary>
    public const string FirmaAyarlari = "Ayarlar";
    public const string MesajSablonlari = "MesajSablonlari";

    /// <summary>F11.1b — tanım ekranlarının (ikinci yarı) tam değiştirme PUT'ları.</summary>
    public const string InsuranceCompanies = "SigortaSirketleri";
    public const string KdvRates = "KdvOranlari";
    public const string PenaltyTypes = "CezaTurleri";
    public const string Locations = "Locations";
    public const string Personnel = "Personeller";
    public const string DocumentTemplates = "BelgeSablonlari";
    public const string VehicleGroups = "AracGruplari";

    /// <summary>F11.1b — web sitesi yönetimi tam değiştirme PUT'ları (blog, site sayfası, SSS, ilan fiyat/özellik).</summary>
    public const string BlogYazilari = "BlogYazilari";
    public const string SayfaIcerikler = "SayfaIcerikler";
    public const string SssKayitlari = "SssKayitlari";
    public const string WebIlanlar = "WebIlanlar";

    /// <summary>F7.1 — cari kartı ve CRM kayıtları (anket, şikayet, assistans, hukuk) tam değiştirme PUT'ları.</summary>
    public const string Customers = "Customers", Surveys = "Anketler", Complaints = "Sikayetler", AssistanceRequests = "AssistansTalepleri", LegalFiles = "HukukDosyalari";

    // F6.1b — araç finans/operasyon kayıtları: durum geçişleri kilit altında, tam değiştirme PUT'u sürümlü.
    public const string AracSiparisleri = "AracSiparisleri";
    public const string Baflar = "Baflar";
    public const string HasarDosyalari = "DamageFiles";
    public const string MusteriTaksitleri = "MusteriTaksitleri";
    public const string FiloPlanHedefleri = "FiloPlanHedefleri";

    /// <summary>F8.1b (#286 M3) — gelen e-fatura bağlama PUT'u sürümlü; durum geçişleri ve giderleştirme kilit altında.</summary>
    public const string GelenEFaturalar = "GelenEFaturalar";

    private static readonly HashSet<string> Tablolar =
    [
        Rezervasyonlar, RezSartlari, FiloKiralamalar, Teklifler,
        Araclar, AracSahipleri, Segmentler, AracTipleri,
        AracSiparisleri, Baflar, HasarDosyalari, MusteriTaksitleri, FiloPlanHedefleri,
        // F11.1b
        FirmaAyarlari, MesajSablonlari,
        InsuranceCompanies, KdvRates, PenaltyTypes, Locations, Personnel, DocumentTemplates, VehicleGroups,
        BlogYazilari, SayfaIcerikler, SssKayitlari, WebIlanlar,
        GelenEFaturalar,
        Customers, Surveys, Complaints, AssistanceRequests, LegalFiles,
    ];

    private static string Dogrula(string tablo)
        => Tablolar.Contains(tablo) ? tablo : throw new ArgumentException($"Sürüm tablosu beyaz listede değil: {tablo}", nameof(tablo));

    /// <summary>Satırı OKUMADAN önce kilitler (aynı işlemde; çağıran işlemi açmış olmalı).</summary>
    public static Task KilitleAsync(AppDbContext db, string tablo, Guid id, CancellationToken ct)
    {
        var sql = $"SELECT 1 FROM \"{Dogrula(tablo)}\" WHERE \"Id\" = {{0}} FOR UPDATE";
        return db.Database.ExecuteSqlRawAsync(sql, [id], ct);
    }

    /// <summary>Satır sürümü; satır yoksa / RLS kapsamı dışındaysa <c>null</c>. Açık işlemde çağrılırsa aynı işlemde okunur.</summary>
    public static async Task<string?> OkuAsync(AppDbContext db, string tablo, Guid id, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await db.Database.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText = $"SELECT xmin::text FROM \"{Dogrula(tablo)}\" WHERE \"Id\" = @id";
        var p = cmd.CreateParameter();
        p.ParameterName = "id";
        p.Value = id;
        cmd.Parameters.Add(p);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    /// <summary>
    /// Kilit + sürüm karşılaştırması + <paramref name="uygula"/> TEK işlemde. <paramref name="beklenenSurum"/> null →
    /// yalnız kilit (karşılaştırma yok). Sürüm farklı → <see cref="Application.Common.EszamanliDegisiklikException"/>,
    /// hiçbir şey yazılmaz (kontrol ile yazma arasında başka yazım giremez).
    /// </summary>
    public static async Task<bool> GuncelleAsync<T>(
        IDbContextFactory<AppDbContext> factory, string tablo, Guid id, string? beklenenSurum,
        Func<AppDbContext, Guid, CancellationToken, Task<T?>> bul, Action<T> uygula, CancellationToken ct)
        where T : class
        => await PgRetry.RunAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await KilitleAsync(db, tablo, id, ct);
            if (beklenenSurum is not null && await OkuAsync(db, tablo, id, ct) is { } guncel
                && !string.Equals(guncel, beklenenSurum.Trim(), StringComparison.Ordinal))
                throw new Application.Common.EszamanliDegisiklikException(Application.Common.EszamanliDegisiklikException.KayitMesaji);
            var satir = await bul(db, id, ct);
            if (satir is null) return false;
            uygula(satir);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }, ct);
}
