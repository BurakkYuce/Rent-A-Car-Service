using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.FiloPlan;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.FiloPlan;

/// <summary>Filo plan hedefi form post uçları (FAZ-19). OperationsWrite.</summary>
public static class FiloPlanEndpoints
{
    public static IEndpointRouteBuilder MapFiloPlanEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/filo-plan").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (FiloPlanService svc, HttpRequest req) =>
            await Run(() => svc.CreateAsync(Build(req.Form)), "Kayıt eklendi."));

        grp.MapPost("/update", async (FiloPlanService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(() => svc.UpdateAsync(id, Build(req.Form)), "Değişiklikler kaydedildi."));

        // Artır/Azalt — delta SUNUCUDA sınırlanır: forma güvenilip serbest bırakılsaydı bir POST
        // hedefi tek hamlede istediği yere taşıyabilirdi (bu uç düzenleme ucu değil).
        grp.MapPost("/delta", async (FiloPlanService svc, [FromForm] Guid id, [FromForm] string? yon) =>
            await Run(() => svc.HedefDegistirAsync(id, yon == "azalt" ? -1 : 1), "İşlem tamamlandı."));

        grp.MapPost("/delete", async (FiloPlanService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id), "Kayıt silindi."));

        return app;
    }

    private static FiloPlanInput Build(IFormCollection f) => new()
    {
        AracGrupAdi = FormParse.Str(f, "aracGrupAdi"),
        Sipp = FormParse.Str(f, "sipp"),
        Donem = FormParse.Str(f, "donem"),
        HedefAdet = FormParse.Int(FormParse.Str(f, "hedefAdet")) ?? 0,
        Aciklama = FormParse.Str(f, "aciklama")
    };

    private static async Task<IResult> Run(Func<Task> action, string mesaj)
    {
        try { await action(); return Sonuc.Tamam("/filo-plan", mesaj); }
        catch (ValidationException ex) { return Results.Redirect($"/filo-plan?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
