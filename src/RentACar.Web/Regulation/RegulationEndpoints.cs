using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Common;
using RentACar.Application.Regulation;
using RentACar.Domain.Enums;

namespace RentACar.Web.Regulation;

/// <summary>Sigorta/MTV/Muayene kayıt form post uçları.</summary>
public static class RegulationEndpoints
{
    public static IEndpointRouteBuilder MapRegulationEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/regulasyon").RequireAuthorization().DisableAntiforgery();

        grp.MapPost("/sigorta", async (RegulationService svc,
            [FromForm] Guid vehicleId, [FromForm] InsuranceType tip,
            [FromForm] DateTimeOffset baslangic, [FromForm] DateTimeOffset bitis, [FromForm] decimal prim,
            [FromForm] string? policeNo, [FromForm] string? firma, [FromForm] string? acenta) =>
        {
            try { await svc.AddInsuranceAsync(vehicleId, tip, baslangic, bitis, prim, policeNo, firma, acenta); return Results.Redirect("/regulasyon"); }
            catch (ValidationException ex) { return Results.Redirect($"/regulasyon?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        grp.MapPost("/mtv", async (RegulationService svc,
            [FromForm] Guid vehicleId, [FromForm] string donem, [FromForm] decimal tutar, [FromForm] DateTimeOffset vade) =>
        {
            try { await svc.AddMtvAsync(vehicleId, donem, tutar, vade); return Results.Redirect("/regulasyon"); }
            catch (ValidationException ex) { return Results.Redirect($"/regulasyon?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        grp.MapPost("/muayene", async (RegulationService svc,
            [FromForm] Guid vehicleId, [FromForm] DateTimeOffset muayeneTarihi, [FromForm] DateTimeOffset bitis, [FromForm] decimal ucret) =>
        {
            try { await svc.AddInspectionAsync(vehicleId, muayeneTarihi, bitis, ucret); return Results.Redirect("/regulasyon"); }
            catch (ValidationException ex) { return Results.Redirect($"/regulasyon?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        return app;
    }
}
