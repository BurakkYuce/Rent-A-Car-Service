using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.DolulukFiyat;
using RentACar.Web.Identity;

namespace RentACar.Web.DolulukFiyat;

/// <summary>Doluluk fiyat kuralı master form post uçları (FAZ 3.A7). OperationsWrite.</summary>
public static class DolulukFiyatEndpoints
{
    public static IEndpointRouteBuilder MapDolulukFiyatEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/doluluk-kurallari").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (DolulukFiyatKuralService svc, HttpRequest req) =>
            await Run(() => svc.CreateAsync(Build(req.Form))));

        grp.MapPost("/update", async (DolulukFiyatKuralService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(() => svc.UpdateAsync(id, Build(req.Form))));

        grp.MapPost("/delete", async (DolulukFiyatKuralService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static DolulukFiyatKuralInput Build(IFormCollection f) => new()
    {
        Kod = f["kod"].ToString(),
        Ad = f["ad"].ToString(),
        AracGrupKod = FormParse.Str(f, "aracGrupKod"),
        EsikYuzde = FormParse.Int(FormParse.Str(f, "esikYuzde")) ?? 0,
        CarpanYuzde = FormParse.Dec(FormParse.Str(f, "carpanYuzde")) ?? 0m,
        GecerlilikBas = FormParse.Date(FormParse.Str(f, "gecerlilikBas")),
        GecerlilikBit = FormParse.Date(FormParse.Str(f, "gecerlilikBit")),
        Aktif = (FormParse.Str(f, "aktif") ?? "true") is "true" or "True"
    };

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/doluluk-kurallari"); }
        catch (ValidationException ex) { return Results.Redirect($"/doluluk-kurallari?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
