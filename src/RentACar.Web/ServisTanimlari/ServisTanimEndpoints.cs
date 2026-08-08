using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.ServisTanimlari;
using RentACar.Web.Identity;

namespace RentACar.Web.ServisTanimlari;

/// <summary>Servis tanım master form post uçları (roadmap N1). OperationsWrite.</summary>
public static class ServisTanimEndpoints
{
    public static IEndpointRouteBuilder MapServisTanimEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/servis-tanimlari").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (ServisTanimService svc, HttpRequest req,
            [FromForm] string kod, [FromForm] string aracTipi, [FromForm] int bakimKm, [FromForm] string? aciklama) =>
            await Run(() => svc.CreateAsync(Build(req.Form, kod, aracTipi, bakimKm, aciklama, true))));

        grp.MapPost("/update", async (ServisTanimService svc, HttpRequest req, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string aracTipi, [FromForm] int bakimKm, [FromForm] string? aciklama, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, Build(req.Form, kod, aracTipi, bakimKm, aciklama, aktif))));

        grp.MapPost("/delete", async (ServisTanimService svc, [FromForm] Guid id) => await Run(() => svc.DeleteAsync(id)));

        // FAZ-14 C: öneriyi KABUL et — öneri sayfası hiçbir şey yazmaz, kayıt bu uçta doğar.
        // Kod/KM kullanıcı tarafından düzenlenebilir olduğu için formdan gelir (önerinin
        // kendisinden değil) — kullanıcı ne gördüyse o kaydedilir.
        grp.MapPost("/oneri-kabul", async (ServisTanimService svc, HttpRequest req) =>
            await Run(() => svc.CreateAsync(Build(
                req.Form,
                req.Form["kod"].ToString(),
                req.Form["aracTipi"].ToString(),
                FormParse.Int(FormParse.Str(req.Form, "bakimKm")) ?? 0,
                FormParse.Str(req.Form, "aciklama"), true))));

        return app;
    }

    private static ServisTanimInput Build(IFormCollection f, string kod, string aracTipi, int bakimKm, string? aciklama, bool aktif) => new()
    {
        Kod = kod, AracTipi = aracTipi, BakimKm = bakimKm, Aciklama = aciklama, Aktif = aktif,
        Marka = FormParse.Str(f, "marka"), Tip = FormParse.Str(f, "tip"),
        Yakit = FormParse.Str(f, "yakit"), Vites = FormParse.Str(f, "vites")
    };

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/servis-tanimlari"); }
        catch (ValidationException ex) { return Results.Redirect($"/servis-tanimlari?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
