using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.BelgeSablon;
using RentACar.Application.Common;
using RentACar.Domain.Enums;
using RentACar.Web.Identity;

namespace RentACar.Web.BelgeSablon;

/// <summary>Marka-özel belge şablonu master form post uçları. ManageUsers (Ayarlar hassasiyeti).</summary>
public static class BelgeSablonEndpoints
{
    public static IEndpointRouteBuilder MapBelgeSablonEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/belge-sablonlari").RequirePermission(Permission.ManageUsers).AntiforgeryByEnv();

        grp.MapPost("/create", async (DocumentTemplateService svc, HttpRequest req) =>
            await Run(() => svc.CreateAsync(Build(req.Form))));

        grp.MapPost("/update", async (DocumentTemplateService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(() => svc.UpdateAsync(id, Build(req.Form))));

        grp.MapPost("/delete", async (DocumentTemplateService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static BelgeSablonInput Build(IFormCollection f) => new()
    {
        BelgeTuru = Enum.TryParse<BelgeTuru>(f["belgeTuru"].ToString(), out var t) ? t : BelgeTuru.KiraSozlesmesi,
        Ad = f["ad"].ToString(),
        VarsayilanMi = FormParse.Str(f, "varsayilanMi") is "true" or "on",
        Aktif = (FormParse.Str(f, "aktif") ?? "true") is "true" or "True",
        BelgeBasligi = FormParse.Str(f, "belgeBasligi"),
        HukukiMetinSol = FormParse.Str(f, "hukukiMetinSol"),
        HukukiMetinSag = FormParse.Str(f, "hukukiMetinSag"),
        EkKosullarVarsayilan = FormParse.Str(f, "ekKosullarVarsayilan"),
        AltBilgi = FormParse.Str(f, "altBilgi"),
        // Checkbox: işaretsizken tarayıcı alanı HİÇ göndermez. Formda gizli bir "false" alanı
        // (aynı ad) var → gönderilen son değer kazanır; alan hiç yoksa varsayılan true kalır
        // (mevcut davranışı korur).
        ImzaAlaniGoster = f["imzaAlaniGoster"].Count == 0
            || f["imzaAlaniGoster"].Last() is "true" or "on" or "True"
    };

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/belge-sablonlari?ok=1"); }
        catch (ValidationException ex) { return Results.Redirect($"/belge-sablonlari?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
