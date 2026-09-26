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
public static class CustomerInstallmentEndpoints
{
    public static IEndpointRouteBuilder MapCustomerInstallmentEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/musteri-taksit").RequirePermission(Permission.FinanceWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (CustomerInstallmentService svc, HttpRequest req) =>
            await Run(req, () => svc.CreateAsync(Build(req.Form))));

        grp.MapPost("/update", async (CustomerInstallmentService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(req, () => svc.UpdateAsync(id, Build(req.Form))));

        grp.MapPost("/plan", async (CustomerInstallmentService svc, HttpRequest req) =>
            await Run(req, () => svc.GeneratePlanAsync(new TaksitPlanInput
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

        grp.MapPost("/odeme", async (CustomerInstallmentService svc, HttpRequest req,
            [FromForm] Guid id, [FromForm] string? geriAl) =>
            await Run(req, () => svc.MarkPaidAsync(id, geriAl != "true",
                FormParse.Date(FormParse.Str(req.Form, "odemeTarihi")))));

        grp.MapPost("/delete", async (CustomerInstallmentService svc, HttpRequest req, [FromForm] Guid id) =>
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
        Durum = Enum.TryParse<InstallmentStatus>(FormParse.Str(f, "durum"), out var d) ? d : InstallmentStatus.Bekliyor,
        OdemeTarihi = FormParse.Date(FormParse.Str(f, "odemeTarihi")),
        Aciklama = FormParse.Str(f, "aciklama")
    };

    /// <summary>Filtreyi koruyarak dön — kullanıcı baktığı süzgeçte kalır.</summary>
    private static string Back(HttpRequest req, string? error = null)
    {
        var q = new List<string>();
        foreach (var name in new[] { "cariF", "aracF", "durumF", "min", "max", "gecikmis" })
        {
            var v = FormParse.Str(req.Form, name);
            if (v is not null) q.Add($"{name}={Uri.EscapeDataString(v)}");
        }
        if (error is not null) q.Add($"hata={Uri.EscapeDataString(error)}");
        return "/musteri-taksit" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
    }

    private static async Task<IResult> Run(HttpRequest req, Func<Task> action)
    {
        try { await action(); return Results.Redirect(Back(req)); }
        catch (ValidationException ex) { return Results.Redirect(Back(req, ex.Message)); }
    }
}
