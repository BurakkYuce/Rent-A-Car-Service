using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Locations;
using RentACar.Domain.Entities;
using RentACar.Web.Identity;

namespace RentACar.Web.Locations;

/// <summary>Ofis/Lokasyon master form post uçları. OperationsWrite (operasyonel yapılandırma).</summary>
public static class LocationEndpoints
{
    public static IEndpointRouteBuilder MapLocationEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/lokasyonlar").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        // FAZ-22: alan sayısı 20'yi geçtiği için tek tek [FromForm] parametre yerine IFormCollection
        // okunuyor — opsiyonel sayısal alanlar "" ile 400 vermesin diye zaten FormParse gerekiyordu.
        grp.MapPost("/create", async (LocationService svc, HttpRequest req) =>
            await Run(() => svc.CreateAsync(Build(req.Form, aktif: true))));

        grp.MapPost("/update", async (LocationService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(() => svc.UpdateAsync(id, Build(req.Form, aktif: Bool(req.Form, "aktif")))));

        grp.MapPost("/delete", async (LocationService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static bool Bool(IFormCollection f, string key)
        => string.Equals(f[key].ToString(), "true", StringComparison.OrdinalIgnoreCase);

    private static LocationInput Build(IFormCollection f, bool aktif) => new()
    {
        Kod = f["kod"].ToString(),
        Ad = f["ad"].ToString(),
        Adres = FormParse.Str(f, "adres"),
        Telefon = FormParse.Str(f, "telefon"),
        Eposta = FormParse.Str(f, "eposta"),
        CalismaSaatleri = FormParse.Str(f, "calismaSaatleri"),
        TeslimUcreti = FormParse.Dec(FormParse.Str(f, "teslimUcreti")),
        Sube = FormParse.Str(f, "sube"),
        IngilizceAd = FormParse.Str(f, "ingilizceAd"),
        BulusmaNoktasi = FormParse.Str(f, "bulusmaNoktasi"),
        Iata = FormParse.Str(f, "iata"),
        WebdeGizle = Bool(f, "webdeGizle"),
        LokasyonTuru = FormParse.Str(f, "lokasyonTuru"),
        BinaNo = FormParse.Str(f, "binaNo"),
        Tarif = FormParse.Str(f, "tarif"),
        Ulke = FormParse.Str(f, "ulke"),
        PostaKodu = FormParse.Str(f, "postaKodu"),
        MapsKonumu = FormParse.Str(f, "mapsKonumu"),
        EkAciklama = FormParse.Str(f, "ekAciklama"),
        WebSira = FormParse.Int(FormParse.Str(f, "webSira")),
        DropKarsilamaTuru = FormParse.Str(f, "dropKarsilamaTuru"),
        DropCalismaSekli = FormParse.Str(f, "dropCalismaSekli"),
        OzelMail = FormParse.Str(f, "ozelMail"),
        OzelTelefon = FormParse.Str(f, "ozelTelefon"),
        HaftalikCalismaSaatleri = Hafta(f),
        Aktif = aktif
    };

    /// <summary>
    /// Haftalık saatler: form 7 satırı <c>gun{n}Acilis</c>/<c>gun{n}Kapanis</c>/<c>gun{n}Kapali</c>
    /// adlarıyla gönderir. Checkbox işaretsizken TARAYICI HİÇBİR ŞEY GÖNDERMEZ — bu yüzden "kapalı"
    /// varlık kontrolüyle okunur, değer karşılaştırmasıyla değil.
    /// </summary>
    private static List<GunSaat> Hafta(IFormCollection f)
    {
        var liste = new List<GunSaat>(7);
        for (var gun = 1; gun <= 7; gun++)
            liste.Add(new GunSaat
            {
                Gun = gun,
                Acilis = FormParse.Str(f, $"gun{gun}Acilis"),
                Kapanis = FormParse.Str(f, $"gun{gun}Kapanis"),
                Kapali = f.ContainsKey($"gun{gun}Kapali")
            });
        return liste;
    }

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/lokasyonlar"); }
        catch (ValidationException ex) { return Results.Redirect($"/lokasyonlar?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
