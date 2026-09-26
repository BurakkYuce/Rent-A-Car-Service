using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Periods;
using RentACar.Web.Identity;

namespace RentACar.Web.Periods;

/// <summary>Dönem kapanışı uçları (roadmap D2). Kilitle/aç → FinanceWrite.</summary>
public static class PeriodClosingEndpoints
{
    public static IEndpointRouteBuilder MapPeriodClosingEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/donem-kapanis").RequirePermission(Permission.FinanceWrite).AntiforgeryByEnv();

        // PR-A: "Dönemi Kapat" artık TEK akış — kapanış fişini post et (Gelir/Gider→DonemSonucu) + dönemi kilitle.
        grp.MapPost("/kilitle", async (PeriodClosingVoucherService kapanis, HttpRequest req) =>
        {
            try
            {
                var date = FormParse.Date(req.Form["kapanisTarihi"].ToString())
                    ?? throw new ValidationException("Kapanış tarihi gerekli.");
                await kapanis.CloseAsync(date);
                return Results.Redirect("/donem-kapanis?ok=1");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/donem-kapanis?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        grp.MapPost("/ac", async (PeriodLockService svc) =>
        {
            await svc.UnlockAsync();
            return Results.Redirect("/donem-kapanis?ok=1");
        });

        return app;
    }
}
