using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.RateMatrices;
using RentACar.Domain.Enums;
using RentACar.Web.Identity;

namespace RentACar.Web.RateMatrices;

/// <summary>Tarife matrisi master form post uçları. OperationsWrite. Çok sayıda opsiyonel sayısal/
/// tarih alanı boş "" ile [FromForm] tipli bind 400 vermesin diye tüm form IFormCollection'dan
/// okunup FormParse/Enum.TryParse ile çevrilir (boş → null).</summary>
public static class RateMatrixEndpoints
{
    public static IEndpointRouteBuilder MapRateMatrixEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/tarife-matris").RequirePermission(Permission.OperationsWrite).DisableAntiforgery();

        grp.MapPost("/create", async (RateMatrixService svc, HttpRequest req) =>
            await Run(() => svc.CreateAsync(Build(req.Form))));

        grp.MapPost("/update", async (RateMatrixService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(() => svc.UpdateAsync(id, Build(req.Form))));

        grp.MapPost("/delete", async (RateMatrixService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static RateMatrixInput Build(IFormCollection f) => new()
    {
        Kod = f["kod"].ToString(),
        Ad = f["ad"].ToString(),
        Aciklama = Str(f, "aciklama"),
        Kanal = Str(f, "kanal"),
        Sube = Str(f, "sube"),
        Lokasyon = Str(f, "lokasyon"),
        AracGrupKod = Str(f, "aracGrupKod"),
        ParaBirimi = Str(f, "paraBirimi"),
        BasTar = FormParse.Date(Str(f, "basTar")),
        BitTar = FormParse.Date(Str(f, "bitTar")),
        Gun1 = FormParse.Dec(Str(f, "gun1")),
        Gun2 = FormParse.Dec(Str(f, "gun2")),
        Gun3 = FormParse.Dec(Str(f, "gun3")),
        Gun4 = FormParse.Dec(Str(f, "gun4")),
        Gun5 = FormParse.Dec(Str(f, "gun5")),
        Gun6 = FormParse.Dec(Str(f, "gun6")),
        Gun7 = FormParse.Dec(Str(f, "gun7")),
        MaxEsneklik = FormParse.Dec(Str(f, "maxEsneklik")),
        OnayDurumu = ParseEnum<TarifeOnayDurumu>(Str(f, "onayDurumu")) ?? TarifeOnayDurumu.Bekliyor,
        Onaylayan = Str(f, "onaylayan"),
        OnayZaman = FormParse.Date(Str(f, "onayZaman")),
        Aktif = (Str(f, "aktif") ?? "true") is "true" or "True"
    };

    private static string? Str(IFormCollection f, string key)
    {
        var v = f[key].ToString();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    private static T? ParseEnum<T>(string? s) where T : struct, Enum
        => Enum.TryParse<T>((s ?? string.Empty).Trim(), out var v) ? v : null;

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/tarife-matris"); }
        catch (ValidationException ex) { return Results.Redirect($"/tarife-matris?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
