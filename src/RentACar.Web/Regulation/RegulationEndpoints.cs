using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Regulation;
using RentACar.Domain.Enums;
using RentACar.Web.Identity;

namespace RentACar.Web.Regulation;

/// <summary>Sigorta/MTV/Muayene kayıt form post uçları.</summary>
public static class RegulationEndpoints
{
    public static IEndpointRouteBuilder MapRegulationEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/regulasyon").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

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

        // MTV ödeme→defter (roadmap J1): FinanceWrite (mali işlem).
        var ode = app.MapGroup("/regulasyon-odeme").RequirePermission(Permission.FinanceWrite).AntiforgeryByEnv();
        ode.MapPost("/mtv", async (RegulationService svc, [FromForm] Guid id, [FromForm] string? hesap) =>
        {
            var h = string.Equals(hesap, "Banka", StringComparison.OrdinalIgnoreCase) ? LedgerAccountType.Banka : LedgerAccountType.Kasa;
            try { await svc.MtvOdeAsync(id, h); return Results.Redirect("/regulasyon?ok=1"); }
            catch (ValidationException ex) { return Results.Redirect($"/regulasyon?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        ode.MapPost("/muayene", async (RegulationService svc, [FromForm] Guid id, [FromForm] string? hesap, [FromForm] string? ceza) =>
        {
            var h = string.Equals(hesap, "Banka", StringComparison.OrdinalIgnoreCase) ? LedgerAccountType.Banka : LedgerAccountType.Kasa;
            var c = FormParse.Dec(ceza) ?? 0m;
            try { await svc.MuayeneOdeAsync(id, h, c); return Results.Redirect("/regulasyon?ok=1"); }
            catch (ValidationException ex) { return Results.Redirect($"/regulasyon?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        ode.MapPost("/sigorta", async (RegulationService svc, [FromForm] Guid id, [FromForm] string? hesap, [FromForm] string? zeyil) =>
        {
            var h = string.Equals(hesap, "Banka", StringComparison.OrdinalIgnoreCase) ? LedgerAccountType.Banka : LedgerAccountType.Kasa;
            var z = FormParse.Dec(zeyil) ?? 0m;
            try { await svc.SigortaOdeAsync(id, h, z); return Results.Redirect("/regulasyon?ok=1"); }
            catch (ValidationException ex) { return Results.Redirect($"/regulasyon?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        return app;
    }
}
