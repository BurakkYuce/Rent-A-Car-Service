using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.BrokerYasaklari;
using RentACar.Application.Common;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.BrokerYasaklari;

/// <summary>Broker/kaynak satış yasağı form post uçları. OperationsWrite. Opsiyonel sayısal/tarih alanlar
/// boş "" ile bind 400 vermesin diye IFormCollection'dan FormParse ile çevrilir (boş → null).</summary>
public static class BrokerBanEndpoints
{
    public static IEndpointRouteBuilder MapBrokerBanEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/broker-yasaklari").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (BrokerBanService svc, HttpRequest req) =>
            await Run(() => svc.CreateAsync(Build(req.Form)), "Kayıt eklendi."));

        grp.MapPost("/update", async (BrokerBanService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(() => svc.UpdateAsync(id, Build(req.Form)), "Değişiklikler kaydedildi."));

        grp.MapPost("/delete", async (BrokerBanService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id), "Kayıt silindi."));

        return app;
    }

    private static BrokerYasakInput Build(IFormCollection f) => new()
    {
        Kod = f["kod"].ToString(),
        Ad = f["ad"].ToString(),
        Aciklama = FormParse.Str(f, "aciklama"),
        Kaynak = FormParse.Str(f, "kaynak"),
        AracGrupKod = FormParse.Str(f, "aracGrupKod"),
        Bolge = FormParse.Str(f, "bolge"),
        MinGun = FormParse.Int(FormParse.Str(f, "minGun")),
        TumSatisKapali = (FormParse.Str(f, "tumSatisKapali")) is "true" or "True" or "on",
        GecerlilikBas = FormParse.Date(FormParse.Str(f, "gecerlilikBas")),
        GecerlilikBit = FormParse.Date(FormParse.Str(f, "gecerlilikBit")),
        Aktif = (FormParse.Str(f, "aktif") ?? "true") is "true" or "True"
    };

    private static async Task<IResult> Run(Func<Task> action, string message)
    {
        try { await action(); return Result.Ok("/broker-yasaklari", message); }
        catch (ValidationException ex) { return Results.Redirect($"/broker-yasaklari?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
