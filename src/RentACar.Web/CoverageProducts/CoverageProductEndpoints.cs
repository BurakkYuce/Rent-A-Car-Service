using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.CoverageProducts;
using RentACar.Domain.Enums;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.CoverageProducts;

/// <summary>Sigorta/ek hizmet ürün kataloğu master form post uçları. OperationsWrite. Opsiyonel
/// sayısal alanlar boş "" ile bind 400 vermesin diye IFormCollection'dan FormParse ile çevrilir.</summary>
public static class CoverageProductEndpoints
{
    public static IEndpointRouteBuilder MapCoverageProductEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/sigorta-urunleri").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (CoverageProductService svc, HttpRequest req) =>
            await Run(() => svc.CreateAsync(Build(req.Form)), "Kayıt eklendi."));

        grp.MapPost("/update", async (CoverageProductService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(() => svc.UpdateAsync(id, Build(req.Form)), "Değişiklikler kaydedildi."));

        grp.MapPost("/delete", async (CoverageProductService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id), "Kayıt silindi."));

        return app;
    }

    private static CoverageProductInput Build(IFormCollection f) => new()
    {
        Kod = f["kod"].ToString(),
        Ad = f["ad"].ToString(),
        AdEn = FormParse.Str(f, "adEn"),
        Aciklama = FormParse.Str(f, "aciklama"),
        Tur = ParseEnum<CoverageProductType>(FormParse.Str(f, "tur")) ?? CoverageProductType.Diger,
        GunlukUcret = FormParse.Dec(FormParse.Str(f, "gunlukUcret")),
        KdvOrani = FormParse.Dec(FormParse.Str(f, "kdvOrani")),
        MaxGun = FormParse.Int(FormParse.Str(f, "maxGun")),
        Doviz = FormParse.Str(f, "doviz"),
        Zorunlu = (FormParse.Str(f, "zorunlu")) is "true" or "True" or "on",
        Aktif = (FormParse.Str(f, "aktif") ?? "true") is "true" or "True"
    };


    private static T? ParseEnum<T>(string? s) where T : struct, Enum
        => Enum.TryParse<T>((s ?? string.Empty).Trim(), out var v) ? v : null;

    private static async Task<IResult> Run(Func<Task> action, string message)
    {
        try { await action(); return Result.Ok("/sigorta-urunleri", message); }
        catch (ValidationException ex) { return Results.Redirect($"/sigorta-urunleri?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
