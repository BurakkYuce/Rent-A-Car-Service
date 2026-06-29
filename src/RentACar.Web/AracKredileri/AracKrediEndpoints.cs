using Microsoft.AspNetCore.Mvc;
using RentACar.Application.AracKredileri;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.AracKredileri;

/// <summary>Araç kredisi form post uçları (roadmap L4). OperationsWrite.</summary>
public static class AracKrediEndpoints
{
    public static IEndpointRouteBuilder MapAracKrediEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/arac-kredi").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (AracKrediService svc, HttpRequest req) =>
        {
            var f = req.Form;
            string? S(string k) { var v = f[k].ToString(); return string.IsNullOrWhiteSpace(v) ? null : v; }
            var input = new AracKrediInput
            {
                BankaAdi = S("bankaAdi"),
                VehicleId = FormParse.Id(S("vehicleId")),
                KrediTutari = FormParse.Dec(S("krediTutari")) ?? 0m,
                FaizOran = FormParse.Dec(S("faizOran")) ?? 0m,
                TaksitSayisi = FormParse.Int(S("taksitSayisi")) ?? 0,
                BaslangicTarihi = FormParse.Date(S("baslangicTarihi")),
                Doviz = S("doviz") ?? "TRY",
                Kur = FormParse.Dec(S("kur")) ?? 1m,
                Aciklama = S("aciklama")
            };
            try { await svc.CreateAsync(input); return Results.Redirect("/arac-kredi?ok=1"); }
            catch (ValidationException ex) { return Results.Redirect($"/arac-kredi?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        grp.MapPost("/taksit-ode", async (AracKrediService svc, [FromForm] Guid id) => await Durum(() => svc.TaksitOdeAsync(id)));
        grp.MapPost("/iptal", async (AracKrediService svc, [FromForm] Guid id) => await Durum(() => svc.IptalAsync(id)));

        return app;
    }

    private static async Task<IResult> Durum(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/arac-kredi"); }
        catch (ValidationException ex) { return Results.Redirect($"/arac-kredi?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
