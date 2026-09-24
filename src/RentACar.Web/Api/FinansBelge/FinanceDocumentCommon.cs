using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Kur;
using RentACar.Domain.Common;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Api.FinansBelge;

/// <summary>
/// F8.1b — finans belge uçlarının (fatura, ceza, gider, gelen e-fatura, araç satışı) ortak uç kuralları. İş mantığı
/// YOK: sınırlar (<c>numeric(19,4)</c>, <c>numeric(19,6)</c>, varchar), döviz normalizasyonu, gövdedeki kimliklerin
/// kiracıda VAR olması (RLS kapsamlı) ve şube kapsamı.
/// <para><b>Şube kapsamı kuralı:</b> yalnız şubeye bağlı kullanıcı (Operatör, kullanıcı-bazlı finans izniyle)
/// kısıtlıdır. Belgenin kapsamı kendi şube kolonu (gider) ya da bağlı olduğu kiranın/aracın şubesidir (ceza, satış,
/// fatura). Hiçbir şubeye bağlanamayan kiracı-geneli belge (manuel fatura, gelen e-fatura) yalnız kısıtsız
/// kullanıcıya açıktır.</para>
/// </summary>
internal static class FinanceDocumentCommon
{
    /// <summary><c>numeric(19,4)</c>: 15 tam basamak.</summary>
    public const decimal AmountUpperLimit = 1_000_000_000_000_000m;
    /// <summary><c>numeric(19,6)</c>: 13 tam basamak (kur kolonları).</summary>
    public const decimal RateUpperLimit = 10_000_000_000_000m;

    public static readonly (string, string)[] SortRules = [("Geçersiz sıralama alanı", "sirala")];

    /// <summary>Pozitif, kolona sığan, en çok 2 ondalıklı (kuruş) tutar. #286 adversarial M2: 4 ondalık kabul edilince
    /// 0,001 net → servis kuruşa yuvarlayıp 0,00 tutarlı, seri numaralı ve DEĞİŞTİRİLEMEZ fatura kesiyordu.</summary>
    public static void Amount(decimal value, string field)
    {
        if (value <= 0m) throw new ValidationException("Tutar pozitif olmalıdır.", field);
        if (value >= AmountUpperLimit) throw new ValidationException("Tutar çok büyük.", field);
        Cents(value, field);
    }

    /// <summary>Para tutarı en çok 2 ondalık (kuruş) — belgeler kuruşla yazılır, sessiz yuvarlama yok.</summary>
    public static void Cents(decimal? value, string field)
    {
        if (value is { } v && decimal.Round(v, 2) != v)
            throw new ValidationException("Tutar en çok 2 ondalık (kuruş) olabilir.", field);
    }

    /// <summary>İsteğe bağlı tutar: verilirse <see cref="Amount"/> kuralları.</summary>
    public static void OptionalAmount(decimal? value, string field)
    {
        if (value is { } v) Amount(v, field);
    }

    /// <summary>İsteğe bağlı bilgi tutarı (vergi/hedef fiyat): yalnız kolon sınırı; negatiflik servis kuralı.</summary>
    public static void AmountLimit(decimal? value, string field)
    {
        if (value is { } v && Math.Abs(v) >= AmountUpperLimit) throw new ValidationException("Tutar çok büyük.", field);
        Cents(value, field);
    }

    /// <summary>KDV oranı KESİR (0,20 = %20): 0–1. "20" girişi %2000 KDV yazmasın.</summary>
    public static void VatRate(decimal? value, string field)
    {
        if (value is < 0m or > 1m)
            throw new ValidationException("KDV oranı kesir olmalıdır (0,20 = %20); 0 ile 1 arasında.", field);
    }

    /// <summary>Açık kur yalnız pozitif ve kolona sığan (TRY'de kur≠1 reddi servisteki KurCozucu'da).</summary>
    public static void Rate(decimal? value, string field = "kur")
    {
        if (value is <= 0m) throw new ValidationException("Kur pozitif olmalıdır (boş = otomatik).", field);
        if (value >= RateUpperLimit) throw new ValidationException("Kur çok büyük.", field);
    }

    /// <summary>Tutar × açık kur baz kolona sığmalı.</summary>
    public static void BaseLimit(decimal amount, decimal? rate, string field)
    {
        if (rate is { } r && amount * r >= AmountUpperLimit) throw new ValidationException("Tutar × kur çok büyük.", field);
    }

    public static void Text(string? value, int max, string field)
    {
        if (value is { Length: var n } && n > max)
            throw new ValidationException($"En çok {max} karakter olabilir.", field);
    }

    /// <summary>Boş → TRY; aksi halde saklanabilir ISO koda indirgenir ("TL" → "TRY"). Biçimsiz → alan hatası.</summary>
    public static string Currency(string? value, string field = "doviz")
    {
        if (string.IsNullOrWhiteSpace(value)) return "TRY";
        try { return KurService.NormalizeKodStrict(value); }
        catch (ValidationException ex) when (ex.GetType() == typeof(ValidationException))
        { throw new ValidationException(ex.Message, field); }
    }

    /// <summary>Alan'sız doğrulama hatasını alan'lı yeniden fırlatır (alt tipler korunur).</summary>
    public static void WithField(string field, Action validate)
    {
        try { validate(); }
        catch (ValidationException ex) when (ex.GetType() == typeof(ValidationException) && ex.Alan is null)
        { throw new ValidationException(ex.Message, field); }
    }

    public static string? Trimmed(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ------------------------------------------------------------------ kapsam

    /// <summary>Kullanıcı şubeye bağlı mı (kısıtlı kapsam).</summary>
    public static bool IsRestricted(ICurrentUser user) => !BranchScope.EffectiveFilter(user).Unrestricted;

    /// <summary>Kiracı-geneli belge: şubeye bağlı kullanıcı 403.</summary>
    public static void RequireUnrestricted(ICurrentUser user)
    {
        if (IsRestricted(user))
            throw new YetkiYokException("Bu kayıt şube kapsamınız dışında (kiracı geneli finans belgesi).");
    }

    /// <summary>Belgenin şube bilgisi: kira (çıkış şubesi) ya da araç (şube); ikisi de yoksa kiracı geneli.</summary>
    public readonly record struct BranchInfo(bool Known, Guid? SubeId, string? SubeText);

    public static bool InScope(ICurrentUser user, BranchInfo branch)
        => !IsRestricted(user) || (branch.Known && BranchScope.InScope(BranchScope.EffectiveFilter(user), branch.SubeId, branch.SubeText));

    public static void RequireInScope(ICurrentUser user, BranchInfo branch)
    {
        if (!InScope(user, branch)) throw new YetkiYokException("Bu kayıt şube kapsamınız dışında.");
    }

    /// <summary>Kira kimlikleri → çıkış şubesi (tek sorgu, RLS kapsamlı).</summary>
    public static async Task<Dictionary<Guid, BranchInfo>> RentalBranchesAsync(
        AppDbContext db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return [];
        return await db.Rentals.AsNoTracking().Where(r => list.Contains(r.Id))
            .Select(r => new { r.Id, r.CikisSubeId, r.CikisOfisi })
            .ToDictionaryAsync(r => r.Id, r => new BranchInfo(true, r.CikisSubeId, r.CikisOfisi), ct);
    }

    /// <summary>Araç kimlikleri → şube (tek sorgu, RLS kapsamlı).</summary>
    public static async Task<Dictionary<Guid, BranchInfo>> VehicleBranchesAsync(
        AppDbContext db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return [];
        return await db.Vehicles.AsNoTracking().Where(v => list.Contains(v.Id))
            .Select(v => new { v.Id, v.SubeId, v.Sube })
            .ToDictionaryAsync(v => v.Id, v => new BranchInfo(true, v.SubeId, v.Sube), ct);
    }

    // ------------------------------------------------------------------ varlık (gövdedeki kimlikler)

    /// <summary>Cari bu kiracıda var mı (RLS + tenant filtresi). Yoksa alan hatası — yetim defter kümesi yazılmaz.</summary>
    public static async Task RequireCustomerAsync(AppDbContext db, Guid? id, string field, CancellationToken ct)
    {
        if (id is not { } v) return;
        if (v == Guid.Empty || !await db.Customers.AsNoTracking().AnyAsync(c => c.Id == v, ct))
            throw new ValidationException("Cari bulunamadı.", field);
    }

    /// <summary>Araç bu kiracıda var mı; şube bilgisini döner (kapsam kontrolü çağıranda).</summary>
    public static async Task<BranchInfo?> RequireVehicleAsync(AppDbContext db, Guid? id, string field, CancellationToken ct)
    {
        if (id is not { } v) return null;
        var found = v == Guid.Empty ? null : await db.Vehicles.AsNoTracking().Where(x => x.Id == v)
            .Select(x => new { x.SubeId, x.Sube }).FirstOrDefaultAsync(ct);
        if (found is null) throw new ValidationException("Araç bulunamadı.", field);
        return new BranchInfo(true, found.SubeId, found.Sube);
    }

    /// <summary>Kira bu kiracıda var mı; çıkış şubesini döner.</summary>
    public static async Task<BranchInfo?> RequireRentalAsync(AppDbContext db, Guid? id, string field, CancellationToken ct)
    {
        if (id is not { } v) return null;
        var found = v == Guid.Empty ? null : await db.Rentals.AsNoTracking().Where(x => x.Id == v)
            .Select(x => new { x.CikisSubeId, x.CikisOfisi }).FirstOrDefaultAsync(ct);
        if (found is null) throw new ValidationException("Kira sözleşmesi bulunamadı.", field);
        return new BranchInfo(true, found.CikisSubeId, found.CikisOfisi);
    }
}
