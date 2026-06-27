using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Web.Identity;

namespace RentACar.Web.Finance;

/// <summary>Nakit tahsilat + ters kayıt form post uçları.</summary>
public static class FinanceEndpoints
{
    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/finans").RequirePermission(Permission.FinanceWrite).DisableAntiforgery();

        grp.MapPost("/tahsilat", async (CashService svc,
            [FromForm] Guid cariId, [FromForm] Guid? rentalId, [FromForm] decimal tutar,
            [FromForm] string? doviz, [FromForm] decimal? kur, [FromForm] string? aciklama,
            [FromForm] string? donus) =>
        {
            try
            {
                await svc.CollectAsync(new CashInput
                {
                    CariId = cariId, RentalId = rentalId, Tutar = tutar,
                    Doviz = string.IsNullOrWhiteSpace(doviz) ? "TRY" : doviz, Kur = kur ?? 1m, Aciklama = aciklama
                });
                return Results.Redirect(donus ?? $"/cariler/{cariId}/ekstre");
            }
            catch (ValidationException ex)
            {
                var url = donus ?? $"/cariler/{cariId}/ekstre";
                return Results.Redirect($"{url}?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        grp.MapPost("/tahsilat/ters", async (CashService svc, [FromForm] Guid id, [FromForm] Guid cariId) =>
        {
            try { await svc.ReverseAsync(id); return Results.Redirect($"/cariler/{cariId}/ekstre"); }
            catch (ValidationException ex) { return Results.Redirect($"/cariler/{cariId}/ekstre?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        grp.MapPost("/fatura", async (InvoiceService svc, [FromForm] Guid rentalId) =>
        {
            try { await svc.CreateFromRentalAsync(rentalId); return Results.Redirect($"/kiralar/{rentalId}"); }
            catch (ValidationException ex) { return Results.Redirect($"/kiralar/{rentalId}?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        return app;
    }
}
