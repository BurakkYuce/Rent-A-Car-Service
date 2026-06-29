using Microsoft.AspNetCore.Mvc;
using RentACar.Application.AracSiparisleri;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.AracSiparisleri;

/// <summary>Araç sipariş/tedarik form post uçları (roadmap L3). OperationsWrite.</summary>
public static class AracSiparisEndpoints
{
    public static IEndpointRouteBuilder MapAracSiparisEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/arac-siparis").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (AracSiparisService svc, HttpRequest req) =>
        {
            var f = req.Form;
            string? S(string k) { var v = f[k].ToString(); return string.IsNullOrWhiteSpace(v) ? null : v; }
            var input = new AracSiparisInput
            {
                Tedarikci = S("tedarikci"),
                SiparisTarihi = FormParse.Date(S("siparisTarihi")),
                BeklenenTeslim = FormParse.Date(S("beklenenTeslim")),
                Marka = S("marka"),
                Tip = S("tip"),
                Grup = S("grup"),
                Adet = FormParse.Int(S("adet")) ?? 1,
                BirimFiyat = FormParse.Dec(S("birimFiyat")) ?? 0m,
                Doviz = S("doviz") ?? "TRY",
                Kur = FormParse.Dec(S("kur")) ?? 1m,
                Aciklama = S("aciklama")
            };
            try { await svc.CreateAsync(input); return Results.Redirect("/arac-siparis?ok=1"); }
            catch (ValidationException ex) { return Results.Redirect($"/arac-siparis?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        grp.MapPost("/onayla", async (AracSiparisService svc, [FromForm] Guid id) => await Durum(() => svc.OnaylaAsync(id)));
        grp.MapPost("/teslim-al", async (AracSiparisService svc, [FromForm] Guid id) => await Durum(() => svc.TeslimAlAsync(id)));
        grp.MapPost("/iptal", async (AracSiparisService svc, [FromForm] Guid id) => await Durum(() => svc.IptalAsync(id)));

        return app;
    }

    private static async Task<IResult> Durum(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/arac-siparis"); }
        catch (ValidationException ex) { return Results.Redirect($"/arac-siparis?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
