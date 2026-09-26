using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Personnel;
using RentACar.Web.Identity;

namespace RentACar.Web.Personnel;

/// <summary>
/// Personel vardiyası form post uçları (FAZ-45). OperationsWrite.
/// Sayfa <c>/raporlar/personel-calisma</c>; POST'lar alt yola gider (aynı yolda @page + MapPost
/// AmbiguousMatchException üretir — repo dersi).
/// Yönlendirme filtre parametrelerini KORUR: kaydettikten sonra kullanıcı baktığı haftada kalır.
/// </summary>
public static class ShiftEndpoints
{
    public static IEndpointRouteBuilder MapShiftEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/raporlar/personel-calisma")
            .RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (StaffShiftService svc, HttpRequest req) =>
            await Run(req, () => svc.CreateAsync(Build(req.Form))));

        grp.MapPost("/update", async (StaffShiftService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(req, () => svc.UpdateAsync(id, Build(req.Form))));

        grp.MapPost("/delete", async (StaffShiftService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(req, () => svc.DeleteAsync(id)));

        return app;
    }

    private static VardiyaInput Build(IFormCollection f) => new()
    {
        PersonelId = FormParse.Id(FormParse.Str(f, "personelId")) ?? Guid.Empty,
        Tarih = FormParse.Day(FormParse.Str(f, "tarih")) ?? default,
        BaslangicSaat = FormParse.Hour(FormParse.Str(f, "baslangicSaat")) ?? default,
        BitisSaat = FormParse.Hour(FormParse.Str(f, "bitisSaat")) ?? default,
        Sube = FormParse.Str(f, "sube"),
        Aciklama = FormParse.Str(f, "aciklama")
    };

    /// <summary>Görüntülenen pencere/filtre form içinde hidden olarak taşınır; redirect onu geri yazar.</summary>
    private static string Back(HttpRequest req, string? error = null)
    {
        var q = new List<string>();
        void Add(string name)
        {
            var v = FormParse.Str(req.Form, name);
            if (v is not null) q.Add($"{name}={Uri.EscapeDataString(v)}");
        }
        Add("bas"); Add("bit"); Add("personelFiltre"); Add("subeFiltre");
        if (error is not null) q.Add($"hata={Uri.EscapeDataString(error)}");
        return "/raporlar/personel-calisma" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
    }

    private static async Task<IResult> Run(HttpRequest req, Func<Task> action)
    {
        try { await action(); return Results.Redirect(Back(req)); }
        catch (ValidationException ex) { return Results.Redirect(Back(req, ex.Message)); }
    }
}
