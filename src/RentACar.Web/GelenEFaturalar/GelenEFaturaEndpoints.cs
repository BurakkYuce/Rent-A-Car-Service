using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.GelenEFaturalar;
using RentACar.Domain.Enums;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.GelenEFaturalar;

/// <summary>Gelen e-Fatura triage form post uçları. FinanceWrite. Elle giriş + GİB-sync (stub) +
/// durum akışı (onayla/reddet/işle). Opsiyonel sayısal/tarih alanlar FormParse ile (boş → null/0).</summary>
public static class GelenEFaturaEndpoints
{
    public static IEndpointRouteBuilder MapGelenEFaturaEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/gelen-efatura").RequirePermission(Permission.FinanceWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (GelenEFaturaService svc, HttpRequest req) =>
            await Run(() => svc.CreateManualAsync(Build(req.Form)), "Kayıt eklendi."));

        grp.MapPost("/onayla", async (GelenEFaturaService svc, [FromForm] Guid id) =>
            await Run(() => svc.OnaylaAsync(id), "İşlem tamamlandı."));

        grp.MapPost("/reddet", async (GelenEFaturaService svc, [FromForm] Guid id, [FromForm] string? neden) =>
            await Run(() => svc.ReddetAsync(id, neden), "Reddedildi."));

        grp.MapPost("/isle", async (GelenEFaturaService svc, [FromForm] Guid id) =>
            await Run(() => svc.IsleAsync(id), "İşlem tamamlandı."));

        grp.MapPost("/sync", async (GelenEFaturaService svc, HttpRequest req) =>
        {
            var from = FormParse.Date(FormParse.Str(req.Form, "from")) ?? DateTimeOffset.Now.Date.AddMonths(-1);
            var to = FormParse.Date(FormParse.Str(req.Form, "to")) ?? DateTimeOffset.Now.Date;
            return await Run(() => svc.SyncFromGibAsync(from, to), "İşlem tamamlandı.");
        });

        // FAZ-55 (a): KDV oran kırılımı + araç/kategori/cari bağlama. Tüm sayısal alanlar OPSİYONEL →
        // string? + FormParse (boş string doğrudan [FromForm] decimal?'a bağlanırsa 400 üretir).
        grp.MapPost("/bagla", async (GelenEFaturaService svc, HttpRequest req) =>
        {
            var f = req.Form;
            return await Run(() => svc.BaglaAsync(new GelenEFaturaBaglamaInput
            {
                Id = FormParse.Id(FormParse.Str(f, "id")) ?? Guid.Empty,
                Kdv20Matrah = FormParse.Dec(FormParse.Str(f, "kdv20Matrah")),
                Kdv20 = FormParse.Dec(FormParse.Str(f, "kdv20")),
                Kdv10Matrah = FormParse.Dec(FormParse.Str(f, "kdv10Matrah")),
                Kdv10 = FormParse.Dec(FormParse.Str(f, "kdv10")),
                Kdv1Matrah = FormParse.Dec(FormParse.Str(f, "kdv1Matrah")),
                Kdv1 = FormParse.Dec(FormParse.Str(f, "kdv1")),
                Kdv0Matrah = FormParse.Dec(FormParse.Str(f, "kdv0Matrah")),
                VehicleId = FormParse.Id(FormParse.Str(f, "vehicleId")),
                ExpenseCategoryId = FormParse.Id(FormParse.Str(f, "expenseCategoryId")),
                CariId = FormParse.Id(FormParse.Str(f, "cariId")),
                GiderTipi = Enum.TryParse<ExpenseType>(FormParse.Str(f, "giderTipi"), out var gt) ? gt : null
            }), "İşlem tamamlandı.");
        });

        // FAZ-55 (b): giderleştirme — TEK para yolu. Deftere yazılan küme mevcut gider kümesidir.
        grp.MapPost("/giderlestir", async (GelenEFaturaService svc, HttpRequest req) =>
        {
            var f = req.Form;
            return await Run(() => svc.GiderlestirAsync(new GelenEFaturaGiderInput
            {
                Id = FormParse.Id(FormParse.Str(f, "id")) ?? Guid.Empty,
                OdemeYontemi = Enum.TryParse<OdemeYontemi>(FormParse.Str(f, "odemeYontemi"), out var oy)
                    ? oy : OdemeYontemi.AcikHesap,
                CariId = FormParse.Id(FormParse.Str(f, "cariId")),
                Sube = FormParse.Str(f, "sube")
            }), "Gidere dönüştürüldü.");
        });

        return app;
    }

    private static GelenEFaturaInput Build(IFormCollection f) => new()
    {
        Ettn = f["ettn"].ToString(),
        GonderenVkn = f["gonderenVkn"].ToString(),
        GonderenUnvan = f["gonderenUnvan"].ToString(),
        Tarih = FormParse.Date(FormParse.Str(f, "tarih")),
        NetTutar = FormParse.Dec(FormParse.Str(f, "netTutar")) ?? 0m,
        KdvTutar = FormParse.Dec(FormParse.Str(f, "kdvTutar")) ?? 0m,
        GenelToplam = FormParse.Dec(FormParse.Str(f, "genelToplam")) ?? 0m,
        Currency = FormParse.Str(f, "currency"),
        Aciklama = FormParse.Str(f, "aciklama")
    };

    private static async Task<IResult> Run(Func<Task> action, string mesaj)
    {
        try { await action(); return Sonuc.Tamam("/gelen-efatura", mesaj); }
        catch (ValidationException ex) { return Results.Redirect($"/gelen-efatura?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
