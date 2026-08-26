using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.DolulukFiyat;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.DolulukFiyat;

/// <summary>Doluluk fiyat kuralı master form post uçları (FAZ 3.A7). OperationsWrite.</summary>
public static class DolulukFiyatEndpoints
{
    public static IEndpointRouteBuilder MapDolulukFiyatEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/doluluk-kurallari").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (DolulukFiyatKuralService svc, HttpRequest req) =>
            await Run(() => svc.CreateAsync(Build(req.Form)), "Kayıt eklendi."));

        grp.MapPost("/update", async (DolulukFiyatKuralService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(() => svc.UpdateAsync(id, Build(req.Form)), "Değişiklikler kaydedildi."));

        grp.MapPost("/delete", async (DolulukFiyatKuralService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id), "Kayıt silindi."));

        // FAZ-73 — toplu kademe girişi (canlı doluluk_algoritma.aspx): ortak kapsam + 10 satır.
        grp.MapPost("/toplu", async (DolulukFiyatKuralService svc, HttpRequest req) =>
            await Run(() => svc.TopluCreateAsync(BuildToplu(req.Form)), "Toplu işlem uygulandı."));

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
        SadeceKendiSubeleri = Isaretli(f, "sadeceKendiSubeleri"),
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
    private static DolulukTopluInput BuildToplu(IFormCollection f)
    {
        var kademeler = new List<DolulukKademeSatiri>();
        for (var i = 0; i < 10; i++)
        {
            var esik = FormParse.Str(f, $"esik_{i}");
            var carpan = FormParse.Str(f, $"carpan_{i}");
            if (esik is null && carpan is null) continue;
            kademeler.Add(new DolulukKademeSatiri(
                FormParse.Int(esik) ?? 0, FormParse.Dec(carpan) ?? 0m));
        }

        return new DolulukTopluInput
        {
            KodOnEk = f["kodOnEk"].ToString(),
            AdOnEk = f["adOnEk"].ToString(),
            AracGrupKod = FormParse.Str(f, "aracGrupKod"),
            Sube = FormParse.Str(f, "sube"),
            SadeceKendiSubeleri = Isaretli(f, "sadeceKendiSubeleri"),
            GecerlilikBas = FormParse.Date(FormParse.Str(f, "gecerlilikBas")),
            GecerlilikBit = FormParse.Date(FormParse.Str(f, "gecerlilikBit")),
            Aktif = (FormParse.Str(f, "aktif") ?? "true") is "true" or "True",
            Kademeler = kademeler
        };
    }

    /// <summary>Checkbox okuma: işaretsiz kutu forma HİÇ gelmez (bool bind 400 verir) → string oku.</summary>
    private static bool Isaretli(IFormCollection f, string ad)
        => FormParse.Str(f, ad) is "true" or "True" or "on";

    private static async Task<IResult> Run(Func<Task> action, string mesaj)
    {
        try { await action(); return Sonuc.Tamam("/doluluk-kurallari", mesaj); }
        catch (ValidationException ex) { return Results.Redirect($"/doluluk-kurallari?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
