using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Pricing;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.Pricing;

/// <summary>
/// Kaydedilmiş maliyet teklifi form post uçları (FAZ-74). FinanceWrite — maliyet/kâr marjı
/// ticari sırdır, operasyon rolüne açılmaz.
///
/// <para><b>DEFTERE YAZMAZ.</b> Bu uçlar yalnız planlama belgesi üretir; gelir/gider/cari
/// bakiyeye tek satır bile eklemez.</para>
///
/// <para><b>YOL AYRIMI:</b> <c>/maliyet-hesapla</c> bir Razor <c>@@page</c>'tir; aynı yola
/// <c>MapPost</c> eklemek AmbiguousMatchException (500) üretir. Bu yüzden POST'lar ayrı
/// <c>/maliyet-teklifi/*</c> alt-yolundadır.</para>
/// </summary>
public static class CostQuotationEndpoints
{
    public static IEndpointRouteBuilder MapCostQuotationEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/maliyet-teklifi").RequirePermission(Permission.FinanceWrite).AntiforgeryByEnv();

        // Hesap ekranından "Teklifi Kaydet" → başarıda hesap ekranına kayıt no ile döner.
        grp.MapPost("/kaydet", async (CostQuotationService svc, HttpRequest req) =>
        {
            try
            {
                var id = await svc.CreateAsync(Build(req.Form));
                var record = (await svc.GetAsync(id))?.KayitNo ?? "";
                return Results.Redirect(CalculationPath(req, record: record));
            }
            catch (ValidationException ex) { return Results.Redirect(CalculationPath(req, error: ex.Message)); }
        });

        grp.MapPost("/update", async (CostQuotationService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(() => svc.UpdateAsync(id, Build(req.Form)), "Değişiklikler kaydedildi."));

        grp.MapPost("/delete", async (CostQuotationService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id), "Kayıt silindi."));

        return app;
    }

    private static MaliyetTeklifiInput Build(IFormCollection f) => new()
    {
        Baslik = FormParse.Str(f, "baslik"),
        Plaka = FormParse.Str(f, "plaka"),
        Tarih = FormParse.Date(FormParse.Str(f, "tarih")),
        CariId = FormParse.Id(FormParse.Str(f, "cariId")),
        HazirlayanId = FormParse.Id(FormParse.Str(f, "hazirlayanId")),
        Aciklama = FormParse.Str(f, "aciklama"),
        // Hesap girdisi ekrandakiyle AYNI ayrıştırıcıdan geçer (önizleme == kayıt).
        Girdi = CostCalculationInput.Setup(name => FormParse.Str(f, name))
    };

    /// <summary>Hata/başarı durumunda hesap ekranına GİRDİLERİ KORUYARAK dön (form boşalmasın).</summary>
    private static string CalculationPath(HttpRequest req, string? error = null, string? record = null)
    {
        var q = new List<string>();
        foreach (var name in CostCalculationInput.FieldNames)
        {
            var v = FormParse.Str(req.Form, name);
            if (v is not null) q.Add($"{name}={Uri.EscapeDataString(v)}");
        }
        if (error is not null) q.Add($"hata={Uri.EscapeDataString(error)}");
        if (!string.IsNullOrEmpty(record)) q.Add($"kayit={Uri.EscapeDataString(record)}");
        return "/maliyet-hesapla" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
    }

    private static async Task<IResult> Run(Func<Task> action, string message)
    {
        try { await action(); return Result.Ok("/maliyet-teklifleri", message); }
        catch (ValidationException ex)
        {
            return Results.Redirect("/maliyet-teklifleri?hata=" + Uri.EscapeDataString(ex.Message));
        }
    }
}
