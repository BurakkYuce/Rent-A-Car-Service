using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Kur;
using RentACar.Domain.Common;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Api.AracFinans;

/// <summary>
/// F6.1b — araç finans uçlarının (kredi, müşteri taksiti, sipariş, BAF, hasar, filo plan) ortak uç-katmanı kuralları.
/// İş mantığı YOK: kapsam (alt kayıt ARACIN şubesinden geçer), varlık (başka kiracının / olmayan cari-araç kimliğiyle
/// yazım yok), para girdi sınırları (<c>numeric(19,4)</c>, TRY'de açık kur ≠ 1 reddi).
/// </summary>
internal static class VehicleFinanceShared
{
    /// <summary><c>numeric(19,4)</c>: 15 tam basamak.</summary>
    public const decimal AmountUpperLimit = 1_000_000_000_000_000m;

    /// <summary>Kur üst sınırı (kolonların en darı <c>numeric(19,4)</c>; makul tavan).</summary>
    public const decimal RateUpperLimit = 1_000_000m;

    public const string BaseCurrency = "TRY";

    /// <summary>Araç kimliği → (şube FK, şube metni); RLS kapsamlı tek sorgu (başka kiracının aracı görünmez).</summary>
    public static async Task<Dictionary<Guid, (Guid? SubeId, string? Sube)>> VehicleBranchesAsync(
        IDbContextFactory<AppDbContext> dbf, IEnumerable<Guid> identities, CancellationToken ct)
    {
        var ids = identities.Distinct().ToList();
        if (ids.Count == 0) return [];
        await using var db = await dbf.CreateDbContextAsync(ct);
        var rows = await db.Vehicles.AsNoTracking().Where(v => ids.Contains(v.Id))
            .Select(v => new { v.Id, v.SubeId, v.Sube }).ToListAsync(ct);
        return rows.ToDictionary(v => v.Id, v => (v.SubeId, v.Sube));
    }

    /// <summary>
    /// Alt kaydın ARACIN şubesi üzerinden görülebilirliği (liste süzgeci). Araçsız kayıt yalnız şube kısıtsız
    /// kullanıcıya görünür: şubeye bağlı kullanıcı bağı olmayan kaydı "kendi şubesinin" sayamaz.
    /// </summary>
    public static bool IsVisible(BranchScope.BranchFilter f, Guid? vehicleId,
        IReadOnlyDictionary<Guid, (Guid? SubeId, string? Sube)> branches)
    {
        if (f.Unrestricted) return true;
        if (vehicleId is not { } v || !branches.TryGetValue(v, out var s)) return false;
        return BranchScope.InScope(f, s.SubeId, s.Sube);
    }

    /// <summary>Tekil kayıt kapısı: kapsam dışı → 403 <c>yetki_yok</c> (durum kontrolünden ÖNCE çağrılır).</summary>
    public static async Task RecordScopeAsync(
        IDbContextFactory<AppDbContext> dbf, ICurrentUser user, Guid? vehicleId, CancellationToken ct)
    {
        var f = BranchScope.EffectiveFilter(user);
        if (f.Unrestricted) return;
        var branches = await VehicleBranchesAsync(dbf, vehicleId is { } v ? [v] : [], ct);
        if (!IsVisible(f, vehicleId, branches)) throw new NoPermissionException("Bu kayıt şube kapsamınız dışında.");
    }

    /// <summary>
    /// Yazımda araç: VAR olmalı (RLS kapsamlı; başka kiracının ya da olmayan kimlik → 400 <paramref name="alan"/>) ve
    /// çağıranın şube kapsamında olmalı (403). Araç boşsa şubeye bağlı kullanıcı reddedilir (kendi de göremeyeceği
    /// "yetim" kayıt açılmasın); <paramref name="required"/> ise herkes için 400.
    /// </summary>
    public static async Task VehicleWriteAsync(IDbContextFactory<AppDbContext> dbf, ICurrentUser user,
        Guid? vehicleId, string alan, bool required, CancellationToken ct)
    {
        var f = BranchScope.EffectiveFilter(user);
        if (vehicleId is not { } v || v == Guid.Empty)
        {
            if (required || !f.Unrestricted)
                throw new ValidationException(required
                    ? "Araç seçilmelidir."
                    : "Araç seçilmelidir (şubeye bağlı kullanıcı kendi şubesinin aracını seçmelidir).", alan);
            return;
        }
        var branches = await VehicleBranchesAsync(dbf, [v], ct);
        if (!branches.TryGetValue(v, out var s)) throw new ValidationException("Araç bulunamadı.", alan);
        BranchScope.RequireInScope(user, s.SubeId, s.Sube);
    }

    /// <summary>Cari bu kiracıda VAR olmalı (RLS kapsamlı; şifreli PII kolonlarına dokunmaz).</summary>
    public static async Task CustomerExistsAsync(IDbContextFactory<AppDbContext> dbf, Guid? customerId, string alan,
        bool required, CancellationToken ct)
    {
        if (customerId is not { } c || c == Guid.Empty)
        {
            if (required) throw new ValidationException("Cari seçilmelidir.", alan);
            return;
        }
        await using var db = await dbf.CreateDbContextAsync(ct);
        if (!await db.Customers.AsNoTracking().AnyAsync(x => x.Id == c, ct))
            throw new ValidationException("Cari bulunamadı.", alan);
    }

    /// <summary>Pozitif, kolona sığan ve kolon ölçeğini (<paramref name="scale"/> ondalık) aşmayan tutar.</summary>
    public static void Amount(decimal amount, string alan, bool zeroFree = false, int scale = 4)
    {
        if (zeroFree ? amount < 0m : amount <= 0m)
            throw new ValidationException(zeroFree ? "Tutar negatif olamaz." : "Tutar pozitif olmalıdır.", alan);
        if (amount >= AmountUpperLimit) throw new ValidationException("Tutar çok büyük.", alan);
        EnsureMaxScale(amount, scale, alan);
    }

    /// <summary>
    /// F6.1b adversarial L1: kolon ölçeğini aşan ondalık reddedilir (DB sessizce yuvarlardı — 1000,00005 → 1000,0001;
    /// aynı anahtarla BİREBİR tekrar "farklı içerik" sayılıyordu). Yuvarlamak yerine red: istemci ne gönderdiyse o yazılır.
    /// </summary>
    public static void EnsureMaxScale(decimal value, int scale, string alan)
    {
        if (decimal.Round(value, scale) != value)
            throw new ValidationException($"En çok {scale} ondalık hane girilebilir.", alan);
    }

    /// <summary>İsteğe bağlı bilgi tutarı: negatif değil, kolona sığar.</summary>
    public static void InfoAmount(decimal? amount, string alan)
    {
        if (amount is { } t) Amount(t, alan, zeroFree: true);
    }

    /// <summary>Boş → TRY; aksi ISO koda indirgenir (biçimsiz → 400 <c>doviz</c>).</summary>
    public static string Currency(string? currency, string alan = "doviz")
    {
        if (string.IsNullOrWhiteSpace(currency)) return BaseCurrency;
        try { return ExchangeRateService.NormalizeCodeStrict(currency); }
        catch (ValidationException ex) when (ex.GetType() == typeof(ValidationException))
        { throw new ValidationException(ex.Message, alan); }
    }

    /// <summary>
    /// Açık kur: pozitif, sınır içinde; TEMEL PARADA (TRY) 1'den farklı kur REDDEDİLİR (DEVIR §5 — aksi halde baz tutar
    /// şişer ve dengeli görünen kayıt yanlış olur). Boş → döviz TRY ise 1, değilse <paramref name="defaultCurrency"/>.
    /// </summary>
    public static decimal Setup(decimal? exchangeRate, string currency, decimal defaultCurrency = 1m, string alan = "kur", int scale = 6)
    {
        if (exchangeRate is { } k)
        {
            if (k <= 0m) throw new ValidationException("Kur pozitif olmalıdır.", alan);
            if (k >= RateUpperLimit) throw new ValidationException("Kur çok büyük.", alan);
            EnsureMaxScale(k, scale, alan);
            if (currency == BaseCurrency && k != 1m)
                throw new ValidationException("Temel para (TRY) işleminde kur 1 olmalıdır.", alan);
            return k;
        }
        return currency == BaseCurrency ? 1m : defaultCurrency;
    }

    public static void Text(string? value, int maximum, string alan)
    {
        if (value is { } s && s.Trim().Length > maximum)
            throw new ValidationException($"En çok {maximum} karakter olabilir.", alan);
    }

    /// <summary>Sürüm zorunluluğu (tam değiştirme PUT'u).</summary>
    public static string Version(string? version)
        => string.IsNullOrWhiteSpace(version)
            ? throw new ValidationException("Kayıt sürümü (surum) zorunludur; kaydı yeniden açın.", "surum")
            : version;

    /// <summary>İki an aynı mı (PG timestamptz µs; .NET 100 ns — kırpılmış karşılaştırma). Boş gelen = karşılaştırılmaz.</summary>
    public static bool SameInstant(DateTimeOffset? record, DateTimeOffset? incoming)
        => incoming is not { } g
           || record is { } k && k.UtcTicks / TimeSpan.TicksPerMicrosecond == g.UtcTicks / TimeSpan.TicksPerMicrosecond;

    public static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
