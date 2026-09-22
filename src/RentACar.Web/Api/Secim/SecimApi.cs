using Microsoft.AspNetCore.Http.HttpResults;
using RentACar.Application.Authorization;
using RentACar.Application.Secim;
using RentACar.Domain.Enums;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Secim;

/// <summary>
/// <c>/api/ui/v1/secim/*</c> — F4–F5 formlarının typeahead/seçim kaynakları (F1.6). Hepsi <c>GET</c>,
/// hepsi <c>q</c> (isteğe bağlı) + <c>limit</c> (varsayılan ve en çok 20) alır; yalnız kimlik + etiket
/// (+ formun ihtiyacı olan operasyonel alan) döner — PII YOK. İzin kapısı <see cref="Permission.OperationsWrite"/>
/// (kira/rezervasyon/teklif formlarının yazma izni); servis katmanı aynı izni ikinci kez doğrular
/// (<see cref="SecimService"/>). Şube kapsamı servis katmanında.
/// <para><b>F4.4 istisnası — <c>musteri</c> ve <c>kur</c>: OperationsWrite VEYA FinanceWrite</b>
/// (<see cref="AuthExtensions.RequireAnyPermission{TBuilder}"/>; servis de aynı "herhangi biri" kuralını uygular).
/// Kira formunun sabit finans panelinde Muhasebe (FinanceWrite, OperationsWrite yok) dış hizmet tedarikçi
/// carisini arar ve kur bilgisini okur — Blazor sabit panelinde de yapabiliyordu. Dönen alanlar DEĞİŞMEDİ:
/// müşteri yalnız kimlik + görünen ad + tip (TC/telefon/e-posta YOK), kur ulusal TCMB verisi.</para>
/// <para>Blazor'daki karşılıkları (<c>CustomerService.ListSecimAsync</c>, <c>*.ListActiveAsync</c>) DEĞİŞMEDİ:
/// bunlar yetkisiz ve sınırsızdır; yeni yüzey o gevşekliği taşımasın diye ayrı, sınırlı yöntemlerden geçer.</para>
/// </summary>
public static class SecimApi
{
    public static RouteGroupBuilder MapSecimApi(this RouteGroupBuilder v1)
    {
        var kok = v1.MapGroup("/secim").WithTags("Seçim");
        // F4.4: finans paneli (Muhasebe) — OperationsWrite VEYA FinanceWrite; PII yok.
        kok.MapGet("/musteri", async Task<Ok<IReadOnlyList<MusteriSecimOgesi>>> (string? q, int? limit, SecimService s, CancellationToken ct)
            => TypedResults.Ok(await s.MusteriAsync(q, limit, ct)))
            .RequireAnyPermission(Permission.OperationsWrite, Permission.FinanceWrite);
        kok.MapGet("/kur", async Task<Ok<IReadOnlyList<KurSecimOgesi>>> (string? q, int? limit, SecimService s, CancellationToken ct)
            => TypedResults.Ok(await s.KurAsync(q, limit, ct)))
            .RequireAnyPermission(Permission.OperationsWrite, Permission.FinanceWrite);

        var g = kok.MapGroup("").RequirePermission(Permission.OperationsWrite);
        g.MapGet("/arac", async Task<Ok<IReadOnlyList<AracSecimOgesi>>> (string? q, int? limit, SecimService s, CancellationToken ct)
            => TypedResults.Ok(await s.AracAsync(q, limit, ct)));
        g.MapGet("/lokasyon", async Task<Ok<IReadOnlyList<LokasyonSecimOgesi>>> (string? q, int? limit, SecimService s, CancellationToken ct)
            => TypedResults.Ok(await s.LokasyonAsync(q, limit, ct)));
        g.MapGet("/ek-hizmet", async Task<Ok<IReadOnlyList<SecimOgesi>>> (string? q, int? limit, SecimService s, CancellationToken ct)
            => TypedResults.Ok(await s.EkHizmetAsync(q, limit, ct)));
        g.MapGet("/sigorta-urunu", async Task<Ok<IReadOnlyList<SecimOgesi>>> (string? q, int? limit, SecimService s, CancellationToken ct)
            => TypedResults.Ok(await s.SigortaUrunuAsync(q, limit, ct)));
        g.MapGet("/rezervasyon-kaynagi", async Task<Ok<IReadOnlyList<SecimOgesi>>> (string? q, int? limit, SecimService s, CancellationToken ct)
            => TypedResults.Ok(await s.RezervasyonKaynagiAsync(q, limit, ct)));
        g.MapGet("/ozel-kod", async Task<Ok<IReadOnlyList<SecimOgesi>>> (string? q, int? limit, SecimService s, CancellationToken ct)
            => TypedResults.Ok(await s.OzelKodAsync(q, limit, ct)));
        g.MapGet("/belge-sablonu", async Task<Ok<IReadOnlyList<SecimOgesi>>> (string? q, int? limit, BelgeTuru? tur, SecimService s, CancellationToken ct)
            => TypedResults.Ok(await s.BelgeSablonuAsync(q, limit, tur, ct)));
        g.MapGet("/personel", async Task<Ok<IReadOnlyList<SecimOgesi>>> (string? q, int? limit, SecimService s, CancellationToken ct)
            => TypedResults.Ok(await s.PersonelAsync(q, limit, ct)));
        g.MapGet("/sube", async Task<Ok<IReadOnlyList<SecimOgesi>>> (string? q, int? limit, SecimService s, CancellationToken ct)
            => TypedResults.Ok(await s.SubeAsync(q, limit, ct)));
        g.MapGet("/arac-grubu", async Task<Ok<IReadOnlyList<SecimOgesi>>> (string? q, int? limit, SecimService s, CancellationToken ct)
            => TypedResults.Ok(await s.AracGrubuAsync(q, limit, ct)));
        return kok;
    }
}
