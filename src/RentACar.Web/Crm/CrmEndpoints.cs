using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Crm;
using RentACar.Domain.Enums;
using RentACar.Web.Identity;

namespace RentACar.Web.Crm;

/// <summary>CRM (anket + şikayet) form post uçları (roadmap C3). OperationsWrite. IFormCollection + FormParse.</summary>
public static class CrmEndpoints
{
    public static IEndpointRouteBuilder MapCrmEndpoints(this IEndpointRouteBuilder app)
    {
        var an = app.MapGroup("/anketler").RequirePermission(Permission.OperationsWrite).DisableAntiforgery();
        an.MapPost("/create", async (AnketService svc, HttpRequest req) =>
            await Run("/anketler", () => svc.CreateAsync(BuildAnket(req.Form))));
        an.MapPost("/update", async (AnketService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run("/anketler", () => svc.UpdateAsync(id, BuildAnket(req.Form))));
        an.MapPost("/delete", async (AnketService svc, [FromForm] Guid id) =>
            await Run("/anketler", () => svc.DeleteAsync(id)));

        var sk = app.MapGroup("/sikayetler").RequirePermission(Permission.OperationsWrite).DisableAntiforgery();
        sk.MapPost("/create", async (SikayetService svc, HttpRequest req) =>
            await Run("/sikayetler", () => svc.CreateAsync(BuildSikayet(req.Form))));
        sk.MapPost("/update", async (SikayetService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run("/sikayetler", () => svc.UpdateAsync(id, BuildSikayet(req.Form))));
        sk.MapPost("/delete", async (SikayetService svc, [FromForm] Guid id) =>
            await Run("/sikayetler", () => svc.DeleteAsync(id)));

        return app;
    }

    private static AnketInput BuildAnket(IFormCollection f) => new()
    {
        CariId = Guid.TryParse(Str(f, "cariId"), out var c) ? c : null,
        Puan = FormParse.Int(Str(f, "puan")) ?? 0,
        Yorum = Str(f, "yorum"),
        Tarih = FormParse.Date(Str(f, "tarih")),
        Kaynak = Str(f, "kaynak")
    };

    private static SikayetInput BuildSikayet(IFormCollection f) => new()
    {
        CariId = Guid.TryParse(Str(f, "cariId"), out var c) ? c : null,
        Konu = f["konu"].ToString(),
        Detay = Str(f, "detay"),
        Durum = ParseEnum<SikayetDurum>(Str(f, "durum")) ?? SikayetDurum.Acik,
        Tarih = FormParse.Date(Str(f, "tarih")),
        Cozum = Str(f, "cozum")
    };

    private static string? Str(IFormCollection f, string key)
    {
        var v = f[key].ToString();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    private static T? ParseEnum<T>(string? s) where T : struct, Enum
        => Enum.TryParse<T>((s ?? string.Empty).Trim(), out var v) ? v : null;

    private static async Task<IResult> Run(string back, Func<Task> action)
    {
        try { await action(); return Results.Redirect(back); }
        catch (ValidationException ex) { return Results.Redirect($"{back}?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
