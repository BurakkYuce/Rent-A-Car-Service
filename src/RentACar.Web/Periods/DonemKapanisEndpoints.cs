using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Periods;
using RentACar.Web.Identity;

namespace RentACar.Web.Periods;

/// <summary>Dönem kapanışı uçları (roadmap D2). Kilitle/aç → FinanceWrite.</summary>
public static class DonemKapanisEndpoints
{
    public static IEndpointRouteBuilder MapDonemKapanisEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/donem-kapanis").RequirePermission(Permission.FinanceWrite).DisableAntiforgery();

        grp.MapPost("/kilitle", async (DonemKilidiService svc, HttpRequest req) =>
        {
            try
            {
                var tarih = FormParse.Date(req.Form["kapanisTarihi"].ToString())
                    ?? throw new ValidationException("Kapanış tarihi gerekli.");
                await svc.LockAsync(tarih);
                return Results.Redirect("/donem-kapanis?ok=1");
            }
            catch (ValidationException ex)
            {
                return Results.Redirect($"/donem-kapanis?hata={Uri.EscapeDataString(ex.Message)}");
            }
        });

        grp.MapPost("/ac", async (DonemKilidiService svc) =>
        {
            await svc.UnlockAsync();
            return Results.Redirect("/donem-kapanis?ok=1");
        });

        return app;
    }
}
