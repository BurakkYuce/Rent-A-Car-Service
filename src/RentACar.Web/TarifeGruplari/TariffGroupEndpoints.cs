using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.TarifeGruplari;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.TarifeGruplari;

/// <summary>Tarife (fiyat) grubu master form post uçları. OperationsWrite.
/// Şifre DÜZ gelir, serviste hash'lenir ve asla geri okunmaz; boş bırakılırsa mevcut korunur.</summary>
public static class TariffGroupEndpoints
{
    public static IEndpointRouteBuilder MapTariffGroupEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/tarife-gruplari").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (TariffGroupService svc, HttpRequest req) =>
            await Run(() => svc.CreateAsync(Build(req.Form)), "Kayıt eklendi."));

        grp.MapPost("/update", async (TariffGroupService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(() => svc.UpdateAsync(id, Build(req.Form)), "Değişiklikler kaydedildi."));

        grp.MapPost("/delete", async (TariffGroupService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id), "Kayıt silindi."));

        return app;
    }

    private static TarifeGrubuInput Build(IFormCollection f) => new()
    {
        Kod = f["kod"].ToString(),
        Ad = f["ad"].ToString(),
        Oran = FormParse.Dec(FormParse.Str(f, "oran")) ?? 0m,
        KullaniciAdi = FormParse.Str(f, "kullaniciAdi"),
        Sifre = FormParse.Str(f, "sifre"),
        Aktif = (FormParse.Str(f, "aktif") ?? "true") is "true" or "True"
    };

    private static async Task<IResult> Run(Func<Task> action, string message)
    {
        try { await action(); return Result.Ok("/tarife-gruplari", message); }
        catch (ValidationException ex) { return Results.Redirect($"/tarife-gruplari?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
