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
internal static class AracFinansOrtak
{
    /// <summary><c>numeric(19,4)</c>: 15 tam basamak.</summary>
    public const decimal TutarUstSiniri = 1_000_000_000_000_000m;

    /// <summary>Kur üst sınırı (kolonların en darı <c>numeric(19,4)</c>; makul tavan).</summary>
    public const decimal KurUstSiniri = 1_000_000m;

    public const string TemelDoviz = "TRY";

    /// <summary>Araç kimliği → (şube FK, şube metni); RLS kapsamlı tek sorgu (başka kiracının aracı görünmez).</summary>
    public static async Task<Dictionary<Guid, (Guid? SubeId, string? Sube)>> AracSubeleriAsync(
        IDbContextFactory<AppDbContext> dbf, IEnumerable<Guid> kimlikler, CancellationToken ct)
    {
        var ids = kimlikler.Distinct().ToList();
        if (ids.Count == 0) return [];
        await using var db = await dbf.CreateDbContextAsync(ct);
        var satirlar = await db.Vehicles.AsNoTracking().Where(v => ids.Contains(v.Id))
            .Select(v => new { v.Id, v.SubeId, v.Sube }).ToListAsync(ct);
        return satirlar.ToDictionary(v => v.Id, v => (v.SubeId, v.Sube));
    }

    /// <summary>
    /// Alt kaydın ARACIN şubesi üzerinden görülebilirliği (liste süzgeci). Araçsız kayıt yalnız şube kısıtsız
    /// kullanıcıya görünür: şubeye bağlı kullanıcı bağı olmayan kaydı "kendi şubesinin" sayamaz.
    /// </summary>
    public static bool Gorunur(BranchScope.BranchFilter f, Guid? vehicleId,
        IReadOnlyDictionary<Guid, (Guid? SubeId, string? Sube)> subeler)
    {
        if (f.Unrestricted) return true;
        if (vehicleId is not { } v || !subeler.TryGetValue(v, out var s)) return false;
        return BranchScope.InScope(f, s.SubeId, s.Sube);
    }

    /// <summary>Tekil kayıt kapısı: kapsam dışı → 403 <c>yetki_yok</c> (durum kontrolünden ÖNCE çağrılır).</summary>
    public static async Task KayitKapsamiAsync(
        IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici, Guid? vehicleId, CancellationToken ct)
    {
        var f = BranchScope.EffectiveFilter(kullanici);
        if (f.Unrestricted) return;
        var subeler = await AracSubeleriAsync(dbf, vehicleId is { } v ? [v] : [], ct);
        if (!Gorunur(f, vehicleId, subeler)) throw new YetkiYokException("Bu kayıt şube kapsamınız dışında.");
    }

    /// <summary>
    /// Yazımda araç: VAR olmalı (RLS kapsamlı; başka kiracının ya da olmayan kimlik → 400 <paramref name="alan"/>) ve
    /// çağıranın şube kapsamında olmalı (403). Araç boşsa şubeye bağlı kullanıcı reddedilir (kendi de göremeyeceği
    /// "yetim" kayıt açılmasın); <paramref name="zorunlu"/> ise herkes için 400.
    /// </summary>
    public static async Task AracYazimAsync(IDbContextFactory<AppDbContext> dbf, ICurrentUser kullanici,
        Guid? vehicleId, string alan, bool zorunlu, CancellationToken ct)
    {
        var f = BranchScope.EffectiveFilter(kullanici);
        if (vehicleId is not { } v || v == Guid.Empty)
        {
            if (zorunlu || !f.Unrestricted)
                throw new ValidationException(zorunlu
                    ? "Araç seçilmelidir."
                    : "Araç seçilmelidir (şubeye bağlı kullanıcı kendi şubesinin aracını seçmelidir).", alan);
            return;
        }
        var subeler = await AracSubeleriAsync(dbf, [v], ct);
        if (!subeler.TryGetValue(v, out var s)) throw new ValidationException("Araç bulunamadı.", alan);
        BranchScope.RequireInScope(kullanici, s.SubeId, s.Sube);
    }

    /// <summary>Cari bu kiracıda VAR olmalı (RLS kapsamlı; şifreli PII kolonlarına dokunmaz).</summary>
    public static async Task CariVarAsync(IDbContextFactory<AppDbContext> dbf, Guid? cariId, string alan,
        bool zorunlu, CancellationToken ct)
    {
        if (cariId is not { } c || c == Guid.Empty)
        {
            if (zorunlu) throw new ValidationException("Cari seçilmelidir.", alan);
            return;
        }
        await using var db = await dbf.CreateDbContextAsync(ct);
        if (!await db.Customers.AsNoTracking().AnyAsync(x => x.Id == c, ct))
            throw new ValidationException("Cari bulunamadı.", alan);
    }

    /// <summary>Pozitif, kolona sığan ve 4 ondalıkta sıfır kalmayan tutar.</summary>
    public static void Tutar(decimal tutar, string alan, bool sifirSerbest = false)
    {
        if (sifirSerbest ? tutar < 0m : tutar <= 0m)
            throw new ValidationException(sifirSerbest ? "Tutar negatif olamaz." : "Tutar pozitif olmalıdır.", alan);
        if (tutar >= TutarUstSiniri) throw new ValidationException("Tutar çok büyük.", alan);
        if (!sifirSerbest && Math.Round(tutar, 4, MidpointRounding.AwayFromZero) == 0m)
            throw new ValidationException("Tutar en az 0,0001 olmalıdır.", alan);
    }

    /// <summary>İsteğe bağlı bilgi tutarı: negatif değil, kolona sığar.</summary>
    public static void BilgiTutari(decimal? tutar, string alan)
    {
        if (tutar is { } t) Tutar(t, alan, sifirSerbest: true);
    }

    /// <summary>Boş → TRY; aksi ISO koda indirgenir (biçimsiz → 400 <c>doviz</c>).</summary>
    public static string Doviz(string? doviz, string alan = "doviz")
    {
        if (string.IsNullOrWhiteSpace(doviz)) return TemelDoviz;
        try { return KurService.NormalizeKodStrict(doviz); }
        catch (ValidationException ex) when (ex.GetType() == typeof(ValidationException))
        { throw new ValidationException(ex.Message, alan); }
    }

    /// <summary>
    /// Açık kur: pozitif, sınır içinde; TEMEL PARADA (TRY) 1'den farklı kur REDDEDİLİR (DEVIR §5 — aksi halde baz tutar
    /// şişer ve dengeli görünen kayıt yanlış olur). Boş → döviz TRY ise 1, değilse <paramref name="dovizVarsayilan"/>.
    /// </summary>
    public static decimal Kur(decimal? kur, string doviz, decimal dovizVarsayilan = 1m, string alan = "kur")
    {
        if (kur is { } k)
        {
            if (k <= 0m) throw new ValidationException("Kur pozitif olmalıdır.", alan);
            if (k >= KurUstSiniri) throw new ValidationException("Kur çok büyük.", alan);
            if (doviz == TemelDoviz && k != 1m)
                throw new ValidationException("Temel para (TRY) işleminde kur 1 olmalıdır.", alan);
            return k;
        }
        return doviz == TemelDoviz ? 1m : dovizVarsayilan;
    }

    public static void Metin(string? deger, int enFazla, string alan)
    {
        if (deger is { } s && s.Trim().Length > enFazla)
            throw new ValidationException($"En çok {enFazla} karakter olabilir.", alan);
    }

    /// <summary>Sürüm zorunluluğu (tam değiştirme PUT'u).</summary>
    public static string Surum(string? surum)
        => string.IsNullOrWhiteSpace(surum)
            ? throw new ValidationException("Kayıt sürümü (surum) zorunludur; kaydı yeniden açın.", "surum")
            : surum;

    /// <summary>İki an aynı mı (PG timestamptz µs; .NET 100 ns — kırpılmış karşılaştırma). Boş gelen = karşılaştırılmaz.</summary>
    public static bool AyniAn(DateTimeOffset? kayit, DateTimeOffset? gelen)
        => gelen is not { } g
           || kayit is { } k && k.UtcTicks / TimeSpan.TicksPerMicrosecond == g.UtcTicks / TimeSpan.TicksPerMicrosecond;

    public static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
