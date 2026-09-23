using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Kira;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Rapor;

/// <summary>
/// F10.1 — rapor uçlarının ŞUBE KAPSAMI. Rapor servisleri (<c>ReportService</c>) kullanıcıyı tanımaz, firma geneli
/// okur; kapsam uç katmanındadır. Tek kural:
/// <list type="bullet">
/// <item><b>Firma geneli rapor</b> (defter, cari, fatura, KDV, kârlılık…): şube kapsamlı kullanıcıya 403
/// <c>yetki_yok</c>. Varsayılan matriste kapsamlı kullanıcı (Operatör) zaten ViewReports taşımaz; izin kullanıcı
/// bazında verilebildiği için kapı yine uçta durur (araç detaylı listesindeki gerekçe).</item>
/// <item><b>Şube boyutlu rapor</b> (araç durum takip, periyodik servis): kapsamlı kullanıcının şube süzgeci KENDİ
/// şubesine zorlanır; başka şube istemek 403.</item>
/// <item><b>Satır bazlı</b> (sigorta-muayene, km detay): satırlar <see cref="BranchScope.InScope"/> ile süzülür.</item>
/// </list>
/// </summary>
internal static class ReportScope
{
    public static void RequireFirmWide(ICurrentUser user)
    {
        if (!BranchScope.EffectiveFilter(user).Unrestricted)
            throw new YetkiYokException("Bu rapor firma genelidir; şube kapsamlı kullanıcıya kapalıdır.");
    }

    /// <summary>
    /// Şube süzgecinin etkin değeri: kapsamsız kullanıcıda istenen (boş = hepsi); kapsamlıda KENDİ şube adı.
    /// Başka şube istemek 403. Atamada yalnız FK varsa ad FK'den çözülür.
    /// </summary>
    public static async Task<string?> BranchFilterAsync(
        ICurrentUser user, string? requested, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        var f = BranchScope.EffectiveFilter(user);
        var istenen = string.IsNullOrWhiteSpace(requested) ? null : requested.Trim();
        if (f.Unrestricted) return istenen;
        var ad = f.SubeAd;
        if (ad is null && f.SubeId is { } id)
        {
            await using var db = await dbf.CreateDbContextAsync(ct);
            ad = await db.Branches.AsNoTracking().Where(b => b.Id == id).Select(b => b.Ad).FirstOrDefaultAsync(ct);
        }
        if (ad is null) throw new YetkiYokException("Şube kapsamınız çözülemedi.");
        if (istenen is not null && !string.Equals(istenen, ad.Trim(), StringComparison.Ordinal))
            throw new YetkiYokException("Bu şube kapsamınız dışında.");
        return ad.Trim();
    }

    /// <summary>Kira kimliklerinden kapsamdakiler (çıkış şubesi FK'si + çıkış ofisi metni — kira listesiyle aynı kural).</summary>
    public static async Task<HashSet<Guid>?> RentalsInScopeAsync(
        ICurrentUser user, IEnumerable<Guid> rentalIds, IDbContextFactory<AppDbContext> dbf, CancellationToken ct)
    {
        var f = BranchScope.EffectiveFilter(user);
        if (f.Unrestricted) return null; // null = süzme yok
        var ids = rentalIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        await using var db = await dbf.CreateDbContextAsync(ct);
        var kiralar = await db.Rentals.AsNoTracking().Where(r => ids.Contains(r.Id))
            .Select(r => new { r.Id, r.CikisSubeId, r.CikisOfisi }).ToListAsync(ct);
        return kiralar.Where(r => BranchScope.InScope(f, r.CikisSubeId, r.CikisOfisi)).Select(r => r.Id).ToHashSet();
    }
}

/// <summary>
/// F10.1 — rapor yanıtlarındaki müşteri adı/iletişimi, KVKK TEK KURALIYLA (<see cref="MusteriGorunumu"/>).
/// Rapor servisleri cari adını <c>DisplayName</c>'den üretir ve anonimleştirme bayraklarını okumaz; uç yeniden çözer.
/// <list type="bullet">
/// <item>Kimliği bilinen satır: carinin kendi bayrakları (<c>AnonimAd/AnonimTelefon/AnonimMail</c>).</item>
/// <item>Kimliği taşımayan satır (servis DTO'sunda yalnız ad var): firmanın <c>AnonimAd</c> işaretli carilerinin
/// görünen ad kümesiyle eşleşen ad maskelenir. Aynı adlı anonim olmayan cari de maskelenir — güvenli yön.</item>
/// <item>TC/ehliyet/pasaport hiçbir rapor yanıtında yoktur (şifreli kolonlara dokunulmaz).</item>
/// </list>
/// </summary>
internal sealed class CustomerMask
{
    private readonly Dictionary<Guid, (bool Ad, bool Tel, bool Mail)> _flags;
    private readonly HashSet<string> _anonNames;

    private CustomerMask(Dictionary<Guid, (bool, bool, bool)> flags, HashSet<string> anonNames)
    {
        _flags = flags;
        _anonNames = anonNames;
    }

    public static async Task<CustomerMask> LoadAsync(
        IDbContextFactory<AppDbContext> dbf, IEnumerable<Guid>? ids, CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);
        var list = ids?.Distinct().ToList() ?? [];
        var flags = list.Count == 0
            ? new Dictionary<Guid, (bool, bool, bool)>()
            : (await db.Customers.AsNoTracking().Where(c => list.Contains(c.Id))
                    .Select(c => new { c.Id, c.AnonimAd, c.AnonimTelefon, c.AnonimMail }).ToListAsync(ct))
                .ToDictionary(c => c.Id, c => (c.AnonimAd, c.AnonimTelefon, c.AnonimMail));
        var anon = (await db.Customers.AsNoTracking().Where(c => c.AnonimAd)
                .Select(c => new { c.Tip, c.Unvan, c.Ad, c.Soyad }).ToListAsync(ct))
            .Select(c => new Customer { Tip = c.Tip, Unvan = c.Unvan, Ad = c.Ad, Soyad = c.Soyad }.DisplayName)
            .ToHashSet(StringComparer.Ordinal);
        return new CustomerMask(flags, anon);
    }

    /// <summary>Görünen ad: anonimse sabit etiket.</summary>
    public string Name(Guid? id, string? name)
    {
        if (id is { } i && _flags.TryGetValue(i, out var f))
            return f.Ad ? MusteriGorunumu.AnonimAdEtiketi : name ?? "—";
        return Name(name);
    }

    /// <summary>Yalnız adla (kimliksiz satır) maskeleme.</summary>
    public string Name(string? name)
        => name is not null && _anonNames.Contains(name) ? MusteriGorunumu.AnonimAdEtiketi : name ?? "—";

    public string? Phone(Guid id, string? phone) => _flags.TryGetValue(id, out var f) && f.Tel ? null : phone;

    public string? Email(Guid id, string? email) => _flags.TryGetValue(id, out var f) && f.Mail ? null : email;
}

/// <summary>
/// F10.1 — export BAĞLANTISI (yeni dosya ucu açılmaz). Mevcut sunucu export uçları (<c>/raporlar/export/{rapor}</c>,
/// ViewReports) ekrandaki süzgeçle aynı sorguyu alır: "gördüğün = indirdiğin". Bağlantı yalnız ViewReports taşıyan VE
/// firma geneli yetkili kullanıcıya verilir (export uçları firma geneli okur; kapsamlı kullanıcıya bağlantı sızmaz).
/// </summary>
internal static class ReportExport
{
    public static ReportExportLinks? Links(HttpContext http, ICurrentUser user, string report,
        IEnumerable<(string Key, string? Value)> query, bool pdf = false)
    {
        if (!AuthExtensions.HasPermission(http.User, Permission.ViewReports)
            || !BranchScope.EffectiveFilter(user).Unrestricted)
            return null;
        var qs = string.Concat(query.Where(q => !string.IsNullOrWhiteSpace(q.Value))
            .Select(q => $"&{q.Key}={Uri.EscapeDataString(q.Value!.Trim())}"));
        string Url(string format) => $"/raporlar/export/{report}?format={format}{qs}";
        return new ReportExportLinks(Url("excel"), Url("csv"), pdf ? Url("pdf") : null);
    }

    /// <summary>Export uçlarının tarih biçimi (<c>from</c>/<c>to</c>, yyyy-MM-dd).</summary>
    public static string? Day(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static (string, string?)[] Period(ReportPeriod p) => [("from", Day(p.Bas)), ("to", Day(p.Bit))];
}

/// <summary>Export bağlantıları (Excel, CSV, varsa PDF). <c>null</c> = kullanıcıya export açık değil.</summary>
public sealed record ReportExportLinks(string Excel, string Csv, string? Pdf);

/// <summary>Satırsız rapor yanıtı: dönem + özet + export.</summary>
public sealed record ReportSummaryResult<TSummary>(ReportPeriodDto Donem, TSummary Ozet, ReportExportLinks? Export);

/// <summary>Satırlı rapor yanıtı: dönem + özet (tüm satırlar üzerinden) + sayfalı satırlar + export.</summary>
public sealed record ReportResult<TSummary, TRow>(
    ReportPeriodDto Donem, TSummary Ozet, Sayfa<TRow> Satirlar, ReportExportLinks? Export);

/// <summary>Özeti olmayan satırlı raporlar için özet yeri (satır sayısı).</summary>
public sealed record ReportCount(int Adet);
