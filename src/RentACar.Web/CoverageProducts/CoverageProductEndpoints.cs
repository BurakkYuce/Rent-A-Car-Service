using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.CoverageProducts;
using RentACar.Domain.Enums;
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
            await Run(() => svc.CreateAsync(Build(req.Form))));

        grp.MapPost("/update", async (CoverageProductService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(() => svc.UpdateAsync(id, Build(req.Form))));

        grp.MapPost("/delete", async (CoverageProductService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static CoverageProductInput Build(IFormCollection f) => new()
    {
        Kod = f["kod"].ToString(),
        Ad = f["ad"].ToString(),
        AdEn = Str(f, "adEn"),
        Aciklama = Str(f, "aciklama"),
        Tur = ParseEnum<CoverageProductType>(Str(f, "tur")) ?? CoverageProductType.Diger,
        GunlukUcret = FormParse.Dec(Str(f, "gunlukUcret")),
        KdvOrani = FormParse.Dec(Str(f, "kdvOrani")),
        MaxGun = FormParse.Int(Str(f, "maxGun")),
        Doviz = Str(f, "doviz"),
        Zorunlu = (Str(f, "zorunlu")) is "true" or "True" or "on",
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
        try { await action(); return Results.Redirect("/sigorta-urunleri"); }
        catch (ValidationException ex) { return Results.Redirect($"/sigorta-urunleri?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
