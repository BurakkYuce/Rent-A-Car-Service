using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Authorization;
using RentACar.Application.Bookings;
using RentACar.Application.Kabis;
using RentACar.Domain.Enums;
using RentACar.Web.Api.Rezervasyon;
using RentACar.Web.Api.Sistem;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Kira;

/// <summary>Kira ekranı KABİS bloğu (PII YOK — kimlik numarası hiçbir alanda dönmez).</summary>
public sealed record KabisDurumDto(
    bool Etkin, bool TeslimiEngelle, string Kurum, IReadOnlyList<KabisBildirimDto> Bildirimler, string? Uyari);

/// <summary>Tek bildirim kaydı. <c>Tur</c>: Kiralama/Iade/Iptal; <c>Durum</c>: Bekliyor/Bildirildi/Hata/GerekliDegil.</summary>
public sealed record KabisBildirimDto(
    string Tur, string Durum, string Kurum, DateTimeOffset OlayUtc, int DenemeSayisi, string? SonHata,
    string? DisReferans, bool Elle, string? Gerekce, DateTimeOffset? SonDenemeUtc, DateTimeOffset? SonrakiDenemeUtc,
    DateTimeOffset? BildirimUtc);

public sealed record KabisGerekceIstegi(string? Gerekce);

public sealed record KabisElleIstegi(string? Referans);

/// <summary>"Bildirilemeyen kiralar" satırı (şube kapsamlı).</summary>
public sealed record KabisSorunSatiri(
    Guid KiraId, string SozlesmeNo, string? Plaka, string Tur, string Durum, int DenemeSayisi, string? SonHata,
    DateTimeOffset OlayUtc, DateTimeOffset? SonDenemeUtc);

/// <summary>KABİS ayarları — şifre DÖNMEZ (yalnız <c>SifreTanimli</c>).</summary>
public sealed record KabisAyarDto(
    bool Etkin, string Kurum, string? Kullanici, bool SifreTanimli, bool TeslimiEngelle, string? Surum);

/// <summary>Tam değiştirme. <c>Sifre</c> boşsa mevcut korunur, <c>SifreTemizle</c> siler. Satır varsa <c>Surum</c> zorunlu.</summary>
public sealed record KabisAyarIstegi(
    bool Etkin, string? Kurum, string? Kullanici, string? Sifre, bool SifreTemizle, bool TeslimiEngelle, string? Surum)
{
    /// <summary>Şifre istek yazdırımlarında (log/iz) görünmez.</summary>
    public override string ToString()
        => $"KabisAyarIstegi {{ Etkin = {Etkin}, Kurum = {Kurum}, Kullanici = {Kullanici}, Sifre = ***, TeslimiEngelle = {TeslimiEngelle} }}";
}

public sealed record KabisBaglantiTestiDto(bool Ok, string Mesaj);

/// <summary>
/// KABİS uçları. <b>İş mantığı YOK</b> — <see cref="KabisNotificationService"/> / <see cref="KabisSettingsService"/>.
/// <list type="bullet">
/// <item><c>/kiralar/{id}/kabis</c>: okuma OperationsWrite veya FinanceWrite (kira okumalarıyla aynı); işlemler
/// (yeniden dene, gerekli değil, elle bildirildi) OperationsWrite. Üst kayıt kapısı ÖNCE: kapsam dışı kira 403,
/// yok/başka kiracı 404 — durum kontrolünden önce.</item>
/// <item><c>/kabis/bildirilemeyenler</c>: şube kapsamlı liste (panel sayacının dökümü).</item>
/// <item><c>/ayarlar/kabis</c>: ManageUsers; şifre hiçbir yanıtta yok; PUT tam değiştirme <c>surum</c> + 409.</item>
/// </list>
/// </summary>
public static class KabisApi
{
    public const string Tag = "KABİS";

    public static RouteGroupBuilder MapKabisApi(this RouteGroupBuilder v1)
    {
        var k = v1.MapGroup("/kiralar/{id:guid}/kabis").WithTags(Tag)
            .RequireAnyPermission(Permission.OperationsWrite, Permission.FinanceWrite);
        k.MapGet("", Status);
        var ow = k.MapGroup("").RequirePermission(Permission.OperationsWrite);
        ow.MapPost("/{tur}/yeniden-dene", Retry);
        ow.MapPost("/{tur}/gerekli-degil", NotRequired);
        ow.MapPost("/{tur}/elle-bildirildi", Manual);

        v1.MapGroup("/kabis").WithTags(Tag)
            .RequireAnyPermission(Permission.OperationsWrite, Permission.FinanceWrite)
            .MapGet("/bildirilemeyenler", Problems);

        var s = v1.MapGroup("/ayarlar/kabis").WithTags(SystemApiCommon.Tag).RequirePermission(Permission.ManageUsers);
        s.MapGet("", GetSettings);
        s.MapPut("", PutSettings);
        s.MapPost("/baglanti-testi", TestConnection);
        return v1;
    }

    private static ProblemHttpResult NotFound() => SystemApiCommon.NotFound("Kira sözleşmesi bulunamadı.");

    private static KabisNoticeType Type(string tur)
        => F5Shared.EnumAdi<KabisNoticeType>(tur, "tur")
           ?? throw new Application.Common.ValidationException("Bildirim türü zorunludur.", "tur");

    private static KabisDurumDto Dto(KabisRentalStatus s) => new(
        s.Etkin, s.TeslimiEngelle, s.Kurum.ToString(),
        s.Bildirimler.Select(b => new KabisBildirimDto(
            b.Tur.ToString(), b.Durum.ToString(), b.Kurum.ToString(), b.OlayUtc.ToUniversalTime(), b.DenemeSayisi,
            b.SonHata, b.DisReferans, b.Elle, b.Gerekce, b.SonDenemeUtc?.ToUniversalTime(),
            b.SonrakiDenemeUtc?.ToUniversalTime(), b.BildirimUtc?.ToUniversalTime())).ToList(),
        s.Uyari);

    // Üst kayıt kapısı: RentalService.GetAsync şube kapsamını uygular (kapsam dışı → 403), yoksa 404.
    private static async Task<Results<Ok<KabisDurumDto>, ProblemHttpResult>> Status(
        Guid id, RentalService rentals, KabisNotificationService kabis, CancellationToken ct)
        => await rentals.GetAsync(id, ct) is null ? NotFound() : TypedResults.Ok(Dto(await kabis.GetForRentalAsync(id, ct)));

    private static async Task<Results<Ok<KabisDurumDto>, ProblemHttpResult>> Retry(
        Guid id, string tur, RentalService rentals, KabisNotificationService kabis, CancellationToken ct)
    {
        if (await rentals.GetAsync(id, ct) is null) return NotFound();
        return TypedResults.Ok(Dto(await kabis.RetryAsync(id, Type(tur), ct)));
    }

    private static async Task<Results<Ok<KabisDurumDto>, ProblemHttpResult>> NotRequired(
        Guid id, string tur, KabisGerekceIstegi istek, RentalService rentals, KabisNotificationService kabis, CancellationToken ct)
    {
        if (await rentals.GetAsync(id, ct) is null) return NotFound();
        return TypedResults.Ok(Dto(await kabis.MarkNotRequiredAsync(id, Type(tur), istek.Gerekce, ct)));
    }

    private static async Task<Results<Ok<KabisDurumDto>, ProblemHttpResult>> Manual(
        Guid id, string tur, KabisElleIstegi istek, RentalService rentals, KabisNotificationService kabis, CancellationToken ct)
    {
        if (await rentals.GetAsync(id, ct) is null) return NotFound();
        return TypedResults.Ok(Dto(await kabis.MarkManuallyReportedAsync(id, Type(tur), istek.Referans, ct)));
    }

    private static async Task<Ok<IReadOnlyList<KabisSorunSatiri>>> Problems(KabisNotificationService kabis, CancellationToken ct)
        => TypedResults.Ok<IReadOnlyList<KabisSorunSatiri>>((await kabis.ListProblemsAsync(ct))
            .Select(r => new KabisSorunSatiri(r.KiraId, r.SozlesmeNo, r.Plaka, r.Tur.ToString(), r.Durum.ToString(),
                r.DenemeSayisi, r.SonHata, r.OlayUtc.ToUniversalTime(), r.SonDenemeUtc?.ToUniversalTime()))
            .ToList());

    private static async Task<Ok<KabisAyarDto>> GetSettings(KabisSettingsService svc, CancellationToken ct)
        => TypedResults.Ok(SettingsDto(await svc.GetAsync(ct)));

    private static async Task<Ok<KabisAyarDto>> PutSettings(KabisAyarIstegi i, KabisSettingsService svc, CancellationToken ct)
    {
        var (_, current) = await svc.GetAsync(ct);
        if (current is not null) SystemApiCommon.RequireVersion(i.Surum);
        var authority = F5Shared.EnumAdi<KabisAuthority>(i.Kurum, "kurum") ?? KabisAuthority.Emniyet;
        await svc.SaveAsync(new KabisSettingsInput
        {
            Etkin = i.Etkin, Kurum = authority, Kullanici = i.Kullanici, Sifre = i.Sifre, SifreTemizle = i.SifreTemizle,
            TeslimiEngelle = i.TeslimiEngelle,
        }, SystemApiCommon.Clean(i.Surum), ct);
        return TypedResults.Ok(SettingsDto(await svc.GetAsync(ct)));
    }

    private static async Task<Ok<KabisBaglantiTestiDto>> TestConnection(KabisSettingsService svc, CancellationToken ct)
    {
        var r = await svc.TestConnectionAsync(ct);
        return TypedResults.Ok(new KabisBaglantiTestiDto(r.Ok, r.Mesaj));
    }

    private static KabisAyarDto SettingsDto((KabisSettingsView View, string? Surum) x) => new(
        x.View.Etkin, x.View.Kurum.ToString(), x.View.Kullanici, x.View.SifreTanimli, x.View.TeslimiEngelle, x.Surum);
}
