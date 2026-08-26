using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.KdvRates;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.KdvRates;

/// <summary>KDV oranı master form post uçları. OperationsWrite (operasyonel yapılandırma).
/// Oran decimal alanı boş "" gelince 400 vermesin diye <c>string?</c> alınıp
/// <see cref="FormParse.Dec"/> ile ayrıştırılır (boşsa 0 → servis 0..1 doğrulamasına takılır).</summary>
public static class KdvRateEndpoints
{
    public static IEndpointRouteBuilder MapKdvRateEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/kdv-oranlari").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (KdvRateService svc,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? oran) =>
            await Run(() => svc.CreateAsync(new KdvRateInput
            {
                Kod = kod,
                Ad = ad,
                Oran = FormParse.Dec(oran) ?? 0m,
                Aktif = true
            }), "Kayıt eklendi."));

        grp.MapPost("/update", async (KdvRateService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? oran, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new KdvRateInput
            {
                Kod = kod,
                Ad = ad,
                Oran = FormParse.Dec(oran) ?? 0m,
                Aktif = aktif
            }), "Değişiklikler kaydedildi."));

        grp.MapPost("/delete", async (KdvRateService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id), "Kayıt silindi."));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action, string mesaj)
    {
        try { await action(); return Sonuc.Tamam("/kdv-oranlari", mesaj); }
        catch (ValidationException ex) { return Results.Redirect($"/kdv-oranlari?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
