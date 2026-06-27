using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Domain.Enums;
using RentACar.Web.Identity;

namespace RentACar.Web.Finance;

/// <summary>Nakit tahsilat/ödeme + virman + ters kayıt form post uçları.</summary>
public static class FinanceEndpoints
{
    /// <summary>Form "Kasa"/"Banka" metnini hesap tipine çevirir (varsayılan Kasa).</summary>
    private static LedgerAccountType ParseHesap(string? s)
        => string.Equals(s, "Banka", StringComparison.OrdinalIgnoreCase)
            ? LedgerAccountType.Banka : LedgerAccountType.Kasa;

    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/finans").RequirePermission(Permission.FinanceWrite).DisableAntiforgery();

        grp.MapPost("/tahsilat", async (CashService svc,
            [FromForm] Guid cariId, [FromForm] string? rentalId, [FromForm] decimal tutar,
            [FromForm] string? doviz, [FromForm] string? kur, [FromForm] string? aciklama,
            [FromForm] string? hesap, [FromForm] string? donus) =>
        {
            try
            {
                await svc.CollectAsync(new CashInput
                {
                    CariId = cariId, RentalId = FormParse.Id(rentalId), Tutar = tutar,
                    Doviz = string.IsNullOrWhiteSpace(doviz) ? "TRY" : doviz, Kur = FormParse.Dec(kur) ?? 1m,
                    Aciklama = aciklama, Hesap = ParseHesap(hesap)
                });
                return Results.Redirect(donus ?? $"/cariler/{cariId}/ekstre");
            }
            catch (ValidationException ex)
            {
                var url = donus ?? $"/cariler/{cariId}/ekstre";
                return Results.Redirect($"{url}?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        grp.MapPost("/odeme", async (CashService svc,
            [FromForm] Guid cariId, [FromForm] string? rentalId, [FromForm] decimal tutar,
            [FromForm] string? doviz, [FromForm] string? kur, [FromForm] string? aciklama,
            [FromForm] string? hesap, [FromForm] string? donus) =>
        {
            try
            {
                await svc.PayAsync(new CashInput
                {
                    CariId = cariId, RentalId = FormParse.Id(rentalId), Tutar = tutar,
                    Doviz = string.IsNullOrWhiteSpace(doviz) ? "TRY" : doviz, Kur = FormParse.Dec(kur) ?? 1m,
                    Aciklama = aciklama, Hesap = ParseHesap(hesap)
                });
                return Results.Redirect(donus ?? $"/cariler/{cariId}/ekstre");
            }
            catch (ValidationException ex)
            {
                var url = donus ?? $"/cariler/{cariId}/ekstre";
                return Results.Redirect($"{url}?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        grp.MapPost("/virman", async (CashService svc,
            [FromForm] string? kaynak, [FromForm] string? hedef, [FromForm] decimal tutar,
            [FromForm] string? aciklama) =>
        {
            try
            {
                await svc.TransferAsync(ParseHesap(kaynak), ParseHesap(hedef), tutar, aciklama: aciklama);
                return Results.Redirect("/kasa");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/kasa?hata={Uri.EscapeDataString(ex.Message)}");
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
