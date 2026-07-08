using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.GelenEFaturalar;
using RentACar.Web.Identity;

namespace RentACar.Web.GelenEFaturalar;

/// <summary>Gelen e-Fatura triage form post uçları. FinanceWrite. Elle giriş + GİB-sync (stub) +
/// durum akışı (onayla/reddet/işle). Opsiyonel sayısal/tarih alanlar FormParse ile (boş → null/0).</summary>
public static class GelenEFaturaEndpoints
{
    public static IEndpointRouteBuilder MapGelenEFaturaEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/gelen-efatura").RequirePermission(Permission.FinanceWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (GelenEFaturaService svc, HttpRequest req) =>
            await Run(() => svc.CreateManualAsync(Build(req.Form))));

        grp.MapPost("/onayla", async (GelenEFaturaService svc, [FromForm] Guid id) =>
            await Run(() => svc.OnaylaAsync(id)));

        grp.MapPost("/reddet", async (GelenEFaturaService svc, [FromForm] Guid id, [FromForm] string? neden) =>
            await Run(() => svc.ReddetAsync(id, neden)));

        grp.MapPost("/isle", async (GelenEFaturaService svc, [FromForm] Guid id) =>
            await Run(() => svc.IsleAsync(id)));

        grp.MapPost("/sync", async (GelenEFaturaService svc, HttpRequest req) =>
        {
            var from = FormParse.Date(FormParse.Str(req.Form, "from")) ?? DateTimeOffset.Now.Date.AddMonths(-1);
            var to = FormParse.Date(FormParse.Str(req.Form, "to")) ?? DateTimeOffset.Now.Date;
            return await Run(() => svc.SyncFromGibAsync(from, to));
        });

        return app;
    }

    private static GelenEFaturaInput Build(IFormCollection f) => new()
    {
        Ettn = f["ettn"].ToString(),
        GonderenVkn = f["gonderenVkn"].ToString(),
        GonderenUnvan = f["gonderenUnvan"].ToString(),
        Tarih = FormParse.Date(FormParse.Str(f, "tarih")),
        NetTutar = FormParse.Dec(FormParse.Str(f, "netTutar")) ?? 0m,
        KdvTutar = FormParse.Dec(FormParse.Str(f, "kdvTutar")) ?? 0m,
        GenelToplam = FormParse.Dec(FormParse.Str(f, "genelToplam")) ?? 0m,
        Currency = FormParse.Str(f, "currency"),
        Aciklama = FormParse.Str(f, "aciklama")
    };

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/gelen-efatura"); }
        catch (ValidationException ex) { return Results.Redirect($"/gelen-efatura?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
