using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.EkHizmetler;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.EkHizmetler;

/// <summary>Ek hizmet tanımı master form post uçları. OperationsWrite (operasyonel yapılandırma).
/// BirimUcret/KdvOrani decimal alanları boş "" gelince 400 vermesin diye <c>string?</c> alınıp
/// <see cref="FormParse.Dec"/> ile ayrıştırılır (KdvOrani boşsa servis varsayılanı 0.20 kullanılır).</summary>
public static class AddOnEndpoints
{
    public static IEndpointRouteBuilder MapAddOnEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/ek-hizmetler").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (AddOnDefinitionService svc,
            [FromForm] string kod, [FromForm] string ad,
            [FromForm] string? birimUcret, [FromForm] string? kdvOrani,
            [FromForm] string? aciklama, [FromForm] string? maxGun) =>
            await Run(() => svc.CreateAsync(new EkHizmetTanimInput
            {
                Kod = kod,
                Ad = ad,
                BirimUcret = FormParse.Dec(birimUcret) ?? 0m,
                KdvOrani = FormParse.Dec(kdvOrani) ?? 0.20m,
                Aciklama = aciklama,
                MaxGun = FormParse.Int(maxGun),
                Aktif = true
            }), "Kayıt eklendi."));

        grp.MapPost("/update", async (AddOnDefinitionService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad,
            [FromForm] string? birimUcret, [FromForm] string? kdvOrani,
            [FromForm] string? aciklama, [FromForm] string? maxGun, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new EkHizmetTanimInput
            {
                Kod = kod,
                Ad = ad,
                BirimUcret = FormParse.Dec(birimUcret) ?? 0m,
                KdvOrani = FormParse.Dec(kdvOrani) ?? 0.20m,
                Aciklama = aciklama,
                MaxGun = FormParse.Int(maxGun),
                Aktif = aktif
            }), "Değişiklikler kaydedildi."));

        grp.MapPost("/delete", async (AddOnDefinitionService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id), "Kayıt silindi."));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action, string message)
    {
        try { await action(); return Result.Ok("/ek-hizmetler", message); }
        catch (ValidationException ex) { return Results.Redirect($"/ek-hizmetler?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
