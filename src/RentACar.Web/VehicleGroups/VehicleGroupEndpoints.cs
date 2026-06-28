using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.VehicleGroups;
using RentACar.Web.Identity;

namespace RentACar.Web.VehicleGroups;

/// <summary>Araç grubu (tanım + fiyat-kural) master form post uçları. OperationsWrite.
/// Çok sayıda opsiyonel sayısal kural alanı boş "" ile [FromForm] tipli bind 400 vermesin diye
/// tüm form <see cref="IFormCollection"/>'dan okunup FormParse.Int/Dec ile çevrilir (boş → null).</summary>
public static class VehicleGroupEndpoints
{
    public static IEndpointRouteBuilder MapVehicleGroupEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/arac-gruplari").RequirePermission(Permission.OperationsWrite).DisableAntiforgery();

        grp.MapPost("/create", async (VehicleGroupService svc, HttpRequest req) =>
            await Run(() => svc.CreateAsync(Build(req.Form, aktif: true))));

        grp.MapPost("/update", async (VehicleGroupService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(() => svc.UpdateAsync(id, Build(req.Form, aktif: Bool(req.Form, "aktif") ?? true))));

        grp.MapPost("/delete", async (VehicleGroupService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static VehicleGroupInput Build(IFormCollection f, bool aktif) => new()
    {
        Kod = f["kod"].ToString(),
        Ad = f["ad"].ToString(),
        Aciklama = Str(f, "aciklama"),
        Sipp = Str(f, "sipp"),
        Segment = Str(f, "segment"),
        KasaTuru = Str(f, "kasaTuru"),
        Marka = Str(f, "marka"),
        Tipi = Str(f, "tipi"),
        KoltukSayisi = FormParse.Int(Str(f, "koltukSayisi")),
        KapiSayisi = FormParse.Int(Str(f, "kapiSayisi")),
        BagajSayisi = FormParse.Int(Str(f, "bagajSayisi")),
        KucukBagaj = FormParse.Int(Str(f, "kucukBagaj")),
        BuyukBagaj = FormParse.Int(Str(f, "buyukBagaj")),
        SurucuMinYas = FormParse.Int(Str(f, "surucuMinYas")),
        GencSurucuYas = FormParse.Int(Str(f, "gencSurucuYas")),
        EhliyetMinYil = FormParse.Int(Str(f, "ehliyetMinYil")),
        GencEhliyetMinYil = FormParse.Int(Str(f, "gencEhliyetMinYil")),
        Provizyon = FormParse.Dec(Str(f, "provizyon")),
        Provizyon2 = FormParse.Dec(Str(f, "provizyon2")),
        MuafiyetTutari = FormParse.Dec(Str(f, "muafiyetTutari")),
        Muafiyet2 = FormParse.Dec(Str(f, "muafiyet2")),
        GunlukKmLimiti = FormParse.Int(Str(f, "gunlukKmLimiti")),
        AylikMaxKm = FormParse.Int(Str(f, "aylikMaxKm")),
        AsimKmUcreti = FormParse.Dec(Str(f, "asimKmUcreti")),
        YakitFiyati = FormParse.Dec(Str(f, "yakitFiyati")),
        SonraOdeOran = FormParse.Dec(Str(f, "sonraOdeOran")),
        KrediKartiSart = Bool(f, "krediKartiSart"),
        WebSira = FormParse.Int(Str(f, "webSira")),
        UpgradeSira = FormParse.Int(Str(f, "upgradeSira")),
        Aktif = aktif
    };

    private static string? Str(IFormCollection f, string key)
    {
        var v = f[key].ToString();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    /// <summary>Boş → null, "true"/"evet"/"on" → true, diğer dolu → false (3 durumlu nullable bool select).</summary>
    private static bool? Bool(IFormCollection f, string key)
    {
        var v = Str(f, key);
        if (v is null) return null;
        return v is "true" or "True" or "evet" or "Evet" or "on";
    }

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/arac-gruplari"); }
        catch (ValidationException ex) { return Results.Redirect($"/arac-gruplari?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
