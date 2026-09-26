using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.ServisTanimlari;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.ServisTanimlari;

/// <summary>Servis tanım master form post uçları (roadmap N1). OperationsWrite.</summary>
public static class ServiceDefinitionEndpoints
{
    public static IEndpointRouteBuilder MapServiceDefinitionEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/servis-tanimlari").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (ServiceDefinitionService svc, HttpRequest req,
            [FromForm] string kod, [FromForm] string aracTipi, [FromForm] int bakimKm, [FromForm] string? aciklama) =>
            await Run(() => svc.CreateAsync(Build(req.Form, kod, aracTipi, bakimKm, aciklama, true)), "Kayıt eklendi."));

        grp.MapPost("/update", async (ServiceDefinitionService svc, HttpRequest req, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string aracTipi, [FromForm] int bakimKm, [FromForm] string? aciklama, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, Build(req.Form, kod, aracTipi, bakimKm, aciklama, aktif)), "Değişiklikler kaydedildi."));

        grp.MapPost("/delete", async (ServiceDefinitionService svc, [FromForm] Guid id) => await Run(() => svc.DeleteAsync(id), "Kayıt silindi."));

        // FAZ-14 C: öneriyi KABUL et — öneri sayfası hiçbir şey yazmaz, kayıt bu uçta doğar.
        // Kod/KM kullanıcı tarafından düzenlenebilir olduğu için formdan gelir (önerinin
        // kendisinden değil) — kullanıcı ne gördüyse o kaydedilir.
        grp.MapPost("/oneri-kabul", async (ServiceDefinitionService svc, HttpRequest req) =>
            await Run(() => svc.CreateAsync(Build(
                req.Form,
                req.Form["kod"].ToString(),
                req.Form["aracTipi"].ToString(),
                FormParse.Int(FormParse.Str(req.Form, "bakimKm")) ?? 0,
                FormParse.Str(req.Form, "aciklama"), true)), "Öneri kabul edildi."));

        return app;
    }

    private static ServisTanimInput Build(IFormCollection f, string code, string vehicleType, int maintenanceKm, string? description, bool active) => new()
    {
        Kod = code, AracTipi = vehicleType, BakimKm = maintenanceKm, Aciklama = description, Aktif = active,
        Marka = FormParse.Str(f, "marka"), Tip = FormParse.Str(f, "tip"),
        Yakit = FormParse.Str(f, "yakit"), Vites = FormParse.Str(f, "vites")
    };

    private static async Task<IResult> Run(Func<Task> action, string message)
    {
        try { await action(); return Result.Ok("/servis-tanimlari", message); }
        catch (ValidationException ex) { return Results.Redirect($"/servis-tanimlari?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
