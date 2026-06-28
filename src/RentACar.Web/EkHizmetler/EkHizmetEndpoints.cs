using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.EkHizmetler;
using RentACar.Web.Identity;

namespace RentACar.Web.EkHizmetler;

/// <summary>Ek hizmet tanımı master form post uçları. OperationsWrite (operasyonel yapılandırma).
/// BirimUcret/KdvOrani decimal alanları boş "" gelince 400 vermesin diye <c>string?</c> alınıp
/// <see cref="FormParse.Dec"/> ile ayrıştırılır (KdvOrani boşsa servis varsayılanı 0.20 kullanılır).</summary>
public static class EkHizmetEndpoints
{
    public static IEndpointRouteBuilder MapEkHizmetEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/ek-hizmetler").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (EkHizmetTanimService svc,
            [FromForm] string kod, [FromForm] string ad,
            [FromForm] string? birimUcret, [FromForm] string? kdvOrani) =>
            await Run(() => svc.CreateAsync(new EkHizmetTanimInput
            {
                Kod = kod,
                Ad = ad,
                BirimUcret = FormParse.Dec(birimUcret) ?? 0m,
                KdvOrani = FormParse.Dec(kdvOrani) ?? 0.20m,
                Aktif = true
            })));

        grp.MapPost("/update", async (EkHizmetTanimService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad,
            [FromForm] string? birimUcret, [FromForm] string? kdvOrani, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new EkHizmetTanimInput
            {
                Kod = kod,
                Ad = ad,
                BirimUcret = FormParse.Dec(birimUcret) ?? 0m,
                KdvOrani = FormParse.Dec(kdvOrani) ?? 0.20m,
                Aktif = aktif
            })));

        grp.MapPost("/delete", async (EkHizmetTanimService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/ek-hizmetler"); }
        catch (ValidationException ex) { return Results.Redirect($"/ek-hizmetler?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
