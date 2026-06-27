using RentACar.Api.Common;
using RentACar.Api.Dtos;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Finance;

namespace RentACar.Api.Endpoints;

/// <summary>
/// Finans JSON API: tahsilat/ödeme/virman/ters kayıt + cari bakiye/ekstre + fatura. Tüm grup
/// FinanceWrite (Admin/Yönetici/Muhasebe) → yetkisiz 403, token yok 401. Para mantığı mevcut
/// (adversarial-incelenmiş) CashService/InvoiceService'tedir; API yalnız taşıma + DTO. Tenant
/// izolasyonu JWT→ApiIdentity→RLS; dengesizlik/geçersizlik → 400, idempotent ters → 400.
/// </summary>
public static class FinanceApi
{
    public static IEndpointRouteBuilder MapFinanceApi(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/v1/finance").WithTags("Finance").RequirePermission(Permission.FinanceWrite);

        // ---- Nakit ----
        grp.MapGet("/cash", async (CashService svc, CancellationToken ct) =>
            Results.Ok((await svc.ListAsync(ct)).Select(CashTransactionResponse.From)));

        grp.MapGet("/cash/{id:guid}", async (Guid id, CashService svc, CancellationToken ct) =>
            await svc.GetAsync(id, ct) is { } t ? Results.Ok(CashTransactionResponse.From(t)) : CashNotFound());

        grp.MapPost("/cash/collect", async (CashRequest req, CashService svc, CancellationToken ct) =>
        {
            var id = await svc.CollectAsync(req.ToInput(), ct);
            return Results.Created($"/api/v1/finance/cash/{id}", CashTransactionResponse.From((await svc.GetAsync(id, ct))!));
        });

        grp.MapPost("/cash/pay", async (CashRequest req, CashService svc, CancellationToken ct) =>
        {
            var id = await svc.PayAsync(req.ToInput(), ct);
            return Results.Created($"/api/v1/finance/cash/{id}", CashTransactionResponse.From((await svc.GetAsync(id, ct))!));
        });

        grp.MapPost("/cash/transfer", async (TransferRequest req, CashService svc, CancellationToken ct) =>
        {
            await svc.TransferAsync(req.Kaynak, req.Hedef, req.Tutar, req.Doviz, req.Kur, req.Aciklama, ct);
            return Results.Ok(new { transferred = true });
        });

        grp.MapPost("/cash/{id:guid}/reverse", async (Guid id, CashService svc, CancellationToken ct) =>
        {
            var reversalId = await svc.ReverseAsync(id, ct);
            return Results.Created($"/api/v1/finance/cash/{reversalId}", CashTransactionResponse.From((await svc.GetAsync(reversalId, ct))!));
        });

        // ---- Cari bakiye / ekstre ----
        grp.MapGet("/customers/{cariId:guid}/balance", async (Guid cariId, CashService svc, CancellationToken ct) =>
            Results.Ok(new { cariId, bakiye = await svc.GetCariBalanceAsync(cariId, ct) }));

        grp.MapGet("/customers/{cariId:guid}/statement", async (Guid cariId, CashService svc, CancellationToken ct) =>
            Results.Ok((await svc.GetStatementAsync(cariId, ct)).Select(LedgerEntryResponse.From)));

        // ---- Fatura ----
        grp.MapGet("/invoices", async (InvoiceService svc, CancellationToken ct) =>
            Results.Ok((await svc.ListAsync(ct)).Select(InvoiceResponse.From)));

        grp.MapGet("/invoices/{id:guid}", async (Guid id, InvoiceService svc, CancellationToken ct) =>
            await svc.GetAsync(id, ct) is { } i ? Results.Ok(InvoiceResponse.From(i))
                : Results.Json(new ApiError("not_found", "Fatura bulunamadı."), statusCode: StatusCodes.Status404NotFound));

        grp.MapPost("/invoices/from-rental/{rentalId:guid}", async (Guid rentalId, InvoiceService svc, CancellationToken ct, decimal? kdvRate = null) =>
        {
            // İstemci-kontrollü kdvRate sınırı (0..1) → geçersizse 400 (adversarial: negatif 500'e kaçmasın).
            if (kdvRate is < 0m or > 1m)
                throw new ValidationException("KDV oranı 0 ile 1 arasında olmalıdır.");
            var id = await svc.CreateFromRentalAsync(rentalId, kdvRate, ct);
            return Results.Created($"/api/v1/finance/invoices/{id}", InvoiceResponse.From((await svc.GetAsync(id, ct))!));
        });

        return app;
    }

    private static IResult CashNotFound()
        => Results.Json(new ApiError("not_found", "İşlem bulunamadı."), statusCode: StatusCodes.Status404NotFound);
}
