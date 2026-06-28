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
        CariId = Guid.TryParse(Str(f, "cariId"), out var c) ? c : null,
        Tur = ParseEnum<HukukTuru>(Str(f, "tur")) ?? HukukTuru.Dava,
        Avukat = Str(f, "avukat"),
        Tutar = FormParse.Dec(Str(f, "tutar")) ?? 0m,
        Durum = ParseEnum<HukukDurum>(Str(f, "durum")) ?? HukukDurum.Acik,
        Tarih = FormParse.Date(Str(f, "tarih")),
        Aciklama = Str(f, "aciklama"),
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
        try { await action(); return Results.Redirect("/hukuk"); }
        catch (ValidationException ex) { return Results.Redirect($"/hukuk?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
