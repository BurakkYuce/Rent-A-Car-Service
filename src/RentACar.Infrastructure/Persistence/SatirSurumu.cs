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

    private static readonly HashSet<string> Tablolar = [Rezervasyonlar, RezSartlari, FiloKiralamalar, Teklifler];

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
