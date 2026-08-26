using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.PenaltyTypes;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.PenaltyTypes;

/// <summary>Ceza türü master form post uçları. OperationsWrite (operasyonel yapılandırma).
/// VarsayilanTutar opsiyonel decimal — boş "" gelince 400 vermesin diye <c>string?</c> alınıp
/// <see cref="FormParse.Dec"/> ile ayrıştırılır (boş → null).</summary>
public static class PenaltyTypeEndpoints
{
    public static IEndpointRouteBuilder MapPenaltyTypeEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/ceza-turleri").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (PenaltyTypeService svc,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? varsayilanTutar) =>
            await Run(() => svc.CreateAsync(new PenaltyTypeInput
            { Kod = kod, Ad = ad, VarsayilanTutar = FormParse.Dec(varsayilanTutar), Aktif = true }), "Kayıt eklendi."));

        grp.MapPost("/update", async (PenaltyTypeService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? varsayilanTutar, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, new PenaltyTypeInput
            { Kod = kod, Ad = ad, VarsayilanTutar = FormParse.Dec(varsayilanTutar), Aktif = aktif }), "Değişiklikler kaydedildi."));

        grp.MapPost("/delete", async (PenaltyTypeService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id), "Kayıt silindi."));

        return app;
    }

    private static async Task<IResult> Run(Func<Task> action, string mesaj)
    {
        try { await action(); return Sonuc.Tamam("/ceza-turleri", mesaj); }
        catch (ValidationException ex) { return Results.Redirect($"/ceza-turleri?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
