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
public sealed class TcmbExchangeRateService(IHttpClientFactory httpFactory, IConfiguration config, ILogger<TcmbExchangeRateService> log)
{
    private const string Url = "https://www.tcmb.gov.tr/kurlar/today.xml";
    private static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(30);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastFetch = DateTimeOffset.MinValue;

    /// <summary>
    /// TCMB'yi çek + upsert. Dönen: yazılan döviz sayısı; **-1 = THROTTLE** (son 30 dk içinde çekilmiş → TCMB'ye
    /// GİDİLMEDİ; buton-spam koruması — kur günde 1 değişir + KurKayitlari paylaşımlı, tek çekim herkese yeter).
    /// 0 = başarısız/boş. Singleton + SemaphoreSlim → tüm tenant'lar için GLOBAL kısıt. <paramref name="force"/>
    /// throttle'ı atlar.
    /// </summary>
    public async Task<int> RefreshAsync(bool force = false, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!force && DateTimeOffset.UtcNow - _lastFetch < MinInterval)
                return -1; // cache güncel → TCMB'yi yorma
            var n = await DoRefreshAsync(ct);
            if (n > 0) _lastFetch = DateTimeOffset.UtcNow; // yalnız başarılı çekimde damgala
            return n;
        }
        finally { _gate.Release(); }
    }

    private async Task<int> DoRefreshAsync(CancellationToken ct)
    {
        string xml;
        try
        {
            var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(30);
            xml = await http.GetStringAsync(Url, ct);
        }
        catch (Exception ex) { log.LogWarning(ex, "TCMB kur feed'i çekilemedi."); return 0; }

        IReadOnlyList<KurKaydi> records;
        try { records = TcmbExchangeRateParser.Parse(xml); }
        catch (Exception ex) { log.LogWarning(ex, "TCMB kur XML parse edilemedi."); return 0; }
        if (records.Count == 0) return 0;

        var appConn = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default eksik.");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(appConn).Options;
        await using var db = new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance);

        var date = records[0].Tarih;
        var codes = records.Select(k => k.Kod).ToList();
        var existing = await db.KurKayitlari.Where(x => x.Tarih == date && codes.Contains(x.Kod)).ToListAsync(ct);
        var map = existing.ToDictionary(x => x.Kod);
        foreach (var k in records)
        {
            if (map.TryGetValue(k.Kod, out var e))
            {
                e.Ad = k.Ad; e.Birim = k.Birim;
                e.ForexAlis = k.ForexAlis; e.ForexSatis = k.ForexSatis;
                e.EfektifAlis = k.EfektifAlis; e.EfektifSatis = k.EfektifSatis;
            }
            else db.KurKayitlari.Add(k);
        }
        try { await db.SaveChangesAsync(ct); }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
            when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            // Çok-instance yarışı: başka bir instance aynı (Tarih,Kod) günü az önce yazdı → veri taze,
            // idempotent no-op ("zaten güncel" = throttle semantiği). Denetim kozmetik bulgusu.
            log.LogDebug("TCMB kur yazımında yarış (unique) — başka instance yazdı, atlandı.");
            return -1;
        }
        log.LogInformation("TCMB kur güncellendi: {Tarih:yyyy-MM-dd} — {Count} döviz.", date, records.Count);
        return records.Count;
    }
}
