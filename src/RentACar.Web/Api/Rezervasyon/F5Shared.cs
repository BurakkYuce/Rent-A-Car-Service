using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Locations;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Api.Kira;

namespace RentACar.Web.Api.Rezervasyon;

/// <summary>
/// F5.1 — rezervasyon fazı uçlarının (rezervasyon, teklif, takvim, müsaitlik, rez şartı, filo kiralama) ortak
/// yardımcıları. İş mantığı YOK; yalnız uç katmanı kuralları: görünen ad (KVKK tek kural
/// <see cref="CustomerView"/>), çıkış ofisi kapsamı, girdi sınırları, tarih dönüşümü.
/// </summary>
internal static class F5Shared
{
    public static ProblemHttpResult NotFound(string detail)
        => TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status404NotFound, title: "Bulunamadı");

    /// <summary>Boş/boşluk → null, aksi Trim (Blazor <c>FormParse.Str</c> ile aynı).</summary>
    public static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>DB'ye giden her an UTC (Npgsql timestamptz yalnız offset 0 kabul eder; +03:00 parametre sessizce kayar).</summary>
    public static DateTimeOffset Utc(DateTimeOffset an) => an.ToUniversalTime();

    public static DateTimeOffset? Utc(DateTimeOffset? an) => an?.ToUniversalTime();

    /// <summary>Sorgu tarihlerinde kabul edilen yıl aralığı (0001-01-01 gibi uç değerler 500 yerine 400 alan hatası).</summary>
    public const int MinQueryYear = 1900, MaxQueryYear = 2100;

    /// <summary>
    /// Takvim gününün İstanbul gece yarısı, UTC olarak (kira listesi süzgeciyle aynı kural). Yıl
    /// <see cref="MinQueryYear"/>…<see cref="MaxQueryYear"/> dışındaysa 400 <c>errors[field]</c> (#278 L1: 0001-01-01
    /// offset çevriminde taşıp 500 veriyordu).
    /// </summary>
    public static DateTimeOffset DayStart(DateOnly day, string field = "tarih")
    {
        EnsureQueryYear(day, field);
        return DayStartUtc(day);
    }

    private static void EnsureQueryYear(DateOnly day, string field)
    {
        if (day.Year is < MinQueryYear or > MaxQueryYear)
            throw new ValidationException($"Tarih {MinQueryYear} ile {MaxQueryYear} yılları arasında olmalıdır.", field);
    }

    /// <summary>Doğrulamasız gün başı — yalnız aralığı ÖNCEDEN doğrulanmış günün komşusu için (ör. bitiş + 1 gün).</summary>
    internal static DateTimeOffset DayStartUtc(DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TenantDay.Slice.GetUtcOffset(local)).ToUniversalTime();
    }

    /// <summary>
    /// F5.1 adversarial L4 — "duvar saati" (offset'i anlamsız, gün+saat İstanbul niyetiyle girilmiş an) → gerçek UTC an.
    /// <see cref="Application.Availability.AvailabilityService.Window"/> gün+saati offset 0 ile kurar (Blazor ekranı ve
    /// fiyat motorunun takvim-günü konvansiyonu); müsaitlik ÇAKIŞMA sorgusu ise gerçek an ister — 08:00 aranınca
    /// 08:00 İstanbul (05:00Z) sorgulanmalı, 08:00Z (11:00 İstanbul) değil.
    /// </summary>
    public static DateTimeOffset LocalToUtc(DateTimeOffset wall)
    {
        var local = DateTime.SpecifyKind(wall.DateTime, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TenantDay.Slice.GetUtcOffset(local)).ToUniversalTime();
    }

    /// <summary>Takvim günü aralığı: [gün başı, ertesi gün başı − 1 µs] (üst sınır GÜN DAHİL; repo &lt;= uygular).</summary>
    public static (DateTimeOffset? Min, DateTimeOffset? Max) DayRange(
        DateOnly? min, DateOnly? max, string minField = "bas", string maxField = "bit")
    {
        if (max is { } m) EnsureQueryYear(m, maxField);
        return (min is { } a ? DayStart(a, minField) : null, max is { } b ? DayStartUtc(b.AddDays(1)).AddMicroseconds(-1) : null);
    }

    /// <summary>Enum ADI (büyük/küçük harf duyarsız); sayı ya da tanımsız ad 400 (sessizce "filtre yok"a düşmez).</summary>
    public static T? EnumAdi<T>(string? value, string alan) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var d = value.Trim();
        // Birebir ad eşleşmesi: Enum.TryParse virgüllü (flags) "Musait,Kirada" değerini de kabul ediyordu (#278 L2).
        var exactName = Enum.GetNames<T>().FirstOrDefault(n => string.Equals(n, d, StringComparison.OrdinalIgnoreCase));
        if (exactName is not null)
            return Enum.Parse<T>(exactName);
        throw new ValidationException($"Geçersiz {alan} değeri. İzin verilenler: {string.Join(", ", Enum.GetNames<T>())}.", alan);
    }

    // ------------------------------------------------------------------ kapsam

    /// <summary>
    /// Çıkış ofisi GİRİŞ NOKTASINDA şube kapsamından geçer — <c>RentalService.CreateDirectAsync</c>'teki kural (F4.1
    /// adversarial M1): ofis → Location → türetilmiş şube → <see cref="BranchScope.RequireInScope(ICurrentUser, Guid?, string?)"/>
    /// (kapsam dışı → 403 <c>yetki_yok</c>). Şubeye bağlı kullanıcı ofissiz kayıt açamaz (kendisinin de göremeyeceği
    /// "yetim" kayıt). Blazor rezervasyon/teklif formları bu kontrolü yapmıyor; yeni yüzey o açığı taşımaz.
    /// </summary>
    public static async Task PickupOfficeScopeAsync(
        ILocationRepository locations, ICurrentUser user, string? office, CancellationToken ct)
    {
        var o = Nz(office);
        if (o is null)
        {
            if (!BranchScope.EffectiveFilter(user).Unrestricted)
                throw new ValidationException(
                    "Çıkış ofisi zorunludur (şubeye bağlı kullanıcı kendi şubesinin ofisini seçmelidir).", "cikisOfisi");
            return;
        }
        var branchId = (await locations.FindByNameAsync(o, ct))?.SubeId;
        BranchScope.RequireInScope(user, branchId, o);
    }

    // ------------------------------------------------------------------ görünen adlar (PII tek kural)

    /// <summary>Liste yüzeyinde cari görünümü: ad + cep tel, <see cref="CustomerView"/> kurallarıyla (AnonimAd/AnonimTelefon).</summary>
    public sealed record CariGorunum(string Ad, string? CepTel);

    /// <summary>
    /// Kimlik kümesi → görünen ad + cep tel, TEK sorguda. Şifreli PII kolonlarına (TC/ehliyet/pasaport) HİÇ dokunmaz
    /// (<c>CustomerService.ListAsync</c> her cari için cipher çözer). RLS + tenant filtresi kapsamlı.
    /// </summary>
    public static async Task<Dictionary<Guid, CariGorunum>> CustomersAsync(
        IDbContextFactory<AppDbContext> f, IEnumerable<Guid> identities, CancellationToken ct)
    {
        var ids = identities.Distinct().ToList();
        if (ids.Count == 0) return [];
        await using var db = await f.CreateDbContextAsync(ct);
        var rows = await db.Customers.AsNoTracking().Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.Tip, c.Unvan, c.Ad, c.Soyad, c.CepTel, c.AnonimAd, c.AnonimTelefon })
            .ToListAsync(ct);
        return rows.ToDictionary(c => c.Id, c =>
        {
            var m = new Customer
            {
                Tip = c.Tip, Unvan = c.Unvan, Ad = c.Ad, Soyad = c.Soyad, CepTel = c.CepTel,
                AnonimAd = c.AnonimAd, AnonimTelefon = c.AnonimTelefon,
            };
            return new CariGorunum(CustomerView.TarafAdi(m), CustomerView.Phone(m));
        });
    }

    public static string CustomerName(Dictionary<Guid, CariGorunum> customers, Guid id)
        => customers.TryGetValue(id, out var c) ? c.Ad : "—";

    /// <summary>Araç kimliği → plaka (tek sorgu). Kayıt zaten kapsam kapısından geçtiği için araç kapsamı aranmaz.</summary>
    public static async Task<Dictionary<Guid, string>> PlatesAsync(
        IDbContextFactory<AppDbContext> f, IEnumerable<Guid> identities, CancellationToken ct)
    {
        var ids = identities.Distinct().ToList();
        if (ids.Count == 0) return [];
        await using var db = await f.CreateDbContextAsync(ct);
        return await db.Vehicles.AsNoTracking().Where(v => ids.Contains(v.Id))
            .Select(v => new { v.Id, v.Plaka }).ToDictionaryAsync(v => v.Id, v => v.Plaka, ct);
    }

    public static string Plate(Dictionary<Guid, string> plates, Guid id) => plates.GetValueOrDefault(id, "—");

    /// <summary>
    /// Sayfalama + beyaz liste sıralaması (bellekte; servisler liste döndürüyor). <c>sirala</c> yoksa servisin sırası
    /// korunur. Bilinmeyen alan 400 (<see cref="SortFieldMap{T}"/>).
    /// </summary>
    public static Sayfa<T> Paginate<T>(IReadOnlyList<T> rows, SortFieldMap<T> map, int? page, int? size, string? sort)
    {
        var request = new ListeIstegi(page ?? 1, size ?? 50, sort);
        IEnumerable<T> sorted = request.Sirala is null ? rows : map.Apply(rows.AsQueryable(), request.Sirala);
        var records = request.Atla >= rows.Count ? [] : sorted.Skip((int)request.Atla).Take(request.Boyut).ToList();
        return new Sayfa<T>(records, rows.Count, request.Sayfa, request.Boyut);
    }

    public static readonly (string, string)[] SortRules = [("Geçersiz sıralama alanı", "sirala")];
}
