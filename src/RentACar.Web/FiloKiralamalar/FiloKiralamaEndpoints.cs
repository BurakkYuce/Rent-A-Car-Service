using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.FiloKiralamalar;
using RentACar.Web.Identity;

namespace RentACar.Web.FiloKiralamalar;

/// <summary>Filo (uzun-dönem) kiralama form post uçları (roadmap L1). OperationsWrite.</summary>
public static class FiloKiralamaEndpoints
{
    public static IEndpointRouteBuilder MapFiloKiralamaEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/filo-kiralama").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (FiloKiralamaService svc, HttpRequest req) =>
        {
            var f = req.Form;
            string? S(string k) { var v = f[k].ToString(); return string.IsNullOrWhiteSpace(v) ? null : v; }
            var input = new FiloKiralamaInput
            {
                MusteriId = FormParse.Id(S("musteriId")) ?? Guid.Empty,
                VehicleId = FormParse.Id(S("vehicleId")) ?? Guid.Empty,
                BasTar = FormParse.Date(S("basTar")),
                SureAy = FormParse.Int(S("sureAy")) ?? 0,
                AylikUcret = FormParse.Dec(S("aylikUcret")) ?? 0m,
                KdvOrani = FormParse.Dec(S("kdvOrani")) ?? 0.20m,
                Doviz = S("doviz") ?? "TRY",
                Kur = FormParse.Dec(S("kur")) ?? 1m,
                ToplamKmLimiti = FormParse.Int(S("toplamKmLimiti")),
                DamgaVergisi = FormParse.Dec(S("damgaVergisi")),
                Aciklama = S("aciklama")
            };
            try { await svc.CreateAsync(input); return Results.Redirect("/filo-kiralama?ok=1"); }
            catch (ValidationException ex) { return Results.Redirect($"/filo-kiralama?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        grp.MapPost("/iptal", async (FiloKiralamaService svc, [FromForm] Guid id) =>
        {
            try { await svc.IptalAsync(id); return Results.Redirect("/filo-kiralama"); }
            catch (ValidationException ex) { return Results.Redirect($"/filo-kiralama?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        grp.MapPost("/tamamla", async (FiloKiralamaService svc, [FromForm] Guid id) =>
        {
            try { await svc.TamamlaAsync(id); return Results.Redirect("/filo-kiralama"); }
            catch (ValidationException ex) { return Results.Redirect($"/filo-kiralama?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        return app;
    }
}
