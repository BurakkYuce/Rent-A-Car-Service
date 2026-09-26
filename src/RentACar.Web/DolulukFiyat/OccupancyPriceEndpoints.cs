using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.DolulukFiyat;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.DolulukFiyat;

/// <summary>Doluluk fiyat kuralı master form post uçları (FAZ 3.A7). OperationsWrite.</summary>
public static class OccupancyPriceEndpoints
{
    public static IEndpointRouteBuilder MapOccupancyPriceEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/doluluk-kurallari").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (OccupancyPriceRuleService svc, HttpRequest req) =>
            await Run(() => svc.CreateAsync(Build(req.Form)), "Kayıt eklendi."));

        grp.MapPost("/update", async (OccupancyPriceRuleService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(() => svc.UpdateAsync(id, Build(req.Form)), "Değişiklikler kaydedildi."));

        grp.MapPost("/delete", async (OccupancyPriceRuleService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id), "Kayıt silindi."));

        // FAZ-73 — toplu kademe girişi (canlı doluluk_algoritma.aspx): ortak kapsam + 10 satır.
        grp.MapPost("/toplu", async (OccupancyPriceRuleService svc, HttpRequest req) =>
            await Run(() => svc.BulkCreateAsync(BuildBulk(req.Form)), "Toplu işlem uygulandı."));

        return app;
    }

    private static DolulukFiyatKuralInput Build(IFormCollection f) => new()
    {
        Kod = f["kod"].ToString(),
        Ad = f["ad"].ToString(),
        AracGrupKod = FormParse.Str(f, "aracGrupKod"),
        EsikYuzde = FormParse.Int(FormParse.Str(f, "esikYuzde")) ?? 0,
        CarpanYuzde = FormParse.Dec(FormParse.Str(f, "carpanYuzde")) ?? 0m,
        Sube = FormParse.Str(f, "sube"),
        SadeceKendiSubeleri = IsChecked(f, "sadeceKendiSubeleri"),
        GecerlilikBas = FormParse.Date(FormParse.Str(f, "gecerlilikBas")),
        GecerlilikBit = FormParse.Date(FormParse.Str(f, "gecerlilikBit")),
        Aktif = (FormParse.Str(f, "aktif") ?? "true") is "true" or "True"
    };

    /// <summary>
    /// FAZ-73 toplu-giriş form okuması. 10 satır <c>esik_0..9</c> / <c>carpan_0..9</c> adlarıyla
    /// gelir; İKİSİ DE boş olan satır "girilmedi" sayılıp ATLANIR (kullanıcı 3 kademe girip 7 satırı
    /// boş bırakabilsin). Yalnız biri doluysa satır alınır ve eksik alan 0 olur — servis doğrulaması
    /// (eşik 1..100) bunu gürültülü reddeder, sessizce %0 kademe yazılmaz.
    /// </summary>
    private static DolulukTopluInput BuildBulk(IFormCollection f)
    {
        var tiers = new List<DolulukKademeSatiri>();
        for (var i = 0; i < 10; i++)
        {
            var threshold = FormParse.Str(f, $"esik_{i}");
            var multiplier = FormParse.Str(f, $"carpan_{i}");
            if (threshold is null && multiplier is null) continue;
            tiers.Add(new DolulukKademeSatiri(
                FormParse.Int(threshold) ?? 0, FormParse.Dec(multiplier) ?? 0m));
        }

        return new DolulukTopluInput
        {
            KodOnEk = f["kodOnEk"].ToString(),
            AdOnEk = f["adOnEk"].ToString(),
            AracGrupKod = FormParse.Str(f, "aracGrupKod"),
            Sube = FormParse.Str(f, "sube"),
            SadeceKendiSubeleri = IsChecked(f, "sadeceKendiSubeleri"),
            GecerlilikBas = FormParse.Date(FormParse.Str(f, "gecerlilikBas")),
            GecerlilikBit = FormParse.Date(FormParse.Str(f, "gecerlilikBit")),
            Aktif = (FormParse.Str(f, "aktif") ?? "true") is "true" or "True",
            Kademeler = tiers
        };
    }

    /// <summary>Checkbox okuma: işaretsiz kutu forma HİÇ gelmez (bool bind 400 verir) → string oku.</summary>
    private static bool IsChecked(IFormCollection f, string name)
        => FormParse.Str(f, name) is "true" or "True" or "on";

    private static async Task<IResult> Run(Func<Task> action, string message)
    {
        try { await action(); return Result.Ok("/doluluk-kurallari", message); }
        catch (ValidationException ex) { return Results.Redirect($"/doluluk-kurallari?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
