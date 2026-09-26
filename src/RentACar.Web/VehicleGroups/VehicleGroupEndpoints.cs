using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.VehicleGroups;
using RentACar.Web.Common;
using RentACar.Web.Identity;

namespace RentACar.Web.VehicleGroups;

/// <summary>Araç grubu (tanım + fiyat-kural) master form post uçları. OperationsWrite.
/// Çok sayıda opsiyonel sayısal kural alanı boş "" ile [FromForm] tipli bind 400 vermesin diye
/// tüm form <see cref="IFormCollection"/>'dan okunup FormParse.Int/Dec ile çevrilir (boş → null).</summary>
public static class VehicleGroupEndpoints
{
    public static IEndpointRouteBuilder MapVehicleGroupEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/arac-gruplari").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        grp.MapPost("/create", async (VehicleGroupService svc, HttpRequest req) =>
            await Run(() => svc.CreateAsync(Build(req.Form, aktif: true)), "Kayıt eklendi."));

        grp.MapPost("/update", async (VehicleGroupService svc, HttpRequest req, [FromForm] Guid id) =>
            await Run(() => svc.UpdateAsync(id, Build(req.Form, aktif: Bool(req.Form, "aktif") ?? true)), "Değişiklikler kaydedildi."));

        grp.MapPost("/delete", async (VehicleGroupService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id), "Kayıt silindi."));

        // PR-10 eşleme aracı: tanımsız bir Grup serbest-metin değerini tanımlı gruba taşır.
        // "(boş)" satırı string yerine `bos=true` bayrağıyla gelir (gerçekten "(boş)" yazan bir
        // grup değeriyle karışmasın diye).
        grp.MapPost("/ata", async (VehicleGroupService svc, HttpRequest req,
            [FromForm] Guid hedefGrupId, [FromForm] string? kaynak, [FromForm] bool? bos) =>
        {
            try
            {
                var n = await svc.AssignGroupValueAsync(kaynak, bos ?? false, hedefGrupId);
                return Results.Redirect("/arac-gruplari?bilgi=" +
                    Uri.EscapeDataString($"{n} araç '{kaynak}' değerinden taşındı."));
            }
            catch (ValidationException ex)
            {
                return Results.Redirect("/arac-gruplari?hata=" + Uri.EscapeDataString(ex.Message));
            }
        });

        return app;
    }

    private static VehicleGroupInput Build(IFormCollection f, bool aktif) => new()
    {
        Kod = f["kod"].ToString(),
        Ad = f["ad"].ToString(),
        Aciklama = FormParse.Str(f, "aciklama"),
        Sipp = FormParse.Str(f, "sipp"),
        Segment = FormParse.Str(f, "segment"),
        KasaTuru = FormParse.Str(f, "kasaTuru"),
        Marka = FormParse.Str(f, "marka"),
        Tipi = FormParse.Str(f, "tipi"),
        KoltukSayisi = FormParse.Int(FormParse.Str(f, "koltukSayisi")),
        KapiSayisi = FormParse.Int(FormParse.Str(f, "kapiSayisi")),
        BagajSayisi = FormParse.Int(FormParse.Str(f, "bagajSayisi")),
        KucukBagaj = FormParse.Int(FormParse.Str(f, "kucukBagaj")),
        BuyukBagaj = FormParse.Int(FormParse.Str(f, "buyukBagaj")),
        SurucuMinYas = FormParse.Int(FormParse.Str(f, "surucuMinYas")),
        GencSurucuYas = FormParse.Int(FormParse.Str(f, "gencSurucuYas")),
        GencSurucuUcretGunluk = FormParse.Dec(FormParse.Str(f, "gencSurucuUcretGunluk")),
        EkSurucuUcretGunluk = FormParse.Dec(FormParse.Str(f, "ekSurucuUcretGunluk")),
        EhliyetMinYil = FormParse.Int(FormParse.Str(f, "ehliyetMinYil")),
        GencEhliyetMinYil = FormParse.Int(FormParse.Str(f, "gencEhliyetMinYil")),
        Provizyon = FormParse.Dec(FormParse.Str(f, "provizyon")),
        Provizyon2 = FormParse.Dec(FormParse.Str(f, "provizyon2")),
        MuafiyetTutari = FormParse.Dec(FormParse.Str(f, "muafiyetTutari")),
        Muafiyet2 = FormParse.Dec(FormParse.Str(f, "muafiyet2")),
        GunlukKmLimiti = FormParse.Int(FormParse.Str(f, "gunlukKmLimiti")),
        AylikMaxKm = FormParse.Int(FormParse.Str(f, "aylikMaxKm")),
        AsimKmUcreti = FormParse.Dec(FormParse.Str(f, "asimKmUcreti")),
        YakitFiyati = FormParse.Dec(FormParse.Str(f, "yakitFiyati")),
        SonraOdeOran = FormParse.Dec(FormParse.Str(f, "sonraOdeOran")),
        KrediKartiSart = Bool(f, "krediKartiSart"),
        WebSira = FormParse.Int(FormParse.Str(f, "webSira")),
        UpgradeSira = FormParse.Int(FormParse.Str(f, "upgradeSira")),
        ProvizyonDoviz = FormParse.Str(f, "provizyonDoviz"),
        Provizyon2Doviz = FormParse.Str(f, "provizyon2Doviz"),
        YakitTuru = Enum.TryParse<RentACar.Domain.Enums.FuelType>(FormParse.Str(f, "yakitTuru"), out var yt) ? yt : null,
        Vites = Enum.TryParse<RentACar.Domain.Enums.Transmission>(FormParse.Str(f, "vites"), out var vt) ? vt : null,
        EntegrasyonKod1 = FormParse.Str(f, "entegrasyonKod1"),
        WebId = FormParse.Str(f, "webId"),
        ServisId = FormParse.Str(f, "servisId"),
        Aktif = aktif
    };


    /// <summary>Boş → null, "true"/"evet"/"on" → true, diğer dolu → false (3 durumlu nullable bool select).</summary>
    private static bool? Bool(IFormCollection f, string key)
    {
        var v = FormParse.Str(f, key);
        if (v is null) return null;
        return v is "true" or "True" or "evet" or "Evet" or "on";
    }

    private static async Task<IResult> Run(Func<Task> action, string mesaj)
    {
        try { await action(); return Sonuc.Tamam("/arac-gruplari", mesaj); }
        catch (ValidationException ex) { return Results.Redirect($"/arac-gruplari?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
