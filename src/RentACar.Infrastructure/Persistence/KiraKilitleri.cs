using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RentACar.Application.Common;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// Kira üzerindeki iki kilit — TEK KOPYA (F4.1 adversarial M2/M3):
/// <list type="bullet">
/// <item><see cref="FaturaAsync"/>: kira-fatura advisory kilidi (<c>fatura:{tenant}:{kira}</c>; adversarial
/// B2-Kritik-1). Base/fark/dönem faturası kesimi ve kira İPTALİ bununla serileşir.</item>
/// <item><see cref="SatirAsync"/>: kira satırı <c>FOR UPDATE</c>. Kira toplamını/durumunu değiştiren her yol
/// (dönüş, uzatma, teslim, iptal, açık güncelleme, provizyon, ek hizmet ekle/sil) satırı OKUMADAN ÖNCE
/// kilitler; toplam ve durum kilit ALTINDA okunur → kayıp güncelleme yok.</item>
/// </list>
/// <b>Sıra kuralı:</b> ikisi birden gerekiyorsa ÖNCE advisory, SONRA satır (fatura yolu da bu sırada: advisory →
/// fatura insert'inin kira FK'si için KEY SHARE). Ters sıra kilitlenme döngüsü üretirdi.
/// </summary>
internal static class KiraKilitleri
{
    public static async Task FaturaAsync(AppDbContext db, Guid rentalId, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@k, 42))";
        var p = cmd.CreateParameter();
        p.ParameterName = "k";
        p.Value = $"fatura:{db.TenantId}:{rentalId}";
        cmd.Parameters.Add(p);
        await cmd.ExecuteScalarAsync(ct);
    }

    public static Task SatirAsync(AppDbContext db, Guid rentalId, CancellationToken ct)
        => db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Rentals\" WHERE \"Id\" = {rentalId} FOR UPDATE", ct);

    /// <summary>Fatura yolu, advisory kilit ALTINDA: kira bu arada iptal edildiyse kesim reddedilir
    /// (servisin kilit dışı "iptal kiraya fatura yok" kontrolü ile iptal arasındaki yarış penceresi).</summary>
    public static async Task IptalKirayaFaturaYokAsync(AppDbContext db, Guid rentalId, CancellationToken ct)
    {
        if (await db.Rentals.AsNoTracking().AnyAsync(r => r.Id == rentalId && r.Durum == RentalStatus.Iptal, ct))
            throw new ValidationException("İptal edilmiş kiraya fatura kesilemez (kira bu sırada iptal edildi).");
    }

    /// <summary>Kirada iade edilmemiş (net) fatura var mı — base, fark ve dönem faturaları; iadesi kesilmiş
    /// fatura sayılmaz. Kira iptali bunu fatura kilidi ALTINDA sorar.</summary>
    public static Task<bool> AcikFaturaVarAsync(AppDbContext db, Guid rentalId, CancellationToken ct)
        => db.Invoices.AsNoTracking().AnyAsync(i =>
            (i.RentalId == rentalId || i.KaynakKiraId == rentalId)
            && !i.IadeMi
            && i.Durum != InvoiceStatus.Iptal
            && !db.Invoices.Any(x => x.IadeMi && x.KaynakFaturaId == i.Id), ct);
}
