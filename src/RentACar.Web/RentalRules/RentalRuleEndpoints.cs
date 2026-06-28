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
        var grp = app.MapGroup("/kira-kurallari").RequirePermission(Permission.OperationsWrite).DisableAntiforgery();

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
        Aciklama = Str(f, "aciklama"),
        Kanal = Str(f, "kanal"),
        Sube = Str(f, "sube"),
        AracGrupKod = Str(f, "aracGrupKod"),
        MinGun = FormParse.Int(Str(f, "minGun")),
        MaxGun = FormParse.Int(Str(f, "maxGun")),
        Iskonto = FormParse.Dec(Str(f, "iskonto")),
        SonraOdeOran = FormParse.Dec(Str(f, "sonraOdeOran")),
        HediyeGun = FormParse.Int(Str(f, "hediyeGun")),
        KampanyaMi = (Str(f, "kampanyaMi")) is "true" or "True" or "on",
        KampanyaKodu = Str(f, "kampanyaKodu"),
        GecerlilikBas = FormParse.Date(Str(f, "gecerlilikBas")),
        GecerlilikBit = FormParse.Date(Str(f, "gecerlilikBit")),
        SartMetni = Str(f, "sartMetni"),
        Aktif = (Str(f, "aktif") ?? "true") is "true" or "True"
    };

    private static string? Str(IFormCollection f, string key)
    {
        var v = f[key].ToString();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/kira-kurallari"); }
        catch (ValidationException ex) { return Results.Redirect($"/kira-kurallari?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
