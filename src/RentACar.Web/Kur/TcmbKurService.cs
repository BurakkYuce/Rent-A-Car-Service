using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Kur;

/// <summary>
/// TCMB günlük kur çekimi (public feed, kimliksiz) → KurKayitlari upsert. KurKayitlari PAYLAŞIMLI/platform
/// tablo (RLS yok) → app-conn + NullTenantContext ile yazılır (VadeBildirimJob deseni; owner GEREKMEZ,
/// racar_app CRUD grant'i yeterli). Idempotent (Tarih+Kod). Parse ayrı saf sınıfta (TcmbKurParser).
/// Singleton (job doğrudan enjekte eder + /kurlar/yenile ucu çağırır).
/// </summary>
public sealed class TcmbKurService(IHttpClientFactory httpFactory, IConfiguration config, ILogger<TcmbKurService> log)
{
    private const string Url = "https://www.tcmb.gov.tr/kurlar/today.xml";

    /// <summary>TCMB'yi çek + o günün kurlarını upsert. Yazılan döviz sayısı (0 = başarısız/boş).</summary>
    public async Task<int> RefreshAsync(CancellationToken ct = default)
    {
        string xml;
        try
        {
            var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(30);
            xml = await http.GetStringAsync(Url, ct);
        }
        catch (Exception ex) { log.LogWarning(ex, "TCMB kur feed'i çekilemedi."); return 0; }

        IReadOnlyList<KurKaydi> kayitlar;
        try { kayitlar = TcmbKurParser.Parse(xml); }
        catch (Exception ex) { log.LogWarning(ex, "TCMB kur XML parse edilemedi."); return 0; }
        if (kayitlar.Count == 0) return 0;

        var appConn = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default eksik.");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(appConn).Options;
        await using var db = new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance);

        var tarih = kayitlar[0].Tarih;
        var kodlar = kayitlar.Select(k => k.Kod).ToList();
        var mevcut = await db.KurKayitlari.Where(x => x.Tarih == tarih && kodlar.Contains(x.Kod)).ToListAsync(ct);
        var map = mevcut.ToDictionary(x => x.Kod);
        foreach (var k in kayitlar)
        {
            if (map.TryGetValue(k.Kod, out var e))
            {
                e.Ad = k.Ad; e.Birim = k.Birim;
                e.ForexAlis = k.ForexAlis; e.ForexSatis = k.ForexSatis;
                e.EfektifAlis = k.EfektifAlis; e.EfektifSatis = k.EfektifSatis;
            }
            else db.KurKayitlari.Add(k);
        }
        await db.SaveChangesAsync(ct);
        log.LogInformation("TCMB kur güncellendi: {Tarih:yyyy-MM-dd} — {Count} döviz.", tarih, kayitlar.Count);
        return kayitlar.Count;
    }
}
