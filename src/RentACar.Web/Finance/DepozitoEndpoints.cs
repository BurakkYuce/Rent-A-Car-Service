using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Domain.Enums;
using RentACar.Web.Identity;

namespace RentACar.Web.Finance;

/// <summary>Depozito (emanet) al/iade/mahsup uçları (roadmap I3). FinanceWrite.</summary>
public static class DepozitoEndpoints
{
    public static IEndpointRouteBuilder MapDepozitoEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/depozito").RequirePermission(Permission.FinanceWrite).AntiforgeryByEnv();

        grp.MapPost("/al", async (DepozitoService svc, HttpRequest req) =>
            await Run(req, (cari, tutar, hesap, doviz, kur, anahtar) => svc.AlAsync(cari, tutar, hesap, doviz, kur, null, anahtar)));

        grp.MapPost("/iade", async (DepozitoService svc, HttpRequest req) =>
            await Run(req, (cari, tutar, hesap, doviz, kur, anahtar) => svc.IadeAsync(cari, tutar, hesap, doviz, kur, null, anahtar)));

        grp.MapPost("/mahsup", async (DepozitoService svc, HttpRequest req) =>
            await Run(req, (cari, tutar, _, doviz, kur, anahtar) => svc.MahsupAsync(cari, tutar, doviz, kur, null, anahtar)));

        return app;
    }

    private static async Task<IResult> Run(HttpRequest req,
        Func<Guid, decimal, LedgerAccountType, string?, decimal, Guid?, Task<Guid>> action)
    {
        var f = req.Form;
        string? S(string k) { var v = f[k].ToString(); return string.IsNullOrWhiteSpace(v) ? null : v; }
        var cari = FormParse.Id(S("cariId")) ?? Guid.Empty;
        var tutar = FormParse.Dec(S("tutar")) ?? 0m;
        var hesap = string.Equals(S("hesap"), "Banka", StringComparison.OrdinalIgnoreCase) ? LedgerAccountType.Banka : LedgerAccountType.Kasa;
        var doviz = S("doviz") ?? "TRY";
        var kur = FormParse.Dec(S("kur")) ?? 1m;
        var anahtar = FormParse.Id(S("islemAnahtari"));
        try { await action(cari, tutar, hesap, doviz, kur, anahtar); return Results.Redirect("/depozito?ok=1"); }
        catch (ValidationException ex) { return Results.Redirect($"/depozito?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
