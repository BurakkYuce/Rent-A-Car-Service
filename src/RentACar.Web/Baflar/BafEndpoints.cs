using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Baflar;
using RentACar.Application.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.Baflar;

/// <summary>BAF (personel araç tahsis) form post uçları (roadmap L5). OperationsWrite.</summary>
public static class BafEndpoints
{
    public static IEndpointRouteBuilder MapBafEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/baf").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (BafService svc, HttpRequest req) =>
        {
            var f = req.Form;
            string? S(string k) { var v = f[k].ToString(); return string.IsNullOrWhiteSpace(v) ? null : v; }
            var input = new BafInput
            {
                PersonelId = FormParse.Id(S("personelId")) ?? Guid.Empty,
                VehicleId = FormParse.Id(S("vehicleId")) ?? Guid.Empty,
                CikisTarihi = FormParse.Date(S("cikisTarihi")),
                CikisKm = FormParse.Int(S("cikisKm")) ?? 0,
                CikisYakit = FormParse.Int(S("cikisYakit")),
                Sube = S("sube"),
                Aciklama = S("aciklama")
            };
            try { await svc.CreateAsync(input); return Results.Redirect("/baf?ok=1"); }
            catch (ValidationException ex) { return Results.Redirect($"/baf?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        grp.MapPost("/teslim-al", async (BafService svc, HttpRequest req) =>
        {
            var f = req.Form;
            var id = FormParse.Id(f["id"].ToString()) ?? Guid.Empty;
            var donusKm = FormParse.Int(f["donusKm"].ToString()) ?? 0;
            var donusYakit = FormParse.Int(f["donusYakit"].ToString());
            try { await svc.TeslimAlAsync(id, donusKm, donusYakit); return Results.Redirect("/baf"); }
            catch (ValidationException ex) { return Results.Redirect($"/baf?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        grp.MapPost("/iptal", async (BafService svc, [FromForm] Guid id) =>
        {
            try { await svc.IptalAsync(id); return Results.Redirect("/baf"); }
            catch (ValidationException ex) { return Results.Redirect($"/baf?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        return app;
    }
}
