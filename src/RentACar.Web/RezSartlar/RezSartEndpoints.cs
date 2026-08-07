using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.RezSartlar;
using RentACar.Web.Identity;

namespace RentACar.Web.RezSartlar;

/// <summary>
/// Rez şartı (müşteri özel talebi) form post uçları. OperationsWrite. Opsiyonel tarih/id alanları
/// boş "" ile bind 400 vermesin diye IFormCollection'dan FormParse ile çevrilir (boş → null).
/// </summary>
public static class RezSartEndpoints
{
    public static IEndpointRouteBuilder MapRezSartEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/rez-sartlari").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (RezSartService svc, HttpRequest req) =>
            await Run(() => svc.CreateAsync(Build(req.Form))));

        grp.MapPost("/update", async (RezSartService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(() => svc.UpdateAsync(id, Build(req.Form))));

        grp.MapPost("/karsilandi", async (RezSartService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(() => svc.KarsilandiIsaretleAsync(id, FormParse.Str(req.Form, "teslimEden"))));

        grp.MapPost("/geri-al", async (RezSartService svc, [FromForm] Guid id) =>
            await Run(() => svc.KarsilamaGeriAlAsync(id)));

        grp.MapPost("/delete", async (RezSartService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static RezSartInput Build(IFormCollection f) => new()
    {
        MusteriId = FormParse.Id(FormParse.Str(f, "musteriId")) ?? Guid.Empty,
        Sart = f["sart"].ToString(),
        Grup = FormParse.Str(f, "grup"),
        BasTar = FormParse.Date(FormParse.Str(f, "basTar")),
        BitTar = FormParse.Date(FormParse.Str(f, "bitTar")),
        TalepTarihi = FormParse.Date(FormParse.Str(f, "talepTarihi")),
        KarsilamaTarihi = FormParse.Date(FormParse.Str(f, "karsilamaTarihi")),
        TeslimEden = FormParse.Str(f, "teslimEden"),
        ReservationId = FormParse.Id(FormParse.Str(f, "reservationId")),
        QuotationId = FormParse.Id(FormParse.Str(f, "quotationId"))
    };

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/rez-sartlari"); }
        catch (ValidationException ex) { return Results.Redirect($"/rez-sartlari?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
