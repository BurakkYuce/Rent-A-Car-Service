using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Legal;
using RentACar.Domain.Enums;
using RentACar.Web.Identity;

namespace RentACar.Web.Legal;

/// <summary>Hukuk dosyası master form post uçları (roadmap C2). OperationsWrite. Opsiyonel alanlar boş ""
/// bind 400 vermesin diye IFormCollection + FormParse.</summary>
public static class HukukEndpoints
{
    public static IEndpointRouteBuilder MapHukukEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/hukuk").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (HukukDosyaService svc, HttpRequest req) =>
            await Run(() => svc.CreateAsync(Build(req.Form))));

        grp.MapPost("/update", async (HukukDosyaService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(() => svc.UpdateAsync(id, Build(req.Form))));

        grp.MapPost("/delete", async (HukukDosyaService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static HukukDosyaInput Build(IFormCollection f) => new()
    {
        DosyaNo = f["dosyaNo"].ToString(),
        CariId = Guid.TryParse(FormParse.Str(f, "cariId"), out var c) ? c : null,
        Tur = ParseEnum<HukukTuru>(FormParse.Str(f, "tur")) ?? HukukTuru.Dava,
        Avukat = FormParse.Str(f, "avukat"),
        Tutar = FormParse.Dec(FormParse.Str(f, "tutar")) ?? 0m,
        Durum = ParseEnum<HukukDurum>(FormParse.Str(f, "durum")) ?? HukukDurum.Acik,
        Tarih = FormParse.Date(FormParse.Str(f, "tarih")),
        Aciklama = FormParse.Str(f, "aciklama"),
        Aktif = (FormParse.Str(f, "aktif") ?? "true") is "true" or "True",
        // FAZ-41 derinlik — opsiyonel decimal boş "" gelince [FromForm] 400 verirdi; FormParse.Dec ile çevrilir.
        FaturaNoTemp = FormParse.Str(f, "faturaNoTemp"),
        AvukatTel = FormParse.Str(f, "avukatTel"),
        AvukatMail = FormParse.Str(f, "avukatMail"),
        Avukat2Ad = FormParse.Str(f, "avukat2Ad"),
        Avukat2Tel = FormParse.Str(f, "avukat2Tel"),
        Avukat2Mail = FormParse.Str(f, "avukat2Mail"),
        Tahsilat = FormParse.Dec(FormParse.Str(f, "tahsilat"))
    };


    private static T? ParseEnum<T>(string? s) where T : struct, Enum
        => Enum.TryParse<T>((s ?? string.Empty).Trim(), out var v) ? v : null;

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/hukuk"); }
        catch (ValidationException ex) { return Results.Redirect($"/hukuk?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
