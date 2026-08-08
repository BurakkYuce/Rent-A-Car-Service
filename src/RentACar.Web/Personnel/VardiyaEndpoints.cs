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
public static class VardiyaEndpoints
{
    public static IEndpointRouteBuilder MapVardiyaEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/raporlar/personel-calisma")
            .RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (PersonelVardiyaService svc, HttpRequest req) =>
            await Run(req, () => svc.CreateAsync(Build(req.Form))));

        grp.MapPost("/update", async (PersonelVardiyaService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(req, () => svc.UpdateAsync(id, Build(req.Form))));

        grp.MapPost("/delete", async (PersonelVardiyaService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(req, () => svc.DeleteAsync(id)));

        return app;
    }

    private static VardiyaInput Build(IFormCollection f) => new()
    {
        PersonelId = FormParse.Id(FormParse.Str(f, "personelId")) ?? Guid.Empty,
        Tarih = FormParse.Gun(FormParse.Str(f, "tarih")) ?? default,
        BaslangicSaat = FormParse.Saat(FormParse.Str(f, "baslangicSaat")) ?? default,
        BitisSaat = FormParse.Saat(FormParse.Str(f, "bitisSaat")) ?? default,
        Sube = FormParse.Str(f, "sube"),
        Aciklama = FormParse.Str(f, "aciklama")
    };

    /// <summary>Görüntülenen pencere/filtre form içinde hidden olarak taşınır; redirect onu geri yazar.</summary>
    private static string Geri(HttpRequest req, string? hata = null)
    {
        var q = new List<string>();
        void Ekle(string ad)
        {
            var v = FormParse.Str(req.Form, ad);
            if (v is not null) q.Add($"{ad}={Uri.EscapeDataString(v)}");
        }
        Ekle("bas"); Ekle("bit"); Ekle("personelFiltre"); Ekle("subeFiltre");
        if (hata is not null) q.Add($"hata={Uri.EscapeDataString(hata)}");
        return "/raporlar/personel-calisma" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
    }

    private static async Task<IResult> Run(HttpRequest req, Func<Task> action)
    {
        try { await action(); return Results.Redirect(Geri(req)); }
        catch (ValidationException ex) { return Results.Redirect(Geri(req, ex.Message)); }
    }
}
