using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.VehicleGroups;
using RentACar.Web.Identity;

namespace RentACar.Web.VehicleGroups;

/// <summary>Araç grubu (tanım + fiyat-kural) master form post uçları. OperationsWrite.
/// Opsiyonel sayısal kural alanları (koltuk/yaş/provizyon/KM…) boş "" ile 400 vermesin diye
/// <c>string?</c> alınıp FormParse.Int/Dec ile çevrilir (boş → null).</summary>
public static class VehicleGroupEndpoints
{
    public static IEndpointRouteBuilder MapVehicleGroupEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/arac-gruplari").RequirePermission(Permission.OperationsWrite).DisableAntiforgery();

        grp.MapPost("/create", async (VehicleGroupService svc,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? aciklama,
            [FromForm] string? sipp, [FromForm] string? segment, [FromForm] string? kasaTuru,
            [FromForm] string? koltukSayisi, [FromForm] string? kapiSayisi, [FromForm] string? bagajSayisi,
            [FromForm] string? surucuMinYas, [FromForm] string? gencSurucuYas, [FromForm] string? ehliyetMinYil,
            [FromForm] string? provizyon, [FromForm] string? muafiyetTutari, [FromForm] string? gunlukKmLimiti,
            [FromForm] string? asimKmUcreti) =>
            await Run(() => svc.CreateAsync(Build(kod, ad, aciklama, sipp, segment, kasaTuru,
                koltukSayisi, kapiSayisi, bagajSayisi, surucuMinYas, gencSurucuYas, ehliyetMinYil,
                provizyon, muafiyetTutari, gunlukKmLimiti, asimKmUcreti, aktif: true))));

        grp.MapPost("/update", async (VehicleGroupService svc, [FromForm] Guid id,
            [FromForm] string kod, [FromForm] string ad, [FromForm] string? aciklama,
            [FromForm] string? sipp, [FromForm] string? segment, [FromForm] string? kasaTuru,
            [FromForm] string? koltukSayisi, [FromForm] string? kapiSayisi, [FromForm] string? bagajSayisi,
            [FromForm] string? surucuMinYas, [FromForm] string? gencSurucuYas, [FromForm] string? ehliyetMinYil,
            [FromForm] string? provizyon, [FromForm] string? muafiyetTutari, [FromForm] string? gunlukKmLimiti,
            [FromForm] string? asimKmUcreti, [FromForm] bool aktif) =>
            await Run(() => svc.UpdateAsync(id, Build(kod, ad, aciklama, sipp, segment, kasaTuru,
                koltukSayisi, kapiSayisi, bagajSayisi, surucuMinYas, gencSurucuYas, ehliyetMinYil,
                provizyon, muafiyetTutari, gunlukKmLimiti, asimKmUcreti, aktif))));

        grp.MapPost("/delete", async (VehicleGroupService svc, [FromForm] Guid id) =>
            await Run(() => svc.DeleteAsync(id)));

        return app;
    }

    private static VehicleGroupInput Build(string kod, string ad, string? aciklama, string? sipp,
        string? segment, string? kasaTuru, string? koltukSayisi, string? kapiSayisi, string? bagajSayisi,
        string? surucuMinYas, string? gencSurucuYas, string? ehliyetMinYil, string? provizyon,
        string? muafiyetTutari, string? gunlukKmLimiti, string? asimKmUcreti, bool aktif)
        => new()
        {
            Kod = kod, Ad = ad, Aciklama = aciklama,
            Sipp = sipp, Segment = segment, KasaTuru = kasaTuru,
            KoltukSayisi = FormParse.Int(koltukSayisi), KapiSayisi = FormParse.Int(kapiSayisi),
            BagajSayisi = FormParse.Int(bagajSayisi), SurucuMinYas = FormParse.Int(surucuMinYas),
            GencSurucuYas = FormParse.Int(gencSurucuYas), EhliyetMinYil = FormParse.Int(ehliyetMinYil),
            Provizyon = FormParse.Dec(provizyon), MuafiyetTutari = FormParse.Dec(muafiyetTutari),
            GunlukKmLimiti = FormParse.Int(gunlukKmLimiti), AsimKmUcreti = FormParse.Dec(asimKmUcreti),
            Aktif = aktif
        };

    private static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Redirect("/arac-gruplari"); }
        catch (ValidationException ex) { return Results.Redirect($"/arac-gruplari?hata={Uri.EscapeDataString(ex.Message)}"); }
    }
}
