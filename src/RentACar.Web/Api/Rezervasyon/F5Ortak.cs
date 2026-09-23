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
/// <see cref="MusteriGorunumu"/>), çıkış ofisi kapsamı, girdi sınırları, tarih dönüşümü.
/// </summary>
internal static class F5Ortak
{
    public static ProblemHttpResult Bulunamadi(string detay)
        => TypedResults.Problem(detail: detay, statusCode: StatusCodes.Status404NotFound, title: "Bulunamadı");

    /// <summary>Boş/boşluk → null, aksi Trim (Blazor <c>FormParse.Str</c> ile aynı).</summary>
    public static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>DB'ye giden her an UTC (Npgsql timestamptz yalnız offset 0 kabul eder; +03:00 parametre sessizce kayar).</summary>
    public static DateTimeOffset Utc(DateTimeOffset an) => an.ToUniversalTime();

    public static DateTimeOffset? Utc(DateTimeOffset? an) => an?.ToUniversalTime();

    /// <summary>Takvim gününün İstanbul gece yarısı, UTC olarak (kira listesi süzgeciyle aynı kural).</summary>
    public static DateTimeOffset GunBasi(DateOnly gun)
    {
        var yerel = gun.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(yerel, TenantGun.Dilim.GetUtcOffset(yerel)).ToUniversalTime();
    }

    /// <summary>Takvim günü aralığı: [gün başı, ertesi gün başı − 1 µs] (üst sınır GÜN DAHİL; repo &lt;= uygular).</summary>
    public static (DateTimeOffset? Min, DateTimeOffset? Max) GunAraligi(DateOnly? min, DateOnly? max)
        => (min is { } a ? GunBasi(a) : null, max is { } b ? GunBasi(b.AddDays(1)).AddMicroseconds(-1) : null);

    /// <summary>Enum ADI (büyük/küçük harf duyarsız); sayı ya da tanımsız ad 400 (sessizce "filtre yok"a düşmez).</summary>
    public static T? EnumAdi<T>(string? deger, string alan) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(deger)) return null;
        var d = deger.Trim();
        if (!char.IsDigit(d[0]) && d[0] != '-' && Enum.TryParse<T>(d, ignoreCase: true, out var v) && Enum.IsDefined(v))
            return v;
        throw new ValidationException($"Geçersiz {alan} değeri. İzin verilenler: {string.Join(", ", Enum.GetNames<T>())}.", alan);
    }

    // ------------------------------------------------------------------ kapsam

    /// <summary>
    /// Çıkış ofisi GİRİŞ NOKTASINDA şube kapsamından geçer — <c>RentalService.CreateDirectAsync</c>'teki kural (F4.1
    /// adversarial M1): ofis → Location → türetilmiş şube → <see cref="BranchScope.RequireInScope(ICurrentUser, Guid?, string?)"/>
    /// (kapsam dışı → 403 <c>yetki_yok</c>). Şubeye bağlı kullanıcı ofissiz kayıt açamaz (kendisinin de göremeyeceği
    /// "yetim" kayıt). Blazor rezervasyon/teklif formları bu kontrolü yapmıyor; yeni yüzey o açığı taşımaz.
    /// </summary>
    public static async Task CikisOfisiKapsamiAsync(
        ILocationRepository lokasyonlar, ICurrentUser kullanici, string? ofis, CancellationToken ct)
    {
        var o = Nz(ofis);
        if (o is null)
        {
            if (!BranchScope.EffectiveFilter(kullanici).Unrestricted)
                throw new ValidationException(
                    "Çıkış ofisi zorunludur (şubeye bağlı kullanıcı kendi şubesinin ofisini seçmelidir).", "cikisOfisi");
            return;
        }
        var subeId = (await lokasyonlar.FindByAdAsync(o, ct))?.SubeId;
        BranchScope.RequireInScope(kullanici, subeId, o);
    }

    // ------------------------------------------------------------------ görünen adlar (PII tek kural)

    /// <summary>Liste yüzeyinde cari görünümü: ad + cep tel, <see cref="MusteriGorunumu"/> kurallarıyla (AnonimAd/AnonimTelefon).</summary>
    public sealed record CariGorunum(string Ad, string? CepTel);

    /// <summary>
    /// Kimlik kümesi → görünen ad + cep tel, TEK sorguda. Şifreli PII kolonlarına (TC/ehliyet/pasaport) HİÇ dokunmaz
    /// (<c>CustomerService.ListAsync</c> her cari için cipher çözer). RLS + tenant filtresi kapsamlı.
    /// </summary>
    public static async Task<Dictionary<Guid, CariGorunum>> CarilerAsync(
        IDbContextFactory<AppDbContext> f, IEnumerable<Guid> kimlikler, CancellationToken ct)
    {
        var ids = kimlikler.Distinct().ToList();
        if (ids.Count == 0) return [];
        await using var db = await f.CreateDbContextAsync(ct);
        var satirlar = await db.Customers.AsNoTracking().Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.Tip, c.Unvan, c.Ad, c.Soyad, c.CepTel, c.AnonimAd, c.AnonimTelefon })
            .ToListAsync(ct);
        return satirlar.ToDictionary(c => c.Id, c =>
        {
            var m = new Customer
            {
                Tip = c.Tip, Unvan = c.Unvan, Ad = c.Ad, Soyad = c.Soyad, CepTel = c.CepTel,
                AnonimAd = c.AnonimAd, AnonimTelefon = c.AnonimTelefon,
            };
            return new CariGorunum(MusteriGorunumu.TarafAdi(m), MusteriGorunumu.Telefon(m));
        });
    }

    public static string CariAdi(Dictionary<Guid, CariGorunum> cariler, Guid id)
        => cariler.TryGetValue(id, out var c) ? c.Ad : "—";

    /// <summary>Araç kimliği → plaka (tek sorgu). Kayıt zaten kapsam kapısından geçtiği için araç kapsamı aranmaz.</summary>
    public static async Task<Dictionary<Guid, string>> PlakalarAsync(
        IDbContextFactory<AppDbContext> f, IEnumerable<Guid> kimlikler, CancellationToken ct)
    {
        var ids = kimlikler.Distinct().ToList();
        if (ids.Count == 0) return [];
        await using var db = await f.CreateDbContextAsync(ct);
        return await db.Vehicles.AsNoTracking().Where(v => ids.Contains(v.Id))
            .Select(v => new { v.Id, v.Plaka }).ToDictionaryAsync(v => v.Id, v => v.Plaka, ct);
    }

    public static string Plaka(Dictionary<Guid, string> plakalar, Guid id) => plakalar.GetValueOrDefault(id, "—");

    /// <summary>
    /// Sayfalama + beyaz liste sıralaması (bellekte; servisler liste döndürüyor). <c>sirala</c> yoksa servisin sırası
    /// korunur. Bilinmeyen alan 400 (<see cref="SiralamaHaritasi{T}"/>).
    /// </summary>
    public static Sayfa<T> Sayfala<T>(IReadOnlyList<T> satirlar, SiralamaHaritasi<T> harita, int? sayfa, int? boyut, string? sirala)
    {
        var istek = new ListeIstegi(sayfa ?? 1, boyut ?? 50, sirala);
        IEnumerable<T> sirali = istek.Sirala is null ? satirlar : harita.Uygula(satirlar.AsQueryable(), istek.Sirala);
        var kayitlar = istek.Atla >= satirlar.Count ? [] : sirali.Skip((int)istek.Atla).Take(istek.Boyut).ToList();
        return new Sayfa<T>(kayitlar, satirlar.Count, istek.Sayfa, istek.Boyut);
    }

    public static readonly (string, string)[] SiralamaKurallari = [("Geçersiz sıralama alanı", "sirala")];
}
