using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Crm;
using RentACar.Web.Identity;

namespace RentACar.Web.Crm;

/// <summary>Assistans (yol yardım) talebi form post uçları — FAZ-44. OperationsWrite.</summary>
public static class AssistanceRequestEndpoints
{
    public static IEndpointRouteBuilder MapAssistanceRequestEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/assistans").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (AssistanceRequestService svc, HttpRequest req) =>
            await Run(req, () => svc.CreateAsync(Build(req.Form))));

        grp.MapPost("/update", async (AssistanceRequestService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(req, () => svc.UpdateAsync(id, Build(req.Form))));

        grp.MapPost("/delete", async (AssistanceRequestService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(req, () => svc.DeleteAsync(id)));

        return app;
    }

    private static AssistansInput Build(IFormCollection f) => new()
    {
        RentalId = FormParse.Id(FormParse.Str(f, "rentalId")),
        Plaka = FormParse.Str(f, "plaka"),
        AdSoyad = FormParse.Str(f, "adSoyad"),
        CepTel = FormParse.Str(f, "cepTel"),
        Zaman = FormParse.Date(FormParse.Str(f, "zaman")),
        Mesaj = FormParse.Str(f, "mesaj"),
        Sebep = FormParse.Str(f, "sebep"),
        // Checkbox işaretsizken tarayıcı alanı HİÇ göndermez → varlık kontrolü.
        YedekLastikMi = f.ContainsKey("yedekLastikMi"),
        AracHareketMi = f.ContainsKey("aracHareketMi"),
        Kapandi = f.ContainsKey("kapandi"),
        Cozum = FormParse.Str(f, "cozum")
    };

    /// <summary>Filtreyi koruyarak dön — kullanıcı baktığı süzgeçte kalır.</summary>
    private static string Back(HttpRequest req, string? error = null)
    {
        var q = new List<string>();
        foreach (var name in new[] { "plakaF", "min", "max", "ara", "durum" })
        {
            var v = FormParse.Str(req.Form, name);
            if (v is not null) q.Add($"{name}={Uri.EscapeDataString(v)}");
        }
        if (error is not null) q.Add($"hata={Uri.EscapeDataString(error)}");
        return "/assistans" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
    }

    private static async Task<IResult> Run(HttpRequest req, Func<Task> action)
    {
        try { await action(); return Results.Redirect(Back(req)); }
        catch (ValidationException ex) { return Results.Redirect(Back(req, ex.Message)); }
    }
}
