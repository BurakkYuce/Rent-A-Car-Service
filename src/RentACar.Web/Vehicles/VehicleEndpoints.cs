using Microsoft.AspNetCore.Mvc;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.Web.Identity;

namespace RentACar.Web.Vehicles;

/// <summary>
/// Araç create/update/delete form post uçları. Tenant, HttpContext.User claim'inden
/// (ITenantContext → RLS) gelir; servis tenant'tan habersizdir.
/// Çok sayıda opsiyonel alan (parite zenginleştirme dahil) boş "" ile [FromForm] tipli bind 400
/// vermesin diye tüm form <see cref="IFormCollection"/>'dan okunup FormParse/Enum.TryParse ile çevrilir.
/// NOT: PR #1 smoke kolaylığı için antiforgery devre dışı — ÜRETİMDE açılmalı (follow-up).
/// </summary>
public static class VehicleEndpoints
{
    public static IEndpointRouteBuilder MapVehicleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/vehicles").RequirePermission(Permission.OperationsWrite).AntiforgeryByEnv();

        group.MapPost("/create", async (VehicleService svc, HttpRequest req) =>
        {
            try { await svc.CreateAsync(Build(req.Form)); return Results.Redirect("/vehicles"); }
            catch (ValidationException ex) { return Results.Redirect($"/vehicles?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        group.MapPost("/update", async (VehicleService svc, HttpRequest req, [FromForm] Guid id) =>
        {
            try
            {
                var ok = await svc.UpdateAsync(id, Build(req.Form));
                return ok ? Results.Redirect("/vehicles") : Results.NotFound();
            }
            catch (ValidationException ex) { return Results.Redirect($"/vehicles/{id}?hata={Uri.EscapeDataString(ex.Message)}"); }
        });

        group.MapPost("/delete", async (VehicleService svc, [FromForm] Guid id) =>
        {
            await svc.DeleteAsync(id);
            return Results.Redirect("/vehicles");
        });

        return app;
    }

    private static VehicleInput Build(IFormCollection f) => new()
    {
        Plaka = f["plaka"].ToString(),
        Marka = FormParse.Str(f, "marka"),
        Tip = FormParse.Str(f, "tip"),
        Grup = FormParse.Str(f, "grup"),
        Segment = FormParse.Str(f, "segment"),
        Sipp = FormParse.Str(f, "sipp"),
        Renk = FormParse.Str(f, "renk"),
        ModelYili = FormParse.Int(FormParse.Str(f, "modelYili")),
        Vites = ParseEnum<Vites>(FormParse.Str(f, "vites")),
        SasiNo = FormParse.Str(f, "sasiNo"),
        MotorNo = FormParse.Str(f, "motorNo"),
        Sube = FormParse.Str(f, "sube"),
        Durum = ParseEnum<VehicleStatus>(FormParse.Str(f, "durum")) ?? VehicleStatus.Stokta,
        FiloDurum = ParseEnum<FiloStatus>(FormParse.Str(f, "filoDurum")),
        Km = FormParse.Int(FormParse.Str(f, "km")) ?? 0,
        Yakit = ParseEnum<FuelType>(FormParse.Str(f, "yakit")) ?? FuelType.Benzin,
        // Parite zenginleştirme
        MotorGucu = FormParse.Int(FormParse.Str(f, "motorGucu")),
        SilindirHacmi = FormParse.Int(FormParse.Str(f, "silindirHacmi")),
        RuhsatNo = FormParse.Str(f, "ruhsatNo"),
        TescilTarihi = FormParse.Date(FormParse.Str(f, "tescilTarihi")),
        AracSahibi = FormParse.Str(f, "aracSahibi"),
        AlimBedeli = FormParse.Dec(FormParse.Str(f, "alimBedeli")),
        AlimTarihi = FormParse.Date(FormParse.Str(f, "alimTarihi")),
        AlisVergisiz = FormParse.Dec(FormParse.Str(f, "alisVergisiz")),
        AlisOtv = FormParse.Dec(FormParse.Str(f, "alisOtv")),
        AlisKdv = FormParse.Dec(FormParse.Str(f, "alisKdv")),
        AylikMaliyet = FormParse.Dec(FormParse.Str(f, "aylikMaliyet")),
        FiloYonetimMaliyeti = FormParse.Dec(FormParse.Str(f, "filoYonetimMaliyeti")),
        IkinciElDeger = FormParse.Dec(FormParse.Str(f, "ikinciElDeger")),
        FiloGirisTarih = FormParse.Date(FormParse.Str(f, "filoGirisTarih")),
        FiloCikisTarih = FormParse.Date(FormParse.Str(f, "filoCikisTarih")),
        OzelKod1 = FormParse.Str(f, "ozelKod1"),
        OzelKod2 = FormParse.Str(f, "ozelKod2"),
        OzelKod3 = FormParse.Str(f, "ozelKod3"),
        OzelKod4 = FormParse.Str(f, "ozelKod4"),
        OzelKod5 = FormParse.Str(f, "ozelKod5"),
        HgsNo = FormParse.Str(f, "hgsNo"),
        OgsNo = FormParse.Str(f, "ogsNo"),
        KasaTipi = FormParse.Str(f, "kasaTipi"),
        DetayTipi = FormParse.Str(f, "detayTipi"),
        AlimFaturaNo = FormParse.Str(f, "alimFaturaNo"),
        AlimYapilanFirma = FormParse.Str(f, "alimYapilanFirma"),
        KiraKmLimiti = FormParse.Int(FormParse.Str(f, "kiraKmLimiti")),
        // roadmap K2 — operasyon bayrakları + bakım/lastik
        WebRezKapat = Flag(f, "webRezKapat"),
        OfisRezKapat = Flag(f, "ofisRezKapat"),
        ZIzni = Flag(f, "zIzni"),
        Utts = Flag(f, "utts"),
        KarLastigi = Flag(f, "karLastigi"),
        YedekAnahtar = Flag(f, "yedekAnahtar"),
        Temizlik = Flag(f, "temizlik"),
        Rehin = Flag(f, "rehin"),
        SonBakimTarih = FormParse.Date(FormParse.Str(f, "sonBakimTarih")),
        SonBakimKm = FormParse.Int(FormParse.Str(f, "sonBakimKm")),
        LastikDurumu = FormParse.Str(f, "lastikDurumu")
    };

    /// <summary>Checkbox: değer "true"/"on" ise true; yoksa false.</summary>
    private static bool Flag(IFormCollection f, string key)
    {
        var v = f[key].ToString();
        return v is "true" or "on" or "True";
    }


    /// <summary>Boş/whitespace/geçersiz → null; aksi halde enum değeri.</summary>
    private static T? ParseEnum<T>(string? s) where T : struct, Enum
        => Enum.TryParse<T>((s ?? string.Empty).Trim(), out var v) ? v : null;
}
