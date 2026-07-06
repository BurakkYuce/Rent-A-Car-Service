using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.RentalRules;
using RentACar.Web.Identity;

namespace RentACar.Web.RentalRules;

/// <summary>Kiralama kuralı master form post uçları. OperationsWrite. Opsiyonel sayısal/tarih alanlar
/// boş "" ile bind 400 vermesin diye IFormCollection'dan FormParse ile çevrilir (boş → null).</summary>
public static class RentalRuleEndpoints
{
    public static IEndpointRouteBuilder MapRentalRuleEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/kira-kurallari").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (RentalRuleService svc, HttpRequest req) =>
            await Run(() => svc.CreateAsync(Build(req.Form))));

        grp.MapPost("/update", async (RentalRuleService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(() => svc.UpdateAsync(id, Build(req.Form))));

        grp.MapPost("/delete", async (RentalRuleService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static RentalRuleInput Build(IFormCollection f) => new()
    {
        Kod = f["kod"].ToString(),
        Ad = f["ad"].ToString(),
        Aciklama = FormParse.Str(f, "aciklama"),
        Kanal = FormParse.Str(f, "kanal"),
        Sube = FormParse.Str(f, "sube"),
        AracGrupKod = FormParse.Str(f, "aracGrupKod"),
        MinGun = FormParse.Int(FormParse.Str(f, "minGun")),
        MaxGun = FormParse.Int(FormParse.Str(f, "maxGun")),
        Iskonto = FormParse.Dec(FormParse.Str(f, "iskonto")),
        HaftaSonuFarkOran = FormParse.Dec(FormParse.Str(f, "haftaSonuFarkOran")),
        SonraOdeOran = FormParse.Dec(FormParse.Str(f, "sonraOdeOran")),
        HediyeGun = FormParse.Int(FormParse.Str(f, "hediyeGun")),
        KampanyaMi = (FormParse.Str(f, "kampanyaMi")) is "true" or "True" or "on",
        KampanyaKodu = FormParse.Str(f, "kampanyaKodu"),
        GecerlilikBas = FormParse.Date(FormParse.Str(f, "gecerlilikBas")),
        GecerlilikBit = FormParse.Date(FormParse.Str(f, "gecerlilikBit")),
        SartMetni = FormParse.Str(f, "sartMetni"),
        Aktif = (FormParse.Str(f, "aktif") ?? "true") is "true" or "True"
    };


    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/kira-kurallari"); }
        catch (ValidationException ex) { return Results.Redirect($"/kira-kurallari?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
