using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.MusteriTaksitleri;
using RentACar.Domain.Enums;
using RentACar.Web.Identity;

namespace RentACar.Web.MusteriTaksitleri;

/// <summary>
/// Müşteri taksit takibi form post uçları (FAZ-66). FinanceWrite — para BİLGİSİ taşıyor.
/// Bu uçlar DEFTERE YAZMAZ; "ödendi" bir takip bayrağıdır.
/// </summary>
public static class MusteriTaksitEndpoints
{
    public static IEndpointRouteBuilder MapMusteriTaksitEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/musteri-taksit").RequirePermission(Permission.FinanceWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (MusteriTaksitService svc, HttpRequest req) =>
            await Run(req, () => svc.CreateAsync(Build(req.Form))));

        grp.MapPost("/update", async (MusteriTaksitService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(req, () => svc.UpdateAsync(id, Build(req.Form))));

        grp.MapPost("/plan", async (MusteriTaksitService svc, HttpRequest req) =>
            await Run(req, () => svc.PlanUretAsync(new TaksitPlanInput
            {
                CariId = FormParse.Id(FormParse.Str(req.Form, "cariId")) ?? Guid.Empty,
                VehicleId = FormParse.Id(FormParse.Str(req.Form, "vehicleId")),
                ToplamTutar = FormParse.Dec(FormParse.Str(req.Form, "toplamTutar")) ?? 0m,
                TaksitSayisi = FormParse.Int(FormParse.Str(req.Form, "taksitSayisi")) ?? 0,
                IlkVade = FormParse.Date(FormParse.Str(req.Form, "ilkVade")),
                Currency = FormParse.Str(req.Form, "currency"),
                Kur = FormParse.Dec(FormParse.Str(req.Form, "kur")),
                Aciklama = FormParse.Str(req.Form, "aciklama")
            })));

        grp.MapPost("/odeme", async (MusteriTaksitService svc, HttpRequest req,
            [FromForm] Guid id, [FromForm] string? geriAl) =>
            await Run(req, () => svc.OdemeIsaretleAsync(id, geriAl != "true",
                FormParse.Date(FormParse.Str(req.Form, "odemeTarihi")))));

        grp.MapPost("/delete", async (MusteriTaksitService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(req, () => svc.DeleteAsync(id)));

        return app;
    }

    private static MusteriTaksitInput Build(IFormCollection f) => new()
    {
        CariId = FormParse.Id(FormParse.Str(f, "cariId")) ?? Guid.Empty,
        VehicleId = FormParse.Id(FormParse.Str(f, "vehicleId")),
        Vade = FormParse.Date(FormParse.Str(f, "vade")),
        TaksitTutari = FormParse.Dec(FormParse.Str(f, "taksitTutari")) ?? 0m,
        Currency = FormParse.Str(f, "currency"),
        Kur = FormParse.Dec(FormParse.Str(f, "kur")),
        Durum = Enum.TryParse<TaksitDurum>(FormParse.Str(f, "durum"), out var d) ? d : TaksitDurum.Bekliyor,
        OdemeTarihi = FormParse.Date(FormParse.Str(f, "odemeTarihi")),
        Aciklama = FormParse.Str(f, "aciklama")
    };

    /// <summary>Filtreyi koruyarak dön — kullanıcı baktığı süzgeçte kalır.</summary>
    private static string Geri(HttpRequest req, string? hata = null)
    {
        var q = new List<string>();
        foreach (var ad in new[] { "cariF", "aracF", "durumF", "min", "max", "gecikmis" })
        {
            var v = FormParse.Str(req.Form, ad);
            if (v is not null) q.Add($"{ad}={Uri.EscapeDataString(v)}");
        }
        if (hata is not null) q.Add($"hata={Uri.EscapeDataString(hata)}");
        return "/musteri-taksit" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
    }

    private static async Task<IResult> Run(HttpRequest req, Func<Task> action)
    {
        try { await action(); return Results.Redirect(Geri(req)); }
        catch (ValidationException ex) { return Results.Redirect(Geri(req, ex.Message)); }
    }
}
