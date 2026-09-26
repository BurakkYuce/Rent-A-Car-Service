using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Pricing;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.Pricing;

/// <summary>Tarife (rate card) form post uçları. OperationsWrite (fiyat operasyonel yapılandırma).</summary>
public static class RateCardEndpoints
{
    public static IEndpointRouteBuilder MapRateCardEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/tarifeler").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        // FAZ-72: alan sayısı 15'e çıktı → pozisyonel imza yerine form koleksiyonu (diğer
        // uçlardaki desen). Opsiyonel sayısal/tarih alanları FormParse ile çevrilir.
        grp.MapPost("/create", async (RateCardService svc, HttpRequest req) =>
            await Run(() => svc.CreateAsync(Build(req.Form, defaultActive: true)), "Kayıt eklendi."));

        grp.MapPost("/update", async (RateCardService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(() => svc.UpdateAsync(id, Build(req.Form, defaultActive: null)), "Değişiklikler kaydedildi."));

        grp.MapPost("/delete", async (RateCardService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id), "Kayıt silindi."));

        return app;
    }

    private static RateCardInput Build(IFormCollection f, bool? defaultActive) => new()
    {
        Kod = f["kod"].ToString(),
        Ad = f["ad"].ToString(),
        Grup = f["grup"].ToString(),
        MinGun = FormParse.Int(FormParse.Str(f, "minGun")) ?? 1,
        MaxGun = FormParse.Int(FormParse.Str(f, "maxGun")) ?? 9999,
        GunlukUcret = FormParse.Dec(FormParse.Str(f, "gunlukUcret")) ?? 0m,
        Doviz = FormParse.Str(f, "doviz") ?? "TRY",
        GecerliBas = FormParse.Date(FormParse.Str(f, "gecerliBas")),
        GecerliBit = FormParse.Date(FormParse.Str(f, "gecerliBit")),
        // FAZ-72 teminat/görünürlük bayrakları — işaretsiz checkbox HİÇ gönderilmez → false.
        ScdwDahil = Flag(f, "scdwDahil"),
        MiniHasarDahil = Flag(f, "miniHasarDahil"),
        HirsizlikDahil = Flag(f, "hirsizlikDahil"),
        ScdwZorunlu = Flag(f, "scdwZorunlu"),
        Gosterme = Flag(f, "gosterme"),
        TarifeGrubuId = FormParse.Id(FormParse.Str(f, "tarifeGrubuId")),
        Aktif = defaultActive ?? ((FormParse.Str(f, "aktif") ?? "true") is "true" or "True")
    };

    private static bool Flag(IFormCollection f, string name)
        => FormParse.Str(f, name) is "true" or "on" or "True";

    private static async Task<IResult> Run(Func<Task> action, string message)
    {
        try { await action(); return Result.Ok("/tarifeler", message); }
        catch (ValidationException ex) { return Results.Redirect($"/tarifeler?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
