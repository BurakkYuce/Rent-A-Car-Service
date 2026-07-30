using Microsoft.EntityFrameworkCore;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Bookings;

/// <summary>
/// PR-C — <b>anonim</b> sözleşme görüntüleme (token'lı paylaşım linki). Cookie yok → istek scope'unda
/// tenant context yok → RLS default-deny. Bu yüzden izolasyon ELLE ve <b>iki fazlı</b>, aynen
/// <see cref="Calendar.CalendarFeedService"/>:
/// <list type="number">
/// <item>token → <c>PaylasimLinkler</c> (PLATFORM tablosu, RLS yok; anonim <c>NullTenantContext</c> ile
/// okunabilir) → tenant + kira. İptal edilmiş link ya da kapatılmış tenant → <c>null</c> (uç 404).</item>
/// <item><c>SystemTenantContext</c> (EF filter) + <c>TenantGuc.OpenAsync</c> (RLS GUC) → <c>SozlesmePdfler</c>.
/// Çift izolasyon: PDF müşteri adı/adresi taşır, kimlik doğrulanmamış bir istekte yanlış tenant'ın
/// satırına erişme yolu bırakılmaz.</item>
/// </list>
///
/// <para>Token 32-byte CSPRNG olduğundan tahmin edilemez; uçta ayrıca rate-limit var (ucuz kemer).</para>
///
/// <para><b>Erişim sayacı ham SQL ile atomik artırılır</b> (<c>SET x = x + 1</c>): oku-değiştir-yaz
/// yapılsaydı aynı linki eşzamanlı açan iki kişi tek artış üretirdi. Sayaç yazımı <b>best-effort</b> —
/// hata verirse belge yine servis edilir, çünkü müşterinin sözleşmesini görmesi sayaçtan önemli.</para>
///
/// <para><b>IP KAYDEDİLMEZ</b> (kişisel veri). Sayaç + son erişim zamanı, cevaplaması gereken soru
/// için ("müşteri açmadı diyor") yeterli.</para>
/// </summary>
public sealed class SozlesmeGoruntuleService(IConfiguration config, ILogger<SozlesmeGoruntuleService> logger)
{
    public sealed record Sonuc(byte[] Pdf, string DosyaAdi);

    public async Task<Sonuc?> GoruntuleAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 64) return null;

        var appConn = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default eksik.");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(appConn).Options;

        // 1) token → link (platform tablosu, RLS yok). İptal / pasif tenant → null.
        Guid linkId, tenantId, rentalId;
        string sozlesmeNo;
        await using (var db0 = new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            var link = await db0.PaylasimLinkler.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Token == token && !x.Iptal, ct);
            if (link is null) return null;

            var tenantActive = await db0.Tenants.AsNoTracking()
                .Where(t => t.Id == link.TenantId).Select(t => t.IsActive).FirstOrDefaultAsync(ct);
            if (!tenantActive) return null;   // kapatılmış firmanın linkleri de durur

            linkId = link.Id;
            tenantId = link.TenantId;
            rentalId = link.RentalId;
            sozlesmeNo = link.SozlesmeNo;
        }

        // 2) anlık görüntü — SystemTenantContext (EF filter) + set_config GUC (RLS). Çift izolasyon.
        byte[] pdf;
        var sys = new SystemTenantContext { TenantId = tenantId };
        await using (var db = new AppDbContext(options, sys, sys))
        {
            await TenantGuc.OpenAsync(db, tenantId, ct);   // raw-context GUC açılışının TEK doğru yolu
            var bytes = await db.SozlesmePdfler.AsNoTracking()
                .Where(x => x.RentalId == rentalId).Select(x => x.Bytes).FirstOrDefaultAsync(ct);
            if (bytes is null || bytes.Length == 0) return null;   // link var, görüntü yok → 404
            pdf = bytes;
        }

        // 3) erişim kaydı — atomik, best-effort, IP'siz. Belge zaten elimizde; sayaç hatası
        //    müşteriyi 500'e düşürmemeli.
        try
        {
            await using var dbSay = new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance);
            await dbSay.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE "PaylasimLinkler"
                   SET "ErisimSayisi" = "ErisimSayisi" + 1, "SonErisimUtc" = {DateTimeOffset.UtcNow}
                 WHERE "Id" = {linkId}
                """, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Paylaşım linki erişim sayacı güncellenemedi (link {LinkId}).", linkId);
        }

        // Dosya adı ASCII: müşteri adı KOYULMAZ (kişisel veri + RFC 5987 encode ihtiyacı doğar).
        // SozlesmeNo zaten anlamlı ve ASCII (`RZ-000001`); yine de sadeleştirilir.
        var ad = new string(sozlesmeNo.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').ToArray());
        if (ad.Length == 0) ad = "sozlesme";
        return new Sonuc(pdf, $"Sozlesme-{ad}.pdf");
    }
}
